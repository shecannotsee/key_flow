using KeyFlow.Core;
using Xunit;

public sealed class RepeatTimingTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(10, 5)]
    [InlineData(30, 15)]
    [InlineData(80, 30)]
    public void ShortIntervalsAdaptHoldWhileKeepingSuccessful80msTiming(int interval, int hold)
        => Assert.Equal(hold, RepeatTiming.HoldFor(interval));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3600001)]
    public void InvalidIntervalsAreRejected(int interval)
        => Assert.Throws<ArgumentOutOfRangeException>(() => RepeatTiming.HoldFor(interval));
}
