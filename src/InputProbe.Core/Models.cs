namespace InputProbe.Core;

public enum RuntimeState { Stopped, Armed, Running, Paused }

public sealed class KeyStep
{
    public KeyStep() { }
    public KeyStep(string key, int waitAfterMs = 80) => (Key, WaitAfterMs) = (key, waitAfterMs);
    public string Key { get; set; } = "D1";
    public int WaitAfterMs { get; set; } = 80;
}

public sealed class KeyProfile
{
    public string Name { get; set; } = "新建配置";
    public string TargetProcess { get; set; } = "";
    public string StartHotkey { get; set; } = "F8";
    public string StopHotkey { get; set; } = "F12";
    public List<KeyStep> Steps { get; set; } = [];
    public bool Enabled { get; set; }
}

public sealed class AppConfig
{
    public List<KeyProfile> Profiles { get; set; } = [];
    public string PanicHotkey { get; set; } = "Ctrl+Shift+F12";
}

public interface IKeySender
{
    void KeyDown(string key);
    void KeyUp(string key);
    void ReleaseAll();
}

public interface IForegroundWindowGuard
{
    bool IsTargetForeground(string targetProcess);
}

public sealed class SequenceRunner(IKeySender sender, IForegroundWindowGuard? guard = null)
{
    public async Task<bool> RunAsync(KeyProfile profile, CancellationToken cancellationToken)
    {
        foreach (var step in profile.Steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (guard is not null && !guard.IsTargetForeground(profile.TargetProcess)) return false;
            sender.KeyDown(step.Key);
            sender.KeyUp(step.Key);
            await Task.Delay(step.WaitAfterMs, cancellationToken);
        }
        return true;
    }
}

public sealed class RuntimeController(SequenceRunner runner, IKeySender sender)
{
    private CancellationTokenSource? _runCancellation;
    public RuntimeState State { get; private set; } = RuntimeState.Stopped;
    public event Action<RuntimeState>? StateChanged;
    public string LastMessage { get; private set; } = "未启动";

    public void Arm() => SetState(RuntimeState.Armed, "已就绪，等待触发");
    public void Pause() => SetState(RuntimeState.Paused, "已暂停");

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
        catch (OperationCanceledException) { SetState(RuntimeState.Stopped, "已紧急停止"); }
        finally { _runCancellation.Dispose(); _runCancellation = null; sender.ReleaseAll(); }
    }

    public void Stop(string message = "已停止")
    {
        _runCancellation?.Cancel();
        sender.ReleaseAll();
        SetState(RuntimeState.Stopped, message);
    }

    private void SetState(RuntimeState state, string message)
    {
        State = state;
        LastMessage = message;
        StateChanged?.Invoke(state);
    }
}
