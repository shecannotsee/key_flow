using System.Runtime.InteropServices;

namespace InputProbe.Windows;

// Narrow compensation for the reported initialization jump, not an input blocker.
public sealed class InitializationCursorRecovery
{
    public readonly record struct Position(int X, int Y);
    private readonly Func<Position?> read;
    private readonly Func<Position, bool> restore;
    private readonly Action<string>? log;

    public InitializationCursorRecovery(Func<Position?> read, Func<Position, bool> restore, Action<string>? log = null)
    { this.read = read; this.restore = restore; this.log = log; }

    public T Run<T>(Func<T> initialize)
    {
        var before = read();
        log?.Invoke($"DD cursor before={before?.ToString() ?? "unknown"}");
        try { return initialize(); }
        finally
        {
            var after = read();
            log?.Invoke($"DD cursor after={after?.ToString() ?? "unknown"}");
            if (ShouldRestore(before, after))
            {
                var restored = restore(before!.Value);
                log?.Invoke($"DD cursor restore={restored}, target={before}");
            }
        }
    }

    public static bool ShouldRestore(Position? before, Position? after)
        => before.HasValue && before.Value != new Position(0, 0) && after == new Position(0, 0);

    public static InitializationCursorRecovery ForDesktop(Action<string>? log) => new(
        () => GetCursorPos(out var point) ? new Position(point.X, point.Y) : null,
        point => SetCursorPos(point.X, point.Y), log);

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetCursorPos(int x, int y);
}
