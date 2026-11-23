using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using InputProbe.Core;
using InputProbe.Windows;

namespace InputProbe.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        MainViewModel _viewModel;
        try { _viewModel = new MainViewModel(); }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            MessageBox.Show($"无法启用所选输入后端：{exception.Message}", "Input Probe", MessageBoxButton.OK, MessageBoxImage.Error);
            Application.Current.Shutdown(1);
            return;
        }
        DataContext = _viewModel;
        AddHandler(System.Windows.Controls.Validation.ErrorEvent, new EventHandler<System.Windows.Controls.ValidationErrorEventArgs>((_, e) =>
            _viewModel.ChangeValidationErrors(e.Action == System.Windows.Controls.ValidationErrorEventAction.Added ? 1 : -1)));
        PreviewKeyDown += (_, args) =>
        {
            if (_viewModel.CaptureText.Length == 0) return;
            if (!args.IsRepeat)
                _viewModel.HandleKeyCode(KeyInterop.VirtualKeyFromKey(args.Key == Key.System ? args.SystemKey : args.Key));
            args.Handled = true;
        };
        PreviewKeyUp += (_, _) => _viewModel.FinishCaptureRelease();
        Deactivated += (_, _) => _viewModel.CancelCapture();
        SourceInitialized += (_, _) => _viewModel.AttachHotkeys(new WindowInteropHelper(this).Handle);
        Closed += (_, _) => _viewModel.Dispose();
    }
}

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private enum CaptureMode { None, Start, Stop, Sequence }
    private readonly JsonConfigStore _store = new(Path.Combine(AppContext.BaseDirectory, "config.json"));
    private readonly RuntimeController _runtime;
    private KeyProfile _selectedProfile;
    private string _stateText = "已停止";
    private string _lastMessage = "未启动";
    private KeyStep? _selectedStep;
    private CaptureMode _captureMode;
    private string? _configLoadMessage;
    private SystemHotkeys? _systemHotkeys;
    private bool _startRegistered, _stopRegistered, _registerOnRelease;
    private bool _disposed;
    private readonly IInputLogger _logger;
    private readonly RunFeedback _feedback = new();
    private bool _isRunning;
    private bool _isPinned;
    private int _validationErrors;
    public bool IsRunning => _isRunning;
    public bool IsPinned { get => _isPinned; set { _isPinned = value; OnPropertyChanged(); } }
    public string WindowTitle => IsRunning ? "● 正在重复 — Input Probe" : "■ 已停止 — Input Probe";
    public void ChangeValidationErrors(int delta)
    {
        _validationErrors = Math.Max(0, _validationErrors + delta);
        CommandManager.InvalidateRequerySuggested();
    }
    private string _hotkeyStatus = "正在注册快捷键";
    public string HotkeyStatus { get => _hotkeyStatus; private set { _hotkeyStatus = value; OnPropertyChanged(); } }


    public List<KeyProfile> Profiles { get; private set; }
    public KeyProfile SelectedProfile { get => _selectedProfile; set { if (value is null) return; _selectedProfile = value; OnPropertyChanged(); ConfigureHotkeys(); } }
    public KeyStep? SelectedStep { get => _selectedStep; set { _selectedStep = value; OnPropertyChanged(); } }
    public bool CanEdit => !_runtime.IsBusy;
    public string StateText { get => _stateText; private set { _stateText = value; OnPropertyChanged(); } }
    public string LastMessage { get => _lastMessage; private set { _lastMessage = value; OnPropertyChanged(); } }
    public ICommand SaveCommand { get; }
    public ICommand ArmCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand AddStepCommand { get; }
    public ICommand RemoveStepCommand { get; }
    public ICommand MoveStepUpCommand { get; }
    public ICommand MoveStepDownCommand { get; }
    public ICommand CaptureStartCommand { get; }
    public ICommand CaptureStopCommand { get; }
    public ICommand CaptureSequenceCommand { get; }
    public ICommand UseVerifiedPresetCommand { get; }
    public string CaptureText => _captureMode == CaptureMode.None ? "" : "请按下一个按键...";

    public MainViewModel()
    {
        AppConfig loaded;
        try
        {
            loaded = _store.LoadAsync().GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            loaded = new AppConfig { Profiles = [new KeyProfile { Name = "数字 8 · 80ms", Steps = [new("D8", 80)] }] };
            _configLoadMessage = $"配置加载失败，已使用默认配置：{exception.Message}";
        }
        Profiles = loaded.Profiles.Count > 0
            ? loaded.Profiles
            : [new KeyProfile { Name = "数字 8 · 80ms", Steps = [new("D8", 80)] }];
        _selectedProfile = Profiles[0];
        var logger = _logger = new FileInputLogger(Path.Combine(AppContext.BaseDirectory, "input-probe.log"));
        var arguments = Environment.GetCommandLineArgs();
        var ddIndex = Array.FindIndex(arguments, arg => arg.Equals("--dd", StringComparison.OrdinalIgnoreCase));
        if (ddIndex >= 0 && ddIndex + 1 >= arguments.Length) throw new InvalidOperationException("--dd 后需要 DLL 完整路径");
        var dllPath = ddIndex >= 0 ? arguments[ddIndex + 1] : FindDdLibrary();
        DdKeySender sender;
        try
        {
            sender = new DdKeySender(new DdNativeKeyboard(dllPath));
            logger.Info($"DD 初始化成功，DLL={Path.GetFullPath(dllPath)}");
        }
        catch (Exception exception) { logger.Error("DD 初始化失败", exception); throw; }
        _runtime = new RuntimeController(new SequenceRunner(sender), sender, logger);
        _runtime.StateChanged += state => Application.Current.Dispatcher.Invoke(() =>
        {
            var running = state == RuntimeState.Running;
            if (_isRunning != running)
            {
                _isRunning = running;
                _feedback.Play(running, _logger);
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(WindowTitle));
            }
            StateText = state == RuntimeState.Running ? "运行中" : "已停止";
            OnPropertyChanged(nameof(CanEdit));
            CommandManager.InvalidateRequerySuggested();
            LastMessage = _runtime.LastMessage;
        });
        _logger.Info("启动：不安装键盘 Hook；快捷键使用 RegisterHotKey，录入使用 WPF 窗口事件");
        if (_configLoadMessage is not null) LastMessage = _configLoadMessage;
        SaveCommand = new RelayCommand(async _ => await SaveAsync(), _ => CanEdit && _validationErrors == 0);
        ArmCommand = new RelayCommand(_ => StartSequence(), _ => CanEdit && _validationErrors == 0 && _captureMode == CaptureMode.None && SelectedProfile.Steps.Count > 0);
        UseVerifiedPresetCommand = new RelayCommand(_ =>
        {
            var preset = new KeyProfile { Name = "数字 8 · 80ms", Steps = [new("D8", 80)] };
            Profiles = [.. Profiles, preset];
            OnPropertyChanged(nameof(Profiles));
            SelectedProfile = preset;
            LastMessage = "已新建数字 8 配置，开始后立即重复";
        }, _ => CanEdit);
        StopCommand = new RelayCommand(_ => _runtime.Stop("用户已停止"), _ => _runtime.IsBusy);
        AddStepCommand = new RelayCommand(_ => AddStep());
        RemoveStepCommand = new RelayCommand(_ => RemoveStep(), _ => CanEdit && SelectedStep is not null);
        MoveStepUpCommand = new RelayCommand(_ => MoveStep(-1), _ => CanEdit && SelectedStep is not null && SelectedProfile.Steps.IndexOf(SelectedStep) > 0);
        MoveStepDownCommand = new RelayCommand(_ => MoveStep(1), _ => CanEdit && SelectedStep is not null && SelectedProfile.Steps.IndexOf(SelectedStep) < SelectedProfile.Steps.Count - 1);
        CaptureStartCommand = new RelayCommand(_ => BeginCapture(CaptureMode.Start));
        CaptureStopCommand = new RelayCommand(_ => BeginCapture(CaptureMode.Stop));
        CaptureSequenceCommand = new RelayCommand(parameter => BeginSequenceCapture(parameter));
    }

    private async Task SaveAsync()
    {
        try
        {
            await _store.SaveAsync(new AppConfig { Version = JsonConfigStore.CurrentVersion, Profiles = Profiles });
            LastMessage = "配置已保存";
        }
        catch (Exception exception) { LastMessage = $"保存失败：{exception.Message}"; }
    }

    public void HandleKeyCode(int virtualKey)
    {
        if (_captureMode != CaptureMode.None) CompleteCapture(VirtualKeyNames.FromVirtualKey(virtualKey));
    }

    public void FinishCaptureRelease()
    {
        if (!_registerOnRelease) return;
        _registerOnRelease = false;
        ConfigureHotkeys();
    }

    public void CancelCapture()
    {
        if (_captureMode == CaptureMode.None && !_registerOnRelease) return;
        _captureMode = CaptureMode.None;
        _registerOnRelease = false;
        OnPropertyChanged(nameof(CaptureText));
        ConfigureHotkeys();
    }

    public void AttachHotkeys(nint handle)
    {
        _systemHotkeys = new SystemHotkeys(handle);
        _systemHotkeys.Pressed += id =>
        {
            if (_disposed || _captureMode != CaptureMode.None) return;
            _logger.Info($"WM_HOTKEY id={id}; {InputDiagnostics.DescribeForeground()}");
            if (id == 1) StartSequence(); else _runtime.Stop("系统热键停止");
        };
        ConfigureHotkeys();
    }

    private void ConfigureHotkeys()
    {
        if (_systemHotkeys is null || _disposed) return;
        _systemHotkeys.Clear();
        _startRegistered = RegisterProfileHotkey(1, SelectedProfile.StartHotkey);
        _stopRegistered = RegisterProfileHotkey(2, SelectedProfile.StopHotkey);
        var panicError = _systemHotkeys.Register(3, 0x79, 0x6); // Ctrl+Shift+F10; F12 is reserved.
        HotkeyStatus = $"开始：{(_startRegistered ? "系统热键" : "不可用，请重设")} · 停止：{(_stopRegistered ? "系统热键" : "不可用，请重设")} · 紧急停止：{(panicError == 0 ? "Ctrl+Shift+F10" : $"注册失败 {panicError}")}";
        _logger.Info(HotkeyStatus);
    }

    private bool RegisterProfileHotkey(int id, string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        var vk = Enumerable.Range(1, 254).FirstOrDefault(code => string.Equals(VirtualKeyNames.FromVirtualKey(code), key, StringComparison.OrdinalIgnoreCase));
        if (vk == 0 || vk == 0x7B)
        { _logger.Info($"{key}: 未注册系统热键（未知键或 F12 保留键），请改用其他按键"); return false; }
        var error = _systemHotkeys!.Register(id, (uint)vk);
        if (error != 0) { LastMessage = $"热键 {key} 注册失败（{error}），使用 不可用，请重设"; _logger.Info(LastMessage); }
        return error == 0;
    }

    private void StartSequence()
    {
        if (!CanEdit || _captureMode != CaptureMode.None) return;
        if (_validationErrors != 0) { LastMessage = "请先修正间隔输入"; return; }
        if (SelectedProfile.Steps.Any(step => step.WaitAfterMs is < 30 or > 3_600_000))
        { LastMessage = "重复间隔必须在 30 到 3600000 毫秒之间"; return; }
        if (SelectedProfile.Steps.Any(step => string.Equals(step.Key, SelectedProfile.StartHotkey, StringComparison.OrdinalIgnoreCase) || string.Equals(step.Key, SelectedProfile.StopHotkey, StringComparison.OrdinalIgnoreCase)))
        { LastMessage = "序列按键不能与开始/停止热键相同，请修改后启动"; return; }
        var repeat = new KeyProfile { Name = SelectedProfile.Name, Enabled = true,
            Steps = SelectedProfile.Steps.Select(step => new KeyStep(step.Key, step.WaitAfterMs) { HoldMs = 30 }).ToList() };
        _logger.Info($"开始重复 mode=DD-HID, hold=30ms, steps={string.Join(",", repeat.Steps.Select(s => $"{s.Key}:{s.WaitAfterMs}ms"))}; {InputDiagnostics.DescribeForeground()}");
        _runtime.Arm();
        _ = _runtime.TriggerAsync(repeat);
    }

    private void BeginCapture(CaptureMode mode)
    {
        if (!CanEdit) return;
        _systemHotkeys?.Clear();
        _startRegistered = _stopRegistered = false;
        _captureMode = mode;
        OnPropertyChanged(nameof(CaptureText));
    }

    private void CompleteCapture(string key)
    {
        if (string.IsNullOrEmpty(key)) { LastMessage = "暂不支持此按键，请换一个键"; return; }
        if ((_captureMode == CaptureMode.Start && key == SelectedProfile.StopHotkey) ||
            (_captureMode == CaptureMode.Stop && key == SelectedProfile.StartHotkey))
        { LastMessage = "开始和停止不能使用同一个键"; return; }
        if (key == "F12" && _captureMode is CaptureMode.Start or CaptureMode.Stop)
        { LastMessage = "F12 为系统保留键，请选择其他开始/停止按键"; return; }
        switch (_captureMode)
        {
            case CaptureMode.Start: SelectedProfile.StartHotkey = key; break;
            case CaptureMode.Stop: SelectedProfile.StopHotkey = key; break;
            case CaptureMode.Sequence:
                if (SelectedStep is not null) SelectedStep.Key = key;
                break;
        }
        _captureMode = CaptureMode.None;
        _registerOnRelease = true;
        // Register only after release so the captured key cannot start execution.
        OnPropertyChanged(nameof(SelectedProfile));
        OnPropertyChanged(nameof(CaptureText));
    }

    private void AddStep()
        => AddStep("D1");

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
        var selected = SelectedStep;
        var steps = SelectedProfile.Steps.ToList();
        (steps[index], steps[target]) = (steps[target], steps[index]);
        SelectedProfile.Steps = steps;
        SelectedStep = selected;
        OnPropertyChanged(nameof(SelectedProfile));
    }

    public void Dispose()
    {
        _disposed = true;
        _systemHotkeys?.Dispose();
        try { _runtime.Stop("程序退出"); }
        finally { _runtime.Shutdown(); }

    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private static string FindDdLibrary()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "dependencies", "dd", "hid.72000.dll");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("找不到 dependencies/dd/hid.72000.dll，请使用根目录启动入口");
    }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null) : ICommand
{
    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) { if (CanExecute(parameter)) execute(parameter); }
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}
