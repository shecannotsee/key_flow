using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace key_flow_core;

public enum RuntimeState { Stopped, Armed, Running }
public enum RetriggerMode { ResetTimedRun, IgnoreWhileActive }

public sealed class KeyStep : INotifyPropertyChanged
{
    public KeyStep() { }
    public KeyStep(string key, int waitAfterMs = 80) => (Key, WaitAfterMs) = (key, waitAfterMs);
    private string _key = "D1";
    private int _waitAfterMs = 80;
    private int _holdMs = 30;
    public int HoldMs { get => _holdMs; set { _holdMs = value; OnPropertyChanged(); } }
    public string Key { get => _key; set { _key = value; OnPropertyChanged(); } }
    public int WaitAfterMs { get => _waitAfterMs; set { _waitAfterMs = value; OnPropertyChanged(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class KeyProfile : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    private string _name = "我的方案 1";
    private string _groupName = "未分组";
    private string _startHotkey = "F8";
    private string _stopHotkey = "F9";
    private bool _toggleHotkey;
    private RetriggerMode _retriggerMode;
    private double _durationSeconds;
    private int _durationMilliseconds;
    private string _alternateStartHotkey = "";
    public double DurationSeconds { get => _durationSeconds; set { _durationSeconds = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalDurationSeconds)); } }
    public int DurationMilliseconds { get => _durationMilliseconds; set { _durationMilliseconds = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalDurationSeconds)); } }
    [JsonIgnore]
    public double TotalDurationSeconds => DurationSeconds + DurationMilliseconds / 1000d;
    public string AlternateStartHotkey { get => _alternateStartHotkey; set { _alternateStartHotkey = value; OnPropertyChanged(); } }
    public bool ToggleHotkey { get => _toggleHotkey; set { _toggleHotkey = value; OnPropertyChanged(); } }
    public RetriggerMode RetriggerMode { get => _retriggerMode; set { _retriggerMode = value; OnPropertyChanged(); } }
    private List<KeyStep> _steps = [];
    private bool _sequenceMode;
    private bool _enabled;
    private bool _isInActivationPool;
    public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
    public string GroupName { get => _groupName; set { _groupName = string.IsNullOrWhiteSpace(value) ? "未分组" : value.Trim(); OnPropertyChanged(); } }
    public string StartHotkey { get => _startHotkey; set { _startHotkey = value; OnPropertyChanged(); } }
    public string StopHotkey { get => _stopHotkey; set { _stopHotkey = value; OnPropertyChanged(); } }
    public List<KeyStep> Steps { get => _steps; set { _steps = value; OnPropertyChanged(); } }
    public bool SequenceMode { get => _sequenceMode; set { _sequenceMode = value; OnPropertyChanged(); } }
    public bool Enabled { get => _enabled; set { _enabled = value; OnPropertyChanged(); } }
    public bool IsInActivationPool { get => _isInActivationPool; set { _isInActivationPool = value; OnPropertyChanged(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class AppConfig
{
    public int Version { get; set; } = JsonConfigStore.CurrentVersion;
    public List<KeyProfile> Profiles { get; set; } = [];
    public List<ProfileGroup> Groups { get; set; } = [];
}

public static class AutomationValidation
{
    public static void Validate(KeyProfile profile)
    {
        if (!double.IsFinite(profile.DurationSeconds) || profile.DurationSeconds is < 0 or > 86400 ||
            profile.DurationMilliseconds is < 0 or > 999 || profile.TotalDurationSeconds > 86400)
            throw new InvalidDataException("持续时间应为 0 至 86400 秒，毫秒部分应为 0 至 999；0 表示持续运行");
    }
}

public interface IKeySender
{
    void KeyDown(string key);
    void KeyUp(string key);
    void Press(string key) { KeyDown(key); KeyUp(key); }
    void ReleaseAll();
    void Shutdown() { ReleaseAll(); }
}

public interface IInputLogger
{
    void Info(string message);
    void Error(string message, Exception exception);
}

public sealed class ActivationWindow : IDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _expired = new();
    private readonly Timer _timer;
    private readonly TimeSpan? _duration;
    private bool _started, _disposed;

    public CancellationToken Token => _expired.Token;
    public bool IsExpired => _expired.IsCancellationRequested;

    public ActivationWindow(double durationSeconds)
    {
        _duration = durationSeconds > 0 ? TimeSpan.FromSeconds(durationSeconds) : null;
        _timer = new Timer(_ => Expire(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _started) return;
            _started = true;
            if (_duration is { } duration) _timer.Change(duration, Timeout.InfiniteTimeSpan);
        }
    }

    public bool Refresh()
    {
        lock (_gate)
        {
            if (_disposed || _expired.IsCancellationRequested) return false;
            if (_started && _duration is { } duration) _timer.Change(duration, Timeout.InfiniteTimeSpan);
            return true;
        }
    }

    private void Expire()
    {
        lock (_gate)
            if (!_disposed) _expired.Cancel();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Dispose();
            _expired.Dispose();
        }
    }
}

public sealed class NullInputLogger : IInputLogger
{
    public void Info(string message) { }
    public void Error(string message, Exception exception) { }
}

public sealed class SequenceRunner(IKeySender sender)
{
    public async Task RunAsync(KeyProfile profile, CancellationToken cancellationToken, ActivationWindow? activationWindow = null)
    {
        Validate(profile);
        var steps = profile.Steps.Select(step => new KeyStep(step.Key, step.WaitAfterMs) { HoldMs = step.HoldMs }).ToArray();
        AutomationValidation.Validate(profile);
        using var ownedWindow = activationWindow is null ? new ActivationWindow(profile.TotalDurationSeconds) : null;
        var window = activationWindow ?? ownedWindow!;
        if (activationWindow is null) window.Start();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, window.Token);
        try
        {
            await Task.Run(() => PulseLoop.Run(sender, steps, deadline.Token), deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && window.IsExpired) { }

    }

    private static void Validate(KeyProfile profile)
    {
        if (profile.Steps is null || profile.Steps.Count == 0)
            throw new ArgumentException("序列至少需要一个按键步骤", nameof(profile));
        foreach (var step in profile.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.Key)) throw new ArgumentException("按键不能为空", nameof(profile));
            if (step.WaitAfterMs is < 0 or > 3_600_000)
                throw new ArgumentOutOfRangeException(nameof(profile), "步骤间隔必须在 0 到 3600000 毫秒之间");
            if (step.HoldMs is < 1 or > 60_000) throw new ArgumentOutOfRangeException(nameof(profile), "按住时长必须在 1 到 60000 毫秒之间");
        }
    }
}

public sealed class RuntimeController(SequenceRunner runner, IKeySender sender, IInputLogger? logger = null)
{
    private readonly IInputLogger _logger = logger ?? new NullInputLogger();
    private CancellationTokenSource? _runCancellation;
    public RuntimeState State { get; private set; } = RuntimeState.Stopped;
    public bool LastRunFailed { get; private set; }
    public bool IsBusy => _runCancellation is not null;
    public event Action<RuntimeState>? StateChanged;
    public string LastMessage { get; private set; } = "未启动";

    public void Arm() { if (!IsBusy) SetState(RuntimeState.Armed, "已就绪，等待触发"); }
    public async Task TriggerAsync(KeyProfile profile, CancellationToken cancellationToken = default, ActivationWindow? activationWindow = null)
    {
        if (State != RuntimeState.Armed || !profile.Enabled || _runCancellation is not null) return;
        LastRunFailed = false;
        _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        SetState(RuntimeState.Running, $"正在重复 {profile.Name}");
        try
        {
            await runner.RunAsync(profile, _runCancellation.Token, activationWindow);
            SetState(RuntimeState.Stopped, "执行完成，已自动停止");
        }
        catch (OperationCanceledException)
        {
            SetState(RuntimeState.Stopped, activationWindow?.IsExpired == true ? "执行完成，已自动停止" : "已停止");
        }
        catch (Exception exception)
        {
            LastRunFailed = true;
            _logger.Error("序列执行异常", exception);
            SetState(RuntimeState.Stopped, $"执行失败：{exception.Message}");
        }
        finally
        {
            _runCancellation.Dispose(); _runCancellation = null;
            TryCleanup(sender.ReleaseAll);
            StateChanged?.Invoke(State);
        }
    }

    public void Stop(string message = "已停止")
    {
        _runCancellation?.Cancel();
        var released = TryCleanup(sender.ReleaseAll);
        _logger.Info(message);
        SetState(RuntimeState.Stopped, released ? message : "已取消执行，但按键释放失败，请查看日志");
    }

    public void Shutdown()
    {
        _runCancellation?.Cancel();
        if (TryCleanup(sender.Shutdown)) _logger.Info("输入运行时已关闭");
    }

    private bool TryCleanup(Action cleanup)
    {
        try { cleanup(); return true; }
        catch (Exception exception)
        {
            LastRunFailed = true;
            _logger.Error("按键清理失败", exception);
            SetState(RuntimeState.Stopped, "按键释放失败，请查看日志");
            return false;
        }
    }

    private void SetState(RuntimeState state, string message)
    {
        State = state;
        LastMessage = message;
        StateChanged?.Invoke(state);
    }
}
