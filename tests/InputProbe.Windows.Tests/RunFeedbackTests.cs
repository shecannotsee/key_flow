using System.Text;
using InputProbe.App;
using Xunit;

public sealed class RunFeedbackTests
{
    [Fact]
    public void StartAndStopHaveDifferentValidShortWaveforms()
    {
        var start = RunFeedback.CreateWave(true);
        var stop = RunFeedback.CreateWave(false);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(start, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(start, 8, 4));
        Assert.Equal(start.Length - 8, BitConverter.ToInt32(start, 4));
        Assert.Equal(start.Length - 44, BitConverter.ToInt32(start, 40));
        Assert.Equal(22050, BitConverter.ToInt32(start, 24));
        Assert.Equal(start.Length, stop.Length);
        Assert.False(start.SequenceEqual(stop));
    }
}
