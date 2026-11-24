using System.Diagnostics;

namespace key_flow_core;

// Shared by the verified console runner and the WPF runner. Run off the UI thread.
public static class PulseLoop
{
    public static void Run(IKeySender sender, IReadOnlyList<KeyStep> steps, CancellationToken stop,
        Action<long>? onClick = null, Func<bool>? canPress = null)
    {
        if (steps.Count == 0) throw new ArgumentException("序列不能为空", nameof(steps));
        foreach (var step in steps)
            if (string.IsNullOrWhiteSpace(step.Key) || step.HoldMs is < 1 or > 60_000 || step.WaitAfterMs is < 0 or > 3_600_050)
                throw new ArgumentException("按键或时间参数无效", nameof(steps));
        long count = 0;
        var clock = Stopwatch.StartNew();
        while (true)
        {
            foreach (var step in steps)
            {
                stop.ThrowIfCancellationRequested();
                while (canPress is not null && !canPress())
                {
                    stop.WaitHandle.WaitOne(50);
                    stop.ThrowIfCancellationRequested();
                }
                stop.ThrowIfCancellationRequested();
                var started = clock.Elapsed.TotalMilliseconds;
                try
                {
                    sender.KeyDown(step.Key);
                    stop.WaitHandle.WaitOne(step.HoldMs);
                }
                finally { sender.KeyUp(step.Key); }
                onClick?.Invoke(++count);
                // WaitAfterMs retains its JSON name, but v2 defines start-to-start interval.
                var remaining = step.WaitAfterMs - (clock.Elapsed.TotalMilliseconds - started);
                if (remaining > 0) stop.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(remaining));
                // No accumulated deadlines or catch-up bursts.
            }
        }
    }
}
