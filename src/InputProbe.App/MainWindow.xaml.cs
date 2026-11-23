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
            new FileInputLogger(Path.Combine(AppContext.BaseDirectory, "input-probe.log")).Error("窗口启动失败", exception);
            MessageBox.Show("暂时无法打开程序，请使用目录中的启动入口重试。问题详情已记录。", "Input Probe", MessageBoxButton.OK, MessageBoxImage.Error);
            Application.Current.Shutdown(1);
            return;
        }
        DataContext = _viewModel;
        var expandedHeight = Height;
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(MainViewModel.IsRunning)) return;
            if (_viewModel.IsRunning)
            {
                expandedHeight = ActualHeight > 0 ? ActualHeight : Height;
                MinHeight = 360;
                if (WindowState == WindowState.Normal) Height = 380;
            }
            else
            {
                MinHeight = 460;
                if (WindowState == WindowState.Normal) Height = expandedHeight;
            }
        };
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

public sealed partial class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private enum CaptureMode { None, Start, Stop, Sequence }
    private readonly JsonConfigStore _store = new(Path.Combine(AppContext.BaseDirectory, "config.json"));
    private readonly RuntimeController _runtime;
    private KeyProfile _selectedProfile;
    private string _stateText = "尚未准备";
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
    public KeyProfile SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (value is null || ReferenceEquals(value, _selectedProfile) || _runtime.IsBusy) return;
            CancelCapture();
            _selectedProfile = value; SelectedStep = null;
            OnPropertyChanged(); OnPropertyChanged(nameof(UseToggleHotkey));
            OnPropertyChanged(nameof(StartHotkeyLabel)); ConfigureHotkeys();
        }
    }
    public bool UseToggleHotkey
    {
        get => SelectedProfile.ToggleHotkey;
        set
        {
            if (!CanEdit || value == SelectedProfile.ToggleHotkey) return;
            CancelCapture(); SelectedProfile.ToggleHotkey = value;
            OnPropertyChanged(); OnPropertyChanged(nameof(StartHotkeyLabel)); ConfigureHotkeys();
        }
    }
    public string StartHotkeyLabel => UseToggleHotkey ? "开始 / 停止" : "开始";
    public KeyStep? SelectedStep { get => _selectedStep; set { _selectedStep = value; OnPropertyChanged(); } }
    public bool CanEdit => IsInitialized && !_disposed && !_runtime.IsBusy && !_profileBusy;
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
            _keyboard = new DeferredDdKeyboard(() =>
            {
                logger.Info($"DD 初始化前：{InputDiagnostics.DescribeModifiers()}; {InputDiagnostics.DescribeForeground()}");
                var timer = System.Diagnostics.Stopwatch.StartNew();
                var native = new DdNativeKeyboard(dllPath, logger.Info);
                logger.Info($"DD 初始化成功 elapsedMs={timer.Elapsed.TotalMilliseconds:F1}：{InputDiagnostics.DescribeModifiers()}; DLL={Path.GetFullPath(dllPath)}");
                return native;
            });
            sender = new DdKeySender(_keyboard);
            logger.Info("窗口已启动：等待用户点击初始化 DD");
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
            LastMessage = _runtime.LastMessage.StartsWith("执行失败：", StringComparison.Ordinal)
                ? "执行失败，已停止。请检查配置后重试，问题详情已记录" : _runtime.LastMessage;
        });
        _logger.Info("启动：快捷键使用独立线程 MouseKeyHook / RegisterHotKey / Raw Input / 状态轮询，录入使用 WPF 窗口事件");
        if (_configLoadMessage is not null) LastMessage = _configLoadMessage;
        SetupCommands();
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
        AddStepCommand = new RelayCommand(_ => AddStep(), _ => CanEdit);
        RemoveStepCommand = new RelayCommand(_ => RemoveStep(), _ => CanEdit && SelectedStep is not null);
        MoveStepUpCommand = new RelayCommand(_ => MoveStep(-1), _ => CanEdit && SelectedStep is not null && SelectedProfile.Steps.IndexOf(SelectedStep) > 0);
        MoveStepDownCommand = new RelayCommand(_ => MoveStep(1), _ => CanEdit && SelectedStep is not null && SelectedProfile.Steps.IndexOf(SelectedStep) < SelectedProfile.Steps.Count - 1);
        CaptureStartCommand = new RelayCommand(_ => BeginCapture(CaptureMode.Start), _ => CanEdit);
        CaptureStopCommand = new RelayCommand(_ => BeginCapture(CaptureMode.Stop), _ => CanEdit);
        CaptureSequenceCommand = new RelayCommand(parameter => BeginSequenceCapture(parameter), _ => CanEdit);
    }

    private async Task SaveAsync()
    {
        try
        {
            ProfileOperations.ValidateForRepeat(Profiles);
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
        _logger.Info($"MouseKeyHook 监听：{_systemHotkeys.ListenerStatus}");
        _systemHotkeys.Diagnostic += message => _logger.Info(message);
        _logger.Info($"Raw Input 键盘注册：error={_systemHotkeys.RawInputError}");
        _systemHotkeys.Pressed += id =>
        {
            if (_disposed || _captureMode != CaptureMode.None) return;
            _logger.Info($"热键 source={_systemHotkeys.LastSource}, id={id}; {InputDiagnostics.DescribeForeground()}");
            if (id == 1 && !(UseToggleHotkey && _runtime.IsBusy)) StartSequence();
            else _runtime.Stop("快捷键已停止");
        };
        ConfigureHotkeys();
    }

    private void ConfigureHotkeys()
    {
        if (_systemHotkeys is null || _disposed) return;
        _systemHotkeys.Clear();
        if (!IsInitialized) { HotkeyStatus = "请先点击“准备使用”，快捷键尚未启用"; return; }
        _startRegistered = RegisterProfileHotkey(1, SelectedProfile.StartHotkey);
        _stopRegistered = !UseToggleHotkey && RegisterProfileHotkey(2, SelectedProfile.StopHotkey);
        var panicError = _systemHotkeys.Register(3, 0x79, 0x6); // Ctrl+Shift+F10; F12 is reserved.
        HotkeyStatus = $"开始：{SelectedProfile.StartHotkey} {(_startRegistered ? "✓" : "注册失败")} · 停止：{SelectedProfile.StopHotkey} {(_stopRegistered ? "✓" : "注册失败")} · 紧急停止：{(panicError == 0 ? "Ctrl+Shift+F10" : $"注册失败 {panicError}")}";
        if (UseToggleHotkey)
            HotkeyStatus = $"单键开关：{SelectedProfile.StartHotkey} {(_startRegistered ? "✓" : "不可用，请换一个键")} · 紧急停止：{(panicError == 0 ? "Ctrl+Shift+F10" : "不可用")}";
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
        if (_validationErrors != 0) { RejectStart("请先修正间隔输入"); return; }
        if (SelectedProfile.Steps.Any(step => step.WaitAfterMs is < 1 or > 3_600_000))
        { RejectStart("重复间隔必须在 1 到 3600000 毫秒之间"); return; }
        if (SelectedProfile.Steps.Any(step => string.Equals(step.Key, SelectedProfile.StartHotkey, StringComparison.OrdinalIgnoreCase) || (!UseToggleHotkey && string.Equals(step.Key, SelectedProfile.StopHotkey, StringComparison.OrdinalIgnoreCase))))
        { RejectStart("序列按键不能与开始/停止热键相同，请修改后启动"); return; }
        var repeat = new KeyProfile { Name = SelectedProfile.Name, Enabled = true,
            Steps = SelectedProfile.Steps.Select(step => new KeyStep(step.Key, step.WaitAfterMs) { HoldMs = RepeatTiming.HoldFor(step.WaitAfterMs) }).ToList() };
        _logger.Info($"开始重复 mode=DD-HID, steps={string.Join(",", repeat.Steps.Select(s => $"{s.Key}:interval={s.WaitAfterMs}ms,hold={s.HoldMs}ms"))}; {InputDiagnostics.DescribeForeground()}");
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
        if (!UseToggleHotkey && ((_captureMode == CaptureMode.Start && key == SelectedProfile.StopHotkey) ||
            (_captureMode == CaptureMode.Stop && key == SelectedProfile.StartHotkey)))
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

    private void RejectStart(string reason) { LastMessage = reason; _logger.Info($"开始被拒绝：{reason}"); }
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
