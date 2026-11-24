using System.Diagnostics;
using key_flow_core;
using Xunit;

namespace key_flow_core_tests;

public sealed class AutomationTests
{
    private sealed class Sender : IKeySender
    {
        public int Downs, Ups, Releases;
        public void KeyDown(string key) => Interlocked.Increment(ref Downs);
        public void KeyUp(string key) => Interlocked.Increment(ref Ups);
        public void ReleaseAll() => Releases++;
    }
    private sealed class Screen(Func<RgbColor> read) : IScreenColorReader
    {
        public RgbColor Read(int x, int y) => read();
    }
    private static KeyProfile Profile() => new()
    {
        Enabled = true, DurationSeconds = .25,
        Steps = [new("E", 20) { HoldMs = 5 }]
    };

    [Fact]
    public async Task SixSecondRepeatIgnoresRetriggerAndBalancesEveryKey()
    {
        var sender = new Sender(); var profile = Profile();
        profile.DurationSeconds = 6; profile.Steps[0].WaitAfterMs = 80;
        var runtime = new RuntimeController(new SequenceRunner(sender), sender);
        runtime.Arm(); var watch = Stopwatch.StartNew();
        var running = runtime.TriggerAsync(profile);
        await Task.Delay(100);
        await runtime.TriggerAsync(profile);
        Assert.True(runtime.IsBusy);
        await running.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.InRange(watch.ElapsedMilliseconds, 5700, 8500);
        Assert.InRange(sender.Downs, 20, 90);
        Assert.Equal(sender.Downs, sender.Ups);
        Assert.Equal(1, sender.Releases);
    }

    [Fact]
    public async Task ColorGateResumesAfterMismatch()
    {
        var sender = new Sender(); var profile = Profile();
        profile.DurationSeconds = 1; profile.ColorCondition.Enabled = true;
        var samples = 0;
        var screen = new Screen(() => ++samples <= 2 ? new(0, 0, 0) : new(0, 255, 0));
        await new SequenceRunner(sender, screen).RunAsync(profile, default).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(samples > 2); Assert.True(sender.Downs > 1); Assert.Equal(sender.Downs, sender.Ups);
    }

    [Fact]
    public async Task DeadlineStopsAndReleasesWithoutManualStop()
    {
        var sender = new Sender();
        var runtime = new RuntimeController(new SequenceRunner(sender), sender);
        runtime.Arm();
        var watch = Stopwatch.StartNew();
        await runtime.TriggerAsync(Profile()).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.InRange(watch.ElapsedMilliseconds, 150, 4000);
        Assert.True(sender.Downs > 1);
        Assert.Equal(sender.Downs, sender.Ups);
        Assert.True(sender.Releases > 0);
        Assert.False(runtime.IsBusy);
        Assert.Equal(RuntimeState.Stopped, runtime.State);
        Assert.Contains("自动停止", runtime.LastMessage);
    }

    [Fact]
    public async Task TimedRepeatIncludesMillisecondRemainder()
    {
        var sender = new Sender(); var profile = Profile();
        profile.DurationSeconds = 1; profile.DurationMilliseconds = 300;
        profile.Steps[0].WaitAfterMs = 80;
        Assert.Equal(1.3, profile.TotalDurationSeconds, 3);
        var watch = Stopwatch.StartNew();
        await new SequenceRunner(sender).RunAsync(profile, default).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.InRange(watch.ElapsedMilliseconds, 1200, 3500);
        Assert.Equal(sender.Downs, sender.Ups);
    }

    [Fact]
    public async Task RefreshingAnActiveTimedWindowMovesItsExpiryToTheLatestTrigger()
    {
        using var window = new ActivationWindow(.3);
        window.Start();
        await Task.Delay(180);
        Assert.True(window.Refresh());
        await Task.Delay(180);
        Assert.False(window.IsExpired);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.Delay(Timeout.Infinite, window.Token)
            .WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(window.IsExpired);
    }

    [Fact]
    public async Task MismatchNeverOutputsAndStillExpires()
    {
        var sender = new Sender();
        var profile = Profile(); profile.ColorCondition.Enabled = true;
        await new SequenceRunner(sender, new Screen(() => new(0, 0, 0))).RunAsync(profile, default)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, sender.Downs);
    }

    [Fact]
    public async Task MatchingThenScreenFailureStopsAndCleansUp()
    {
        var sender = new Sender();
        var profile = Profile(); profile.ColorCondition.Enabled = true;
        var screen = new Screen(() => sender.Downs == 0 ? new(0, 255, 0) : throw new IOException("read failed"));
        var runtime = new RuntimeController(new SequenceRunner(sender, screen), sender);
        runtime.Arm(); await runtime.TriggerAsync(profile);
        Assert.Equal(1, sender.Downs); Assert.Equal(1, sender.Ups);
        Assert.True(sender.Releases > 0);
        Assert.Contains("执行失败", runtime.LastMessage);
    }

    [Fact]
    public async Task StopInterruptsLongHold()
    {
        var sender = new Sender(); var profile = Profile();
        profile.DurationSeconds = 6; profile.Steps[0].HoldMs = 60000;
        var runtime = new RuntimeController(new SequenceRunner(sender), sender);
        runtime.Arm(); var run = runtime.TriggerAsync(profile);
        using var timeout = new CancellationTokenSource(3000);
        while (Volatile.Read(ref sender.Downs) == 0) await Task.Delay(5, timeout.Token);
        runtime.Stop(); await run.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(sender.Downs, sender.Ups); Assert.False(runtime.IsBusy);
    }

    [Fact]
    public void ColorToleranceAppliesToEveryChannel()
    {
        var c = new ColorCondition { Target = "#102030", Tolerance = 5 };
        Assert.True(ColorMatcher.Matches(new(21, 27, 48), c));
        Assert.False(ColorMatcher.Matches(new(22, 32, 48), c));
        Assert.Throws<InvalidDataException>(() => RgbColor.Parse("green"));
    }

    [Theory]
    [InlineData(-1)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(86401)]
    public void InvalidDurationRejected(double seconds)
    {
        var p = Profile(); p.DurationSeconds = seconds;
        Assert.Throws<InvalidDataException>(() => AutomationValidation.Validate(p));
    }

    [Theory]
    [InlineData(-1)] [InlineData(1000)]
    public void MillisecondRemainderMustBeWithinOneSecond(int milliseconds)
    {
        var profile = Profile(); profile.DurationSeconds = 6; profile.DurationMilliseconds = milliseconds;
        Assert.Throws<InvalidDataException>(() => AutomationValidation.Validate(profile));
    }

    [Fact]
    public async Task NewOptionsRoundTripAndLegacyDefaultsRemainUnlimited()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            await File.WriteAllTextAsync(path, """{"Version":2,"Profiles":[{"Name":"old","Steps":[{"Key":"E","WaitAfterMs":80,"HoldMs":30}]}]}""");
            var store = new JsonConfigStore(path);
            var config = await store.LoadAsync(); var p = config.Profiles[0];
            Assert.Equal(0, p.DurationSeconds); Assert.False(p.ColorCondition.Enabled);
            p.DurationSeconds = 6; p.AlternateStartHotkey = "F";
            p.ColorCondition = new() { Enabled = true, X = -20, Y = 50, Target = "#123456", Tolerance = 7 };
            await store.SaveAsync(config);
            var loaded = (await store.LoadAsync()).Profiles[0];
            Assert.Equal(6, loaded.DurationSeconds); Assert.Equal("F", loaded.AlternateStartHotkey);
            Assert.Equal(-20, loaded.ColorCondition.X); Assert.Equal("#123456", loaded.ColorCondition.Target);
            Assert.True(loaded.ColorCondition.Enabled); Assert.Equal(7, loaded.ColorCondition.Tolerance);
        }
        finally { File.Delete(path); File.Delete(path + ".bak"); }
    }

    [Fact]
    public async Task VersionFiveMigrationSplitsFractionalSecondsAndEnablesOnlyFirstProfile()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            await File.WriteAllTextAsync(path, """{"Version":5,"Profiles":[{"Name":"first","DurationSeconds":6.3,"Steps":[{"Key":"E","WaitAfterMs":80}]},{"Name":"second","DurationSeconds":2.125,"Steps":[{"Key":"F","WaitAfterMs":80}]}]}""");
            var config = await new JsonConfigStore(path).LoadAsync();
            Assert.Equal(JsonConfigStore.CurrentVersion, config.Version);
            Assert.Equal(6, config.Profiles[0].DurationSeconds);
            Assert.Equal(300, config.Profiles[0].DurationMilliseconds);
            Assert.Equal(2, config.Profiles[1].DurationSeconds);
            Assert.Equal(125, config.Profiles[1].DurationMilliseconds);
            Assert.True(config.Profiles[0].IsInActivationPool);
            Assert.False(config.Profiles[1].IsInActivationPool);
        }
        finally { File.Delete(path); File.Delete(path + ".bak"); }
    }

    [Fact]
    public async Task VersionSixMigrationPreservesActivationPoolAndMilliseconds()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            await File.WriteAllTextAsync(path, """{"Version":6,"Profiles":[{"Name":"first","DurationSeconds":6,"DurationMilliseconds":300,"IsInActivationPool":false,"SequenceMode":true,"Steps":[{"Key":"E","WaitAfterMs":80,"HoldMs":30},{"Key":"F","WaitAfterMs":100,"HoldMs":30}]},{"Name":"second","DurationSeconds":3,"DurationMilliseconds":20,"IsInActivationPool":true,"Steps":[{"Key":"D8","WaitAfterMs":80,"HoldMs":30}]}]}""");
            var config = await new JsonConfigStore(path).LoadAsync();
            Assert.Equal(JsonConfigStore.CurrentVersion, config.Version);
            Assert.Equal(300, config.Profiles[0].DurationMilliseconds);
            Assert.False(config.Profiles[0].IsInActivationPool);
            Assert.True(config.Profiles[0].SequenceMode);
            Assert.Equal(50, config.Profiles[0].Steps[0].WaitAfterMs);
            Assert.Equal(70, config.Profiles[0].Steps[1].WaitAfterMs);
            Assert.True(config.Profiles[1].IsInActivationPool);
        }
        finally { File.Delete(path); File.Delete(path + ".bak"); }
    }
    [Fact]
    public async Task VersionSevenGroupingUpgradePreservesTimingAndParticipation()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            await File.WriteAllTextAsync(path, """{"Version":7,"Profiles":[{"Name":"Existing","IsInActivationPool":true,"Steps":[{"Key":"E","WaitAfterMs":80,"HoldMs":30}]}]}""");
            var store = new JsonConfigStore(path);
            var config = await store.LoadAsync();
            Assert.Equal(JsonConfigStore.CurrentVersion, config.Version);
            var profile = config.Profiles[0];
            Assert.Equal("未分组", profile.GroupName);
            Assert.Equal(80, profile.Steps[0].WaitAfterMs);
            Assert.Equal(30, profile.Steps[0].HoldMs);
            Assert.True(profile.IsInActivationPool);
            profile.GroupName = "  配套方案  ";
            await store.SaveAsync(config);
            var loaded = (await store.LoadAsync()).Profiles[0];
            Assert.Equal("配套方案", loaded.GroupName);
            Assert.Equal("Existing", loaded.Name);
            loaded.GroupName = " ";
            Assert.Equal("未分组", loaded.GroupName);
        }
        finally { File.Delete(path); File.Delete(path + ".bak"); }
    }
}
