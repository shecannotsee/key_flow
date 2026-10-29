using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using InputProbe.Core;
using InputProbe.Windows;

namespace InputProbe.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        Closed += (_, _) => _viewModel.Dispose();
    }
}

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private enum CaptureMode { None, Start, Stop, Sequence }
    private readonly JsonConfigStore _store = new(Path.Combine(AppContext.BaseDirectory, "config.json"));
    private readonly RuntimeController _runtime;
    private KeyProfile _selectedProfile;
    private string _stateText = "Stopped";
    private string _lastMessage = "未启动";
    private KeyStep? _selectedStep;
    private CaptureMode _captureMode;
    private readonly LowLevelKeyboardHook _keyboardHook;

    public List<KeyProfile> Profiles { get; } = [new KeyProfile { Name = "基础序列", TargetProcess = "jx3client", Steps = [new("D1"), new("D2")] }];
    public KeyProfile SelectedProfile { get => _selectedProfile; set { _selectedProfile = value; OnPropertyChanged(); } }
    public KeyStep? SelectedStep { get => _selectedStep; set { _selectedStep = value; OnPropertyChanged(); } }
    public string StateText { get => _stateText; private set { _stateText = value; OnPropertyChanged(); } }
    public string LastMessage { get => _lastMessage; private set { _lastMessage = value; OnPropertyChanged(); } }
    public ICommand SaveCommand { get; }
    public ICommand ArmCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand TriggerCommand { get; }
    public ICommand AddStepCommand { get; }
    public ICommand RemoveStepCommand { get; }
    public ICommand MoveStepUpCommand { get; }
    public ICommand MoveStepDownCommand { get; }
    public ICommand CaptureStartCommand { get; }
    public ICommand CaptureStopCommand { get; }
    public ICommand CaptureSequenceCommand { get; }
    public string CaptureText => _captureMode == CaptureMode.None ? "" : "请按下一个按键...";

    public MainViewModel()
    {
        _selectedProfile = Profiles[0];
        var sender = new SendInputKeySender();
        _runtime = new RuntimeController(new SequenceRunner(sender), sender);
        _runtime.StateChanged += state => Application.Current.Dispatcher.Invoke(() =>
        {
            StateText = state.ToString();
            LastMessage = _runtime.LastMessage;
        });
        _keyboardHook = new LowLevelKeyboardHook();
        _keyboardHook.KeyChanged += OnGlobalKeyChanged;
        SaveCommand = new RelayCommand(async _ => await SaveAsync());
        ArmCommand = new RelayCommand(_ => { SelectedProfile.Enabled = true; _runtime.Arm(); });
        PauseCommand = new RelayCommand(_ => _runtime.Pause());
        StopCommand = new RelayCommand(_ => _runtime.Stop("用户已停止"));
        TriggerCommand = new RelayCommand(async _ => await _runtime.TriggerAsync(SelectedProfile));
        AddStepCommand = new RelayCommand(_ => AddStep());
        RemoveStepCommand = new RelayCommand(_ => RemoveStep());
        MoveStepUpCommand = new RelayCommand(_ => MoveStep(-1));
        MoveStepDownCommand = new RelayCommand(_ => MoveStep(1));
        CaptureStartCommand = new RelayCommand(_ => BeginCapture(CaptureMode.Start));
        CaptureStopCommand = new RelayCommand(_ => BeginCapture(CaptureMode.Stop));
        CaptureSequenceCommand = new RelayCommand(parameter => BeginSequenceCapture(parameter));
    }

    private async Task SaveAsync()
    {
        await _store.SaveAsync(new AppConfig { Profiles = Profiles });
        LastMessage = "配置已保存";
    }

    private void OnGlobalKeyChanged(int virtualKey, bool isDown)
    {
        if (!isDown) return;
        var key = VirtualKeyNames.FromVirtualKey(virtualKey);
        if (_captureMode != CaptureMode.None)
        {
            Application.Current.Dispatcher.Invoke(() => CompleteCapture(key));
            return;
        }
        if (string.Equals(key, SelectedProfile.StopHotkey, StringComparison.OrdinalIgnoreCase))
        {
            _runtime.Stop("按下结束按键");
            return;
        }
        if (SelectedProfile.Enabled && string.Equals(key, SelectedProfile.StartHotkey, StringComparison.OrdinalIgnoreCase))
        {
            if (_runtime.State == RuntimeState.Stopped) _runtime.Arm();
            if (_runtime.State == RuntimeState.Armed) _ = _runtime.TriggerAsync(SelectedProfile);
        }
    }

    private void BeginCapture(CaptureMode mode)
    {
        _captureMode = mode;
        OnPropertyChanged(nameof(CaptureText));
    }

    private void CompleteCapture(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        switch (_captureMode)
        {
            case CaptureMode.Start: SelectedProfile.StartHotkey = key; break;
            case CaptureMode.Stop: SelectedProfile.StopHotkey = key; break;
            case CaptureMode.Sequence:
                if (SelectedStep is not null) SelectedStep.Key = key;
                break;
        }
        _captureMode = CaptureMode.None;
        OnPropertyChanged(nameof(SelectedProfile));
        OnPropertyChanged(nameof(CaptureText));
    }

    private void AddStep()
        => AddStep("");

    private void AddStep(string key)
    {
        SelectedProfile.Steps = [.. SelectedProfile.Steps, new KeyStep(key)];
        OnPropertyChanged(nameof(SelectedProfile));
    }

    private void BeginSequenceCapture(object? parameter)
    {
        if (parameter is KeyStep step) SelectedStep = step;
        if (SelectedStep is null)
        {
            AddStep();
            SelectedStep = SelectedProfile.Steps[^1];
        }
        BeginCapture(CaptureMode.Sequence);
    }

    private void RemoveStep()
    {
        if (SelectedStep is null) return;
        SelectedProfile.Steps = SelectedProfile.Steps.Where(step => !ReferenceEquals(step, SelectedStep)).ToList();
        SelectedStep = null;
        OnPropertyChanged(nameof(SelectedProfile));
    }

    private void MoveStep(int offset)
    {
        if (SelectedStep is null) return;
        var index = SelectedProfile.Steps.IndexOf(SelectedStep);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= SelectedProfile.Steps.Count) return;
        (SelectedProfile.Steps[index], SelectedProfile.Steps[target]) = (SelectedProfile.Steps[target], SelectedProfile.Steps[index]);
        OnPropertyChanged(nameof(SelectedProfile));
    }

    public void Dispose()
    {
        _runtime.Stop("程序退出");
        _keyboardHook.Dispose();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class RelayCommand(Action<object?> execute) : ICommand
{
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute(parameter);
    public event EventHandler? CanExecuteChanged;
}
