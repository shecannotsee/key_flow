namespace KeyFlow.Core;

public static class RepeatTiming
{
    public static int HoldFor(int intervalMs)
    {
        if (intervalMs is < 1 or > 3_600_000) throw new ArgumentOutOfRangeException(nameof(intervalMs));
        return Math.Min(30, Math.Max(1, intervalMs / 2));
    }
}
