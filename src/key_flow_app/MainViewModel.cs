using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using key_flow_core;
using key_flow_windows;

namespace key_flow_app;

public sealed partial class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private enum CaptureMode { None, Start, Stop, AlternateStart, Sequence }
    private readonly JsonConfigStore _store = new(AppPaths.Config);
    private readonly RuntimeController _runtime;
    private readonly ActivationPool _activationPool;
    private readonly Dictionary<int, (KeyProfile Profile, bool IsStop, bool IsToggle, KeyChord Chord)> _hotkeyActions = [];
    private readonly Dictionary<KeyProfile, int> _activeProfileActivations = [];
    private KeyProfile _selectedProfile;
    private string _stateText = "尚未准备";
    private string _lastMessage = "未启动";
    private KeyStep? _selectedStep;
    private CaptureMode _captureMode;
    private readonly ChordCapture _chordCapture = new();
    private string? _configLoadMessage;
    private SystemHotkeys? _systemHotkeys;
    private bool _registerOnRelease;
    private bool _disposed;
    private readonly IInputLogger _logger;
    private readonly RunFeedback _feedback = new();
    private bool _isRunning;
    private bool _isListening;
    private volatile bool _ignoreAllSchemes;
    private readonly object _ignoreAllSchemesGate = new();
    private bool _isPinned;
    private ProfileEditorWindow? _editorWindow;
    private int _editorStep;
    private int _validationErrors;
    public bool IsRunning => _isRunning;
    public bool IsListening => _isListening;
    public bool IgnoreAllSchemes
    {
        get => _ignoreAllSchemes;
        set => SetIgnoreAllSchemes(value);
    }
    public bool IsPinned { get => _isPinned; set { _isPinned = value; OnPropertyChanged(); } }
    public string WindowTitle => IsRunning ? "正在运行 — key_flow" : IsMonitoring ? "正在监听 — key_flow" : "已停止 — key_flow";
    public int EditorStep { get => _editorStep; private set { _editorStep = Math.Clamp(value, 0, 2); OnPropertyChanged(); OnPropertyChanged(nameof(IsFirstEditorStep)); OnPropertyChanged(nameof(IsLastEditorStep)); } }
    public bool IsFirstEditorStep => EditorStep == 0;
    public bool IsLastEditorStep => EditorStep == 2;
    public bool ResetsOnRetrigger => SelectedProfile.RetriggerMode == RetriggerMode.ResetTimedRun;
    public bool IgnoresRetrigger => SelectedProfile.RetriggerMode == RetriggerMode.IgnoreWhileActive;
    public ICommand SetRetriggerCommand { get; private set; } = null!;
    public bool IsSingleKeyMode => !SelectedProfile.SequenceMode;
    public bool IsSequenceMode => SelectedProfile.SequenceMode;
    public bool IsUnlimitedMode => SelectedProfile.TotalDurationSeconds <= 0;
    public bool IsTimedMode => SelectedProfile.TotalDurationSeconds > 0;
    public void ChangeValidationErrors(int delta)
    {
        _validationErrors = Math.Max(0, _validationErrors + delta);
        CommandManager.InvalidateRequerySuggested();
    }
    private string _hotkeyStatus = "正在监听快捷键";
    public string HotkeyStatus { get => _hotkeyStatus; private set { _hotkeyStatus = value; OnPropertyChanged(); } }
    public string ActivationPoolStatus
    {
        get
        {
            var members = Profiles.Count(IsParticipating);
            return members == 0
                ? "暂无参与触发的方案"
                : $"{members} 个方案参与触发 · " + (_activationPool.ActiveCount == 0 ? "当前空闲" : $"{_activationPool.ActiveCount} 个正在执行或等待");
        }
    }


    public List<KeyProfile> Profiles { get; private set; }
    private string _profileSearch = "";
    public string ProfileSearch
    {
        get => _profileSearch;
        set { _profileSearch = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(FilteredProfiles)); }
    }
    public IEnumerable<KeyProfile> FilteredProfiles => string.IsNullOrWhiteSpace(ProfileSearch)
        ? Profiles
        : Profiles.Where(profile => profile.Name.Contains(ProfileSearch, StringComparison.OrdinalIgnoreCase)
            || profile.GroupName.Contains(ProfileSearch, StringComparison.OrdinalIgnoreCase));
    public IEnumerable<KeyProfile> ParticipatingProfiles => Profiles.Where(IsParticipating);
    public bool HasParticipatingProfiles => Profiles.Any(IsParticipating);
    public string SelectedProfileLabel => Profiles.Contains(SelectedProfile) ? SelectedProfile.Name : "暂无方案，请新建";

    public KeyProfile SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (value is null || ReferenceEquals(value, _selectedProfile) || _runtime.IsBusy || _activationPool.IsBusy || IsMonitoring) return;
            CancelCapture();
            _selectedProfile.PropertyChanged -= SelectedProfileChanged;
            _selectedProfile = value; SelectedStep = null; ColorStatus = "尚未检测";
            _selectedProfile.PropertyChanged += SelectedProfileChanged;
            OnPropertyChanged(); OnPropertyChanged(nameof(SelectedProfileLabel)); OnPropertyChanged(nameof(UseToggleHotkey));
            OnPropertyChanged(nameof(ResetsOnRetrigger)); OnPropertyChanged(nameof(IgnoresRetrigger));
            OnPropertyChanged(nameof(StartHotkeyLabel)); ConfigureHotkeys();
            OnPropertyChanged(nameof(IsSingleKeyMode)); OnPropertyChanged(nameof(IsSequenceMode));
            OnPropertyChanged(nameof(IsUnlimitedMode)); OnPropertyChanged(nameof(IsTimedMode));
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
    public bool CanEdit => IsInitialized && !_disposed && !_runtime.IsBusy && !_activationPool.IsBusy && !IsMonitoring && !_profileBusy;
    public string StateText { get => _stateText; private set { _stateText = value; OnPropertyChanged(); } }
    public string LastMessage { get => _lastMessage; private set { _lastMessage = value; OnPropertyChanged(); } }
    public ICommand SaveCommand { get; }
    public ICommand OpenEditorCommand { get; private set; } = null!;
    public ICommand CloseEditorCommand { get; private set; } = null!;
    public ICommand NavigateEditorCommand { get; private set; } = null!;
    public ICommand PreviousEditorStepCommand { get; private set; } = null!;
    public ICommand NextEditorStepCommand { get; private set; } = null!;
    public ICommand SetWorkflowModeCommand { get; private set; } = null!;
    public ICommand SetRepeatModeCommand { get; private set; } = null!;
    public ICommand ArmCommand { get; }
    public ICommand StartListeningCommand { get; private set; } = null!;
    public ICommand StopCommand { get; }
    public ICommand AddStepCommand { get; }
    public ICommand RemoveStepCommand { get; }
    public ICommand MoveStepUpCommand { get; }
    public ICommand MoveStepDownCommand { get; }
    public ICommand CaptureStartCommand { get; }
    public ICommand CaptureStopCommand { get; }
    public ICommand CaptureSequenceCommand { get; }
    public ICommand UseVerifiedPresetCommand { get; }
    public ICommand SetProfileEnabledCommand { get; private set; } = null!;
    public string CaptureText => _captureMode == CaptureMode.None ? "" : "按下主键；组合键请先按住 Ctrl、Alt 或 Shift";

    public MainViewModel()
    {
        AppConfig loaded;
        try
        {
            AppPaths.MigrateLegacyConfiguration();
            loaded = _store.LoadAsync().GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            var fallback = new KeyProfile { Name = "我的方案 1", Enabled = true, IsInActivationPool = true, GroupName = "默认配置组", Steps = [new("D8", 80)] };
            loaded = new AppConfig { Profiles = [fallback], Groups = [new ProfileGroup { Name = "默认配置组", IsDefault = true, IsSelectedForRun = true, MemberIds = [fallback.Id] }] };
            _configLoadMessage = $"配置加载失败，已使用默认配置：{exception.Message}";
        }
        Profiles = loaded.Profiles;
        Groups = loaded.Groups;
        _mainGroup = Groups.FirstOrDefault(group => group.IsDefault);
        _selectedProfile = Profiles.FirstOrDefault() ?? new KeyProfile();
        _selectedProfile.PropertyChanged += SelectedProfileChanged;
        var logger = _logger = new FileInputLogger(AppPaths.Log);
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
        _runtime = new RuntimeController(new SequenceRunner(sender, _screenReader), sender, logger);
        _activationPool = new ActivationPool(ExecutePooledProfileAsync);
        _activationPool.Changed += () => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_disposed)
            {
                OnPropertyChanged(nameof(ActivationPoolStatus));
                OnPropertyChanged(nameof(CanEdit));
                CommandManager.InvalidateRequerySuggested();
            }
        }));
        _activationPool.Failed += exception => _logger.Error("激活池中的方案执行失败", exception);
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
            StateText = state == RuntimeState.Running ? "运行中" : IsMonitoring ? $"监听中 · {_monitorCancellations.Count} 个方案" : "已停止";
            OnPropertyChanged(nameof(CanEdit));
            CommandManager.InvalidateRequerySuggested();
            LastMessage = _runtime.LastMessage.StartsWith("执行失败：", StringComparison.Ordinal)
                ? "执行失败，已停止。请检查配置后重试，问题详情已记录" : _runtime.LastMessage;
        });
        _logger.Info("启动：全局触发键使用 MouseKeyHook / Raw Input / 状态轮询被动监听并放行；录入使用 WPF 窗口事件");
        if (_configLoadMessage is not null) LastMessage = _configLoadMessage;
        SetupCommands();
        SetupAutomationCommands();
        SaveCommand = new RelayCommand(async _ => await SaveAsync(), _ => CanEdit && _validationErrors == 0);
        SetupEditorCommands();
        SetupGroupCommands();
        StartListeningCommand = new RelayCommand(_ => StartListening(), _ => CanStartListening);
        ArmCommand = new RelayCommand(_ => StartListening(), _ => CanStartListening);
        UseVerifiedPresetCommand = new RelayCommand(_ =>
        {
            var preset = new KeyProfile { Name = ProfileOperations.NextProfileName(Profiles), Enabled = true, Steps = [new("D8", 80)] };
            var targetGroup = Groups.FirstOrDefault(group => group == MainGroup) ?? Groups.FirstOrDefault(group => group.IsDefault);
            targetGroup?.MemberIds.Add(preset.Id);
            if (targetGroup is not null) preset.GroupName = targetGroup.Name;
            Profiles = [.. Profiles, preset];
            OnPropertyChanged(nameof(Profiles)); OnPropertyChanged(nameof(ActivationPoolStatus));
            SelectedProfile = preset;
            if (_isListening) ConfigureHotkeys();
            else StartListening();
            LastMessage = $"已新建“{preset.Name}”，请编辑方案并保存";
        }, _ => CanEdit);
        SetProfileEnabledCommand = new RelayCommand(parameter =>
        {
            if (parameter is not KeyProfile profile || !CanEdit) return;
            if (!profile.Enabled)
            {
                _monitorCancellations.GetValueOrDefault(profile)?.Cancel();
                _activationPool.Cancel(profile);
            }
            else if (_isListening && profile.ColorTriggerEnabled && ProfileParticipation.IsEligible(profile, Groups))
                _ = StartMonitoringAsync(profile);
            else if (profile.Enabled)
                StartListening();
            ConfigureHotkeys();
            OnPropertyChanged(nameof(ActivationPoolStatus));
            LastMessage = profile.Enabled
                ? $"方案“{profile.Name}”已启用；还需勾选所属配置组并启动监听才会触发"
                : $"方案“{profile.Name}”已关闭，不会参与触发";
        }, parameter => CanEdit && parameter is KeyProfile profile && Profiles.Contains(profile));
        StopCommand = new RelayCommand(_ => StopEverything("用户已停止"), _ => _isListening || _runtime.IsBusy || _activationPool.IsBusy || IsMonitoring);
        AddStepCommand = new RelayCommand(_ => AddStep(), _ => CanEdit);
        RemoveStepCommand = new RelayCommand(_ => RemoveStep(), _ => CanEdit && SelectedStep is not null && (!SelectedProfile.SequenceMode || SelectedProfile.Steps.Count > 2));
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
            await _store.SaveAsync(new AppConfig { Version = JsonConfigStore.CurrentVersion, Profiles = Profiles, Groups = Groups });
            LastMessage = "配置已保存";
            _editorWindow?.Close();
        }
        catch (Exception exception) { LastMessage = $"保存失败：{exception.Message}"; }
    }

    private void StartListening()
    {
        if (!IsInitialized || _disposed || _isListening) return;
        var selectedGroups = Groups.Where(group => group.MemberIds.Any(id => Profiles.Any(profile => profile.Id == id && profile.Enabled))).ToArray();
        var enabledProfiles = Profiles.Where(profile => profile.Enabled && ProfileParticipation.GroupFor(profile, Groups) is not null).ToArray();
        if (enabledProfiles.Length == 0)
        {
            RejectStart("所选配置组中没有已启用的方案，请启用方案或选择其他配置组");
            return;
        }
        _isListening = true;
        OnPropertyChanged(nameof(IsListening));
        ConfigureHotkeys();
        if (!IgnoreAllSchemes)
            foreach (var profile in Profiles.Where(profile => ProfileParticipation.IsEligible(profile, Groups) && profile.ColorTriggerEnabled).ToArray())
                _ = StartMonitoringAsync(profile);
        StateText = IgnoreAllSchemes ? "监听中 · 已忽略所有方案" : "监听中";
        LastMessage = IgnoreAllSchemes ? "监听已启动，当前忽略所有方案" : $"正在监听 {selectedGroups.Length} 个配置组中的 {enabledProfiles.Length} 个已启用方案";
        OnPropertyChanged(nameof(WindowTitle));
        CommandManager.InvalidateRequerySuggested();
    }

    private void SetupEditorCommands()
    {
        OpenEditorCommand = new RelayCommand(_ => OpenEditor(), _ => CanEdit && Profiles.Contains(SelectedProfile));
        CloseEditorCommand = new RelayCommand(_ => _editorWindow?.Close());
        NavigateEditorCommand = new RelayCommand(value => { if (int.TryParse(value?.ToString(), out var step)) EditorStep = step; }, _ => CanEdit);
        PreviousEditorStepCommand = new RelayCommand(_ => EditorStep--, _ => CanEdit && EditorStep > 0);
        NextEditorStepCommand = new RelayCommand(_ => EditorStep++, _ => CanEdit && EditorStep < 2);
        SetWorkflowModeCommand = new RelayCommand(SetWorkflowMode, _ => CanEdit);
        SetRetriggerCommand = new RelayCommand(value =>
        {
            SelectedProfile.RetriggerMode = value?.ToString() == "ignore" ? RetriggerMode.IgnoreWhileActive : RetriggerMode.ResetTimedRun;
        }, _ => CanEdit && !UseToggleHotkey);
        SetRepeatModeCommand = new RelayCommand(SetRepeatMode, _ => CanEdit);
    }

    private void SelectedProfileChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(KeyProfile.RetriggerMode) or null)
        { OnPropertyChanged(nameof(ResetsOnRetrigger)); OnPropertyChanged(nameof(IgnoresRetrigger)); }
        if (e.PropertyName is nameof(KeyProfile.Name) or null) OnPropertyChanged(nameof(SelectedProfileLabel));
        if (e.PropertyName is nameof(KeyProfile.DurationSeconds) or nameof(KeyProfile.DurationMilliseconds) or null)
        {
            OnPropertyChanged(nameof(IsUnlimitedMode)); OnPropertyChanged(nameof(IsTimedMode));
        }
        if (e.PropertyName is nameof(KeyProfile.SequenceMode) or null)
        {
            OnPropertyChanged(nameof(IsSingleKeyMode)); OnPropertyChanged(nameof(IsSequenceMode));
        }
    }

    private void OpenEditor()
    {
        if (!CanEdit || !Profiles.Contains(SelectedProfile) || _editorWindow is not null) return;
        EditorStep = 0;
        _editorWindow = new ProfileEditorWindow { Owner = Application.Current.MainWindow, DataContext = this };
        _editorWindow.ShowDialog();
    }

    public void EditorClosed(ProfileEditorWindow window)
    {
        if (ReferenceEquals(_editorWindow, window)) _editorWindow = null;
        CancelCapture();
    }

    private void SetWorkflowMode(object? parameter)
    {
        var sequence = string.Equals(parameter?.ToString(), "sequence", StringComparison.OrdinalIgnoreCase);
        if (sequence == SelectedProfile.SequenceMode) return;
        if (!sequence && SelectedProfile.Steps.Count > 1)
        {
            var answer = MessageBox.Show(_editorWindow ?? Application.Current.MainWindow,
                "切换为单键重复会保留第一步并移除其余步骤。是否继续？", "切换流程类型",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) { OnPropertyChanged(nameof(IsSingleKeyMode)); OnPropertyChanged(nameof(IsSequenceMode)); return; }
        }
        SelectedProfile.SequenceMode = sequence;
        if (sequence && SelectedProfile.Steps.Count == 1)
            SelectedProfile.Steps = [.. SelectedProfile.Steps, new KeyStep(SelectedProfile.Steps[0].Key, SelectedProfile.Steps[0].WaitAfterMs) { HoldMs = SelectedProfile.Steps[0].HoldMs }];
        else if (!sequence && SelectedProfile.Steps.Count > 1)
            SelectedProfile.Steps = [SelectedProfile.Steps[0]];
        SelectedStep = null;
        OnPropertyChanged(nameof(SelectedProfile)); OnPropertyChanged(nameof(IsSingleKeyMode)); OnPropertyChanged(nameof(IsSequenceMode));
    }

    private void SetRepeatMode(object? parameter)
    {
        if (string.Equals(parameter?.ToString(), "timed", StringComparison.OrdinalIgnoreCase))
        {
            if (SelectedProfile.TotalDurationSeconds <= 0)
            { SelectedProfile.DurationSeconds = 1; SelectedProfile.DurationMilliseconds = 0; }
        }
        else { SelectedProfile.DurationSeconds = 0; SelectedProfile.DurationMilliseconds = 0; }
        OnPropertyChanged(nameof(IsUnlimitedMode)); OnPropertyChanged(nameof(IsTimedMode));
    }

    public void HandleKeyCode(int virtualKey, uint modifiers = 0)
    {
        if (_captureMode == CaptureMode.None) return;
        var chord = _chordCapture.Down(virtualKey, modifiers);
        if (chord is not null) CompleteCapture(chord);
    }

    public void HandleKeyRelease(int virtualKey)
    {
        var chord = _chordCapture.Up(virtualKey);
        if (chord is not null && _captureMode != CaptureMode.None) CompleteCapture(chord);
        if (_chordCapture.IsReleased) FinishCaptureRelease();
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
        _chordCapture.Reset();
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
            if (id == 3) { StopEverything("紧急停止快捷键已触发"); return; }
            if (!_hotkeyActions.TryGetValue(id, out var action)) return;
            if (action.IsStop || action.Chord == KeyChord.Parse("Ctrl+Shift+F10") ||
                _hotkeyActions.Values.Any(item => item.IsStop && item.Chord == action.Chord))
            { StopProfile(action.Profile, $"方案“{action.Profile.Name}”的停止键已触发"); return; }
            if (action.IsToggle && IsProfileActive(action.Profile))
            { StopProfile(action.Profile, $"方案“{action.Profile.Name}”的开关键已触发"); return; }
            if (action.Profile.ColorTriggerEnabled) { _ = StartMonitoringAsync(action.Profile); return; }
            QueueProfileActivation(action.Profile, "键盘触发");
        };
        ConfigureHotkeys();
    }

    private void ConfigureHotkeys()
    {
        if (_systemHotkeys is null || _disposed) return;
        _systemHotkeys.Clear();
        _hotkeyActions.Clear();
        if (!IsInitialized) { HotkeyStatus = "请先点击“准备使用”，快捷键尚未启用"; return; }
        _systemHotkeys.Bind(3, 0x79, 0x6); // Ctrl+Shift+F10; F12 is reserved.
        var id = 100;
        foreach (var profile in _isListening && !IgnoreAllSchemes ? Profiles.Where(IsParticipating) : [])
        {
            BindAction(id++, profile, profile.StartHotkey, isStop: false, isToggle: profile.ToggleHotkey);
            if (!profile.ToggleHotkey) BindAction(id++, profile, profile.StopHotkey, isStop: true, isToggle: false);
            if (!string.IsNullOrWhiteSpace(profile.AlternateStartHotkey))
                BindAction(id++, profile, profile.AlternateStartHotkey, isStop: false, isToggle: profile.ToggleHotkey);
        }
        var poolCount = _isListening && !IgnoreAllSchemes ? Profiles.Count(IsParticipating) : 0;
        HotkeyStatus = IgnoreAllSchemes ? "已忽略所有方案 · Ctrl+Shift+F10 紧急停止 · 按键继续传递" : !_isListening ? "监听已停止 · Ctrl+Shift+F10 紧急停止 · 按键继续传递" : poolCount == 0
            ? "没有已激活组中的启用方案 · 按键继续传递"
            : $"正在监听 {poolCount} 个已启用方案 · Ctrl+Shift+F10 紧急停止 · 按键继续传递";
        _logger.Info(HotkeyStatus);
    }

    private void BindAction(int id, KeyProfile profile, string key, bool isStop, bool isToggle)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        KeyChord chord;
        try { chord = KeyChord.Parse(key); }
        catch (ArgumentException e) { _logger.Error($"方案“{profile.Name}”快捷键无效", e); return; }
        if (chord.MainKey == "F12") return;
        if (_systemHotkeys!.Bind(id, (uint)chord.VirtualKey, chord.Modifiers))
            _hotkeyActions[id] = (profile, isStop, isToggle, chord);
    }

    private void StartSequence()
    {
        if (!CanEdit || _captureMode != CaptureMode.None) return;
        if (!IsParticipating(SelectedProfile)) { LastMessage = "请先勾选所属配置组并启用所选方案"; return; }
        if (_validationErrors != 0) { RejectStart("请先修正间隔输入"); return; }
        try { ProfileOperations.ValidateForRepeat([SelectedProfile]); }
        catch (InvalidDataException e) { RejectStart(e.Message); return; }
        if (SelectedProfile.ColorTriggerEnabled) { _ = StartMonitoringAsync(SelectedProfile); return; }
        QueueProfileActivation(SelectedProfile, "界面开始");
    }

    private void QueueProfileActivation(KeyProfile source, string origin)
    {
        if (!IsInitialized || !_isListening || _disposed || _captureMode != CaptureMode.None) return;
        try
        {
            ProfileOperations.ValidateForRepeat([source]);
            var repeat = new KeyProfile
            {
                Name = source.Name, Enabled = true, DurationSeconds = source.DurationSeconds,
                DurationMilliseconds = source.DurationMilliseconds,
                ColorCondition = source.ColorCondition.Copy(),
                Steps = source.Steps.Select(step => new KeyStep(step.Key, step.WaitAfterMs) { HoldMs = step.HoldMs }).ToList(),
                RetriggerMode = source.RetriggerMode
            };
            ActivationSubmission submission;
            lock (_ignoreAllSchemesGate)
            {
                if (_ignoreAllSchemes || !IsParticipating(source)) return;
                _logger.Info($"方案进入激活池 origin={origin}, name={repeat.Name}, duration={repeat.TotalDurationSeconds:F3}s, steps={repeat.Steps.Count}");
                submission = _activationPool.Submit(repeat, source, source.RetriggerMode);
            }
            if (submission.Coalesced)
            {
                _logger.Info($"方案重复触发已合并 mode={source.RetriggerMode}, name={source.Name}");
                return;
            }
            _activeProfileActivations[source] = _activeProfileActivations.GetValueOrDefault(source) + 1;
            _ = ObserveActivationAsync(source, submission.Completion);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or InvalidOperationException)
        { RejectStart(exception.Message); }
    }

    private async Task ObserveActivationAsync(KeyProfile source, Task activation)
    {
        try { await activation; }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            _logger.Error("激活池方案失败", exception);
            if (!_disposed) LastMessage = "方案执行失败，已记录日志";
        }
        finally
        {
            var remaining = _activeProfileActivations.GetValueOrDefault(source) - 1;
            if (remaining <= 0) _activeProfileActivations.Remove(source);
            else _activeProfileActivations[source] = remaining;
            if (!_disposed) RefreshMainPresentation();
        }
    }

    private async Task ExecutePooledProfileAsync(KeyProfile profile, CancellationToken cancellationToken, ActivationWindow window)
    {
        var dispatcher = Application.Current.Dispatcher;
        using var registration = cancellationToken.Register(() =>
        {
            if (!window.IsExpired && !dispatcher.HasShutdownStarted)
                dispatcher.BeginInvoke(new Action(() => _runtime.Stop("激活池项目已取消")));
        });
        await dispatcher.InvokeAsync(async () =>
        {
            Task run;
            lock (_ignoreAllSchemesGate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_ignoreAllSchemes) throw new OperationCanceledException(cancellationToken);
                _mainExecutingName = profile.Name;
                _runtime.Arm();
                run = _runtime.TriggerAsync(profile, cancellationToken, window);
            }
            await run;
            if (cancellationToken.IsCancellationRequested && !window.IsExpired)
                cancellationToken.ThrowIfCancellationRequested();
            if (_runtime.LastRunFailed) throw new InvalidOperationException("按键方案执行失败");
        }).Task.Unwrap();
    }

    private void BeginCapture(CaptureMode mode)
    {
        if (!CanEdit) return;
        _systemHotkeys?.Clear();
        _chordCapture.Reset();
        _captureMode = mode;
        OnPropertyChanged(nameof(CaptureText));
    }

    private void CompleteCapture(string key)
    {
        if (string.IsNullOrEmpty(key)) { LastMessage = "暂不支持此按键，请换一个键"; return; }
        if (!UseToggleHotkey && ((_captureMode == CaptureMode.Start && KeyChord.Parse(key) == KeyChord.Parse(SelectedProfile.StopHotkey)) ||
            (_captureMode == CaptureMode.Stop && KeyChord.Parse(key) == KeyChord.Parse(SelectedProfile.StartHotkey))))
        { LastMessage = "开始和停止不能使用同一个键"; _chordCapture.Reset(); return; }
        if (KeyChord.Parse(key).MainKey == "F12" && _captureMode is CaptureMode.Start or CaptureMode.Stop)
        { LastMessage = "F12 为系统保留键，请选择其他开始/停止按键"; _chordCapture.Reset(); return; }
        switch (_captureMode)
        {
            case CaptureMode.AlternateStart: SelectedProfile.AlternateStartHotkey = key; break;
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
        if (SelectedStep is null || (SelectedProfile.SequenceMode && SelectedProfile.Steps.Count <= 2)) return;
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
        _automationCancellation.Cancel();
        _activationPool.Dispose();
        _editorWindow?.Close();
        foreach (var cancellation in _monitorCancellations.Values.ToArray()) cancellation.Cancel();
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
    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new(name));
        if (name is nameof(Profiles) or nameof(Groups) or nameof(ActivationPoolStatus) or nameof(SelectedProfile)
            or nameof(SelectedProfileLabel) or nameof(CanEdit) or nameof(IsRunning) or nameof(IsMonitoring)
            or nameof(LastMessage) or nameof(IsInitialized) or nameof(IgnoreAllSchemes))
            RefreshMainPresentation();
        if (name is nameof(Profiles) or nameof(ActivationPoolStatus))
        {
            PropertyChanged?.Invoke(this, new(nameof(ParticipatingProfiles)));
            PropertyChanged?.Invoke(this, new(nameof(HasParticipatingProfiles)));
        }
        if (name == nameof(Profiles))
        {
            PropertyChanged?.Invoke(this, new(nameof(SelectedProfileLabel)));
            PropertyChanged?.Invoke(this, new(nameof(ActivationPoolStatus)));
            PropertyChanged?.Invoke(this, new(nameof(FilteredProfiles)));
        }
    }
}

