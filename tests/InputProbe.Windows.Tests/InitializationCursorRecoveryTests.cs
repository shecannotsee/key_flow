using InputProbe.Windows;
using Xunit;
using Position = InputProbe.Windows.InitializationCursorRecovery.Position;

public sealed class InitializationCursorRecoveryTests
{
    [Theory]
    [InlineData(120, 80, 0, 0, true)]
    [InlineData(-120, 80, 0, 0, true)]
    [InlineData(0, 0, 0, 0, false)]
    [InlineData(120, 80, 125, 85, false)]
    [InlineData(120, 80, 120, 80, false)]
    public void RestoresOnlyReportedOriginJump(int x, int y, int endX, int endY, bool expected)
    {
        var points = new Queue<Position?>([new(x, y), new(endX, endY)]);
        var restored = new List<Position>();
        var guard = new InitializationCursorRecovery(() => points.Dequeue(), p => { restored.Add(p); return true; });
        Assert.Equal(1, guard.Run(() => 1));
        Assert.Equal(expected ? 1 : 0, restored.Count);
        if (expected) Assert.Equal(new Position(x, y), restored[0]);
    }

    [Fact]
    public void FailedInitializationStillRestoresAndPreservesFailure()
    {
        var points = new Queue<Position?>([new(100, 200), new(0, 0)]);
        var restored = false;
        var guard = new InitializationCursorRecovery(() => points.Dequeue(), _ => restored = true);
        Assert.Throws<InvalidOperationException>(() => guard.Run<int>(() => throw new InvalidOperationException("init")));
        Assert.True(restored);
    }

    [Fact]
    public void UnavailablePositionNeverMovesCursor()
    {
        Assert.False(InitializationCursorRecovery.ShouldRestore(null, new(0, 0)));
        Assert.False(InitializationCursorRecovery.ShouldRestore(new(1, 2), null));
    }
}
