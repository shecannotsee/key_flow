using System.Diagnostics;
using key_flow_core;
using Xunit;

public sealed class PulseLoopTests
{
    [Fact]
    public async Task IntervalIncludesHoldAndSnapshotIgnoresLaterEdits()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var observed = new List<(string Key, double Time)>();
        var clock = Stopwatch.StartNew();
        var profile = new KeyProfile { Steps = [new("D8", 80), new("A", 80)] };
        var sender = new Fake(key =>
        {
            observed.Add((key, clock.Elapsed.TotalMilliseconds));
            profile.Steps[1].Key = "B";
            if (observed.Count == 4) stop.Cancel();
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SequenceRunner(sender).RunAsync(profile, stop.Token));
        Assert.Equal(new[] { "D8", "A", "D8", "A" }, observed.Select(x => x.Key));
        Assert.Equal(4, sender.Ups);
        var intervals = observed.Zip(observed.Skip(1), (a, b) => b.Time - a.Time).ToArray();
        Assert.All(intervals, ms => Assert.True(ms >= 70, $"Premature next press: {ms:F1}ms"));
        // Generous upper bound detects runaway delays, not OS scheduling precision.
        Assert.All(intervals, ms => Assert.True(ms < 1500, $"Delayed next press: {ms:F1}ms"));
    }

    [Fact]
    public async Task CancelInterruptsLongHoldAndReleases()
    {
        using var stop = new CancellationTokenSource();
        var down = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sender = new Fake(_ => down.TrySetResult());
        var run = new SequenceRunner(sender).RunAsync(new KeyProfile { Steps = [new("D8", 60000) { HoldMs = 60000 }] }, stop.Token);
        await down.Task.WaitAsync(TimeSpan.FromSeconds(3));
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(1, sender.Ups);
    }

    [Fact]
    public async Task StartSendsImmediatelyAndStopAllowsRestart()
    {
        var down = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sender = new Fake(_ => down.TrySetResult());
        var runtime = new RuntimeController(new SequenceRunner(sender), sender);
        var profile = new KeyProfile { Enabled = true, Steps = [new("D8")] };
        runtime.Arm();
        var run = runtime.TriggerAsync(profile);
        Assert.True(runtime.IsBusy);
        await down.Task.WaitAsync(TimeSpan.FromSeconds(2));
        runtime.Stop();
        await run;
        Assert.False(runtime.IsBusy);
        Assert.Equal(RuntimeState.Stopped, runtime.State);
        Assert.True(sender.Ups >= 1);
        runtime.Arm();
        Assert.Equal(RuntimeState.Armed, runtime.State);
    }

    [Fact]
    public async Task LegacyConfigKeepsTimingAndSaveKeepsBackup()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pulse-{Guid.NewGuid():N}.json");
        const string old = """{"Version":1,"Profiles":[{"Name":"旧配置","Steps":[{"Key":"D8","WaitAfterMs":80}]}]}""";
        try
        {
            await File.WriteAllTextAsync(path, old);
            var store = new JsonConfigStore(path);
            var config = await store.LoadAsync();
            Assert.Equal(JsonConfigStore.CurrentVersion, config.Version);
            Assert.Equal(50, config.Profiles[0].Steps[0].HoldMs);
            Assert.Equal(130, config.Profiles[0].Steps[0].WaitAfterMs);
            Assert.Equal(old, await File.ReadAllTextAsync(path));
            await store.SaveAsync(config);
            Assert.Equal(old, await File.ReadAllTextAsync(path + ".bak"));
            var again = await store.LoadAsync();
            Assert.Equal(130, again.Profiles[0].Steps[0].WaitAfterMs);
        }
        finally { File.Delete(path); File.Delete(path + ".bak"); }
    }

    [Fact]
    public async Task NewConfigMatchesSuccessfulConsoleParameters()
    {
        var config = await new JsonConfigStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")).LoadAsync();
        var step = Assert.Single(Assert.Single(config.Profiles).Steps);
        Assert.Equal("D8", step.Key);
        Assert.Equal(30, step.HoldMs);
        Assert.Equal(80, step.WaitAfterMs);
    }

    private sealed class Fake(Action<string> down) : IKeySender
    {
        public int Ups;
        public void KeyDown(string key) => down(key);
        public void KeyUp(string key) => Ups++;
        public void ReleaseAll() { }
    }
}
