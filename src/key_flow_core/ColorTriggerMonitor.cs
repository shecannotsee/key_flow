using System.Diagnostics;

namespace key_flow_core;

public enum ColorTriggerState { WaitingForInactive, Watching, Executing, Unknown }

// A rule can only fire after a stable inactive observation. Reset after each workflow.
public sealed class ActivationEdge(int debounceMs)
{
    private bool? _candidate;
    private long _since;
    private bool _armed;
    public bool IsArmed => _armed;

    public bool Observe(bool? active, long milliseconds)
    {
        if (active is null) { _candidate = null; _since = milliseconds; return false; }
        if (_candidate != active) { _candidate = active; _since = milliseconds; }
        if (milliseconds - _since < debounceMs) return false;
        if (active == false) { _armed = true; return false; }
        if (!_armed) return false;
        _armed = false;
        return true;
    }
}

// No keyboard or UI dependencies. The supplied workflow owns its output and cleanup.
public sealed class ColorTriggerMonitor(IScreenColorReader screen)
{
    public async Task RunAsync(ColorCondition condition, int debounceMs,
        Func<CancellationToken, Task> workflow, CancellationToken stop,
        Action<ColorTriggerState>? status = null)
    {
        if (debounceMs is < 50 or > 10000) throw new ArgumentOutOfRangeException(nameof(debounceMs));
        var snapshot = condition.Copy();
        RgbColor.Parse(snapshot.Target);
        if (snapshot.Tolerance is < 0 or > 255) throw new ArgumentOutOfRangeException(nameof(condition));
        var clock = Stopwatch.StartNew();
        var edge = new ActivationEdge(debounceMs);
        ColorTriggerState? last = null;
        void Report(ColorTriggerState state)
        {
            if (last == state) return;
            last = state; status?.Invoke(state);
        }
        Report(ColorTriggerState.WaitingForInactive);
        while (true)
        {
            stop.ThrowIfCancellationRequested();
            var active = ColorMatcher.Classify(ColorMatcher.Sample(screen, snapshot), snapshot);
            stop.ThrowIfCancellationRequested();
            if (edge.Observe(active, clock.ElapsedMilliseconds))
            {
                Report(ColorTriggerState.Executing);
                stop.ThrowIfCancellationRequested();
                await workflow(stop).ConfigureAwait(false);
                stop.ThrowIfCancellationRequested();
                // No queued activations: require a fresh inactive observation after completion.
                edge = new ActivationEdge(debounceMs);
                Report(ColorTriggerState.WaitingForInactive);
            }
            else Report(active is null ? ColorTriggerState.Unknown : edge.IsArmed ? ColorTriggerState.Watching : ColorTriggerState.WaitingForInactive);
            await Task.Delay(50, stop).ConfigureAwait(false);
        }
    }
}
