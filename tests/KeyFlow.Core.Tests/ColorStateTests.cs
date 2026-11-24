using KeyFlow.Core;
using Xunit;

namespace KeyFlow.Core.Tests;

public sealed class ColorStateTests
{
    private sealed class Screen : IScreenColorReader
    {
        public List<(int, int)> Points { get; } = [];
        public RgbColor Read(int x, int y) { Points.Add((x, y)); return new((byte)(x + 10), (byte)y, 30); }
    }

    [Fact]
    public void RegionCaptureAndEvaluationUseSameNinePoints()
    {
        var screen = new Screen();
        var condition = new ColorCondition { X = -10, Y = 10, Width = 11, Height = 11 };
        Assert.Equal(new RgbColor(5, 15, 30), ColorMatcher.Sample(screen, condition));
        Assert.Equal(9, screen.Points.Count);
        Assert.Contains((-10, 10), screen.Points); Assert.Contains((0, 20), screen.Points);
    }

    [Fact]
    public void ExplicitOppositeColorSeparatesInactiveFromUnknown()
    {
        var condition = new ColorCondition { Target = "#00FF00", InactiveTarget = "#222222", UseInactiveTarget = true, Tolerance = 5 };
        Assert.Equal(true, ColorMatcher.Classify(new(0, 255, 0), condition));
        Assert.Equal(false, ColorMatcher.Classify(new(34, 34, 34), condition));
        Assert.Null(ColorMatcher.Classify(new(255, 0, 0), condition));
        var edge = new ActivationEdge(100);
        Assert.False(edge.Observe(null, 0)); Assert.False(edge.Observe(null, 1000));
        Assert.False(edge.Observe(true, 2000)); Assert.False(edge.Observe(true, 3000));
        Assert.False(edge.IsArmed);
    }

    [Fact]
    public void UnknownInterruptsConfirmationAndAmbiguousColorsAreRejected()
    {
        var edge = new ActivationEdge(100);
        edge.Observe(false, 0); edge.Observe(null, 90); edge.Observe(false, 100);
        Assert.False(edge.Observe(true, 150)); Assert.False(edge.Observe(true, 300));
        var profile = new KeyProfile();
        profile.ColorCondition.UseInactiveTarget = true;
        profile.ColorCondition.InactiveTarget = "#01F001";
        Assert.Throws<InvalidDataException>(() => AutomationValidation.Validate(profile));
    }

    [Fact]
    public async Task RegionAndOppositeStateSurviveSaveAndCopy()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var source = new ColorCondition { X = -100, Y = 20, Width = 50, Height = 40, UseInactiveTarget = true, InactiveTarget = "#222222" };
            var copy = source.Copy(); copy.Width = 10;
            Assert.Equal(50, source.Width); Assert.True(copy.UseInactiveTarget);
            var store = new JsonConfigStore(path);
            await store.SaveAsync(new AppConfig { Profiles = [new KeyProfile { Steps = [new("E")], ColorCondition = source }] });
            var loaded = (await store.LoadAsync()).Profiles[0].ColorCondition;
            Assert.Equal(50, loaded.Width); Assert.Equal(40, loaded.Height);
            Assert.Equal("#222222", loaded.InactiveTarget); Assert.True(loaded.UseInactiveTarget);
        }
        finally { File.Delete(path); File.Delete(path + ".bak"); }
    }
}
