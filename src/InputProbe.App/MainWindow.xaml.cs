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
    private readonly JsonConfigStore _store = new(Path.Combine(AppContext.BaseDirectory, "config.json"));
    private readonly RuntimeController _runtime;
    private KeyProfile _selectedProfile;
    private string _stateText = "Stopped";
    private string _lastMessage = "未启动";
    private readonly LowLevelKeyboardHook _keyboardHook;

    public List<KeyProfile> Profiles { get; } = [new KeyProfile { Name = "基础序列", TargetProcess = "jx3client", Steps = [new("D1"), new("D2")] }];
    public KeyProfile SelectedProfile { get => _selectedProfile; set { _selectedProfile = value; OnPropertyChanged(); } }
    public string StateText { get => _stateText; private set { _stateText = value; OnPropertyChanged(); } }
    public string LastMessage { get => _lastMessage; private set { _lastMessage = value; OnPropertyChanged(); } }
    public ICommand SaveCommand { get; }
    public ICommand ArmCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand TriggerCommand { get; }

    public MainViewModel()
    {
        _selectedProfile = Profiles[0];
        var sender = new SendInputKeySender();
        _runtime = new RuntimeController(new SequenceRunner(sender, new ForegroundWindowGuard()), sender);
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
    }

    private async Task SaveAsync()
    {
        await _store.SaveAsync(new AppConfig { Profiles = Profiles });
        LastMessage = "配置已保存";
    }

    private void OnGlobalKeyChanged(int virtualKey, bool isDown)
    {
        if (!isDown || !SelectedProfile.Enabled || _runtime.State != RuntimeState.Armed) return;
        var key = virtualKey switch
        {
            >= 0x70 and <= 0x7B => $"F{virtualKey - 0x6F}",
            >= 0x30 and <= 0x39 => $"D{virtualKey - 0x30}",
            0x20 => "Space",
            0x0D => "Enter",
            0x1B => "Escape",
            _ => string.Empty
        };
        if (string.Equals(key, SelectedProfile.Trigger, StringComparison.OrdinalIgnoreCase))
            _ = _runtime.TriggerAsync(SelectedProfile);
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
