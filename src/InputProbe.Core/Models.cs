using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace InputProbe.Core;

public enum RuntimeState { Stopped, Armed, Running, Paused }

public sealed class KeyStep : INotifyPropertyChanged
{
    public KeyStep() { }
    public KeyStep(string key, int waitAfterMs = 80) => (Key, WaitAfterMs) = (key, waitAfterMs);
    private string _key = "D1";
    private int _waitAfterMs = 80;
    public string Key { get => _key; set { _key = value; OnPropertyChanged(); } }
    public int WaitAfterMs { get => _waitAfterMs; set { _waitAfterMs = value; OnPropertyChanged(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class KeyProfile : INotifyPropertyChanged
{
    private string _name = "新建配置";
    private string _targetProcess = "";
    private string _startHotkey = "F8";
    private string _stopHotkey = "F12";
    private List<KeyStep> _steps = [];
    private bool _enabled;
    public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
    public string TargetProcess { get => _targetProcess; set { _targetProcess = value; OnPropertyChanged(); } }
    public string StartHotkey { get => _startHotkey; set { _startHotkey = value; OnPropertyChanged(); } }
    public string StopHotkey { get => _stopHotkey; set { _stopHotkey = value; OnPropertyChanged(); } }
    public List<KeyStep> Steps { get => _steps; set { _steps = value; OnPropertyChanged(); } }
    public bool Enabled { get => _enabled; set { _enabled = value; OnPropertyChanged(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class AppConfig
{
    public int Version { get; set; } = 1;
    public List<KeyProfile> Profiles { get; set; } = [];
    public string PanicHotkey { get; set; } = "Ctrl+Shift+F12";
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

public sealed class NullInputLogger : IInputLogger
{
    public void Info(string message) { }
    public void Error(string message, Exception exception) { }
}

public interface IForegroundWindowGuard
{
    bool IsTargetForeground(string targetProcess);
}

public sealed class SequenceRunner(IKeySender sender, IForegroundWindowGuard? guard = null)
{
    public async Task<bool> RunAsync(KeyProfile profile, CancellationToken cancellationToken)
    {
        Validate(profile);
        while (true)
        {
            foreach (var step in profile.Steps)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (guard is not null && !guard.IsTargetForeground(profile.TargetProcess)) return false;
                // Keep a snapshot: edits or selection changes must not release another key.
                var key = step.Key;
                sender.KeyDown(key);
                try
                {
                    await Task.Delay(50, cancellationToken);
                }
                finally
                {
                    sender.KeyUp(key);
                }
                await Task.Delay(step.WaitAfterMs, cancellationToken);
            }
        }
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
        }
    }
}

public sealed class RuntimeController(SequenceRunner runner, IKeySender sender, IInputLogger? logger = null)
{
    private readonly IInputLogger _logger = logger ?? new NullInputLogger();
    private CancellationTokenSource? _runCancellation;
    public RuntimeState State { get; private set; } = RuntimeState.Stopped;
    public event Action<RuntimeState>? StateChanged;
    public string LastMessage { get; private set; } = "未启动";

    public void Arm() => SetState(RuntimeState.Armed, "已就绪，等待触发");
    public void Pause()
    {
        _runCancellation?.Cancel();
        sender.ReleaseAll();
        SetState(RuntimeState.Paused, "已暂停");
    }

    public async Task TriggerAsync(KeyProfile profile)
    {
        if (State != RuntimeState.Armed || !profile.Enabled || _runCancellation is not null) return;
        _runCancellation = new CancellationTokenSource();
        SetState(RuntimeState.Running, $"正在执行 {profile.Name}");
        try
        {
            var completed = await runner.RunAsync(profile, _runCancellation.Token);
            SetState(completed ? RuntimeState.Armed : RuntimeState.Stopped,
                completed ? "序列完成" : "目标窗口已失焦，已停止");
        }
        catch (OperationCanceledException)
        {
            if (State != RuntimeState.Paused) SetState(RuntimeState.Stopped, "已停止");
        }
        catch (Exception exception)
        {
            _logger.Error("序列执行异常", exception);
            SetState(RuntimeState.Stopped, "执行异常，已停止");
        }
        finally { _runCancellation.Dispose(); _runCancellation = null; sender.ReleaseAll(); }
    }

    public void Stop(string message = "已停止")
    {
        _runCancellation?.Cancel();
        sender.ReleaseAll();
        _logger.Info(message);
        SetState(RuntimeState.Stopped, message);
    }

    public void Shutdown()
    {
        _runCancellation?.Cancel();
        sender.Shutdown();
        _logger.Info("输入运行时已关闭");
    }

    private void SetState(RuntimeState state, string message)
    {
        State = state;
        LastMessage = message;
        StateChanged?.Invoke(state);
    }
}
