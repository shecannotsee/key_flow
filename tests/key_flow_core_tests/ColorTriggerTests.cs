using key_flow_core;
using Xunit;

namespace key_flow_core_tests;

public sealed class ColorTriggerTests
{
    private sealed class Screen(Func<RgbColor> read) : IScreenColorReader
    {
        public RgbColor Read(int x, int y) => read();
    }
    private sealed class Sender : IKeySender
    {
        public int Downs, Ups, Releases;
        public void KeyDown(string key) => Downs++;
        public void KeyUp(string key) => Ups++;
        public void ReleaseAll() => Releases++;
    }

    [Fact]
    public void StartupActiveAndBriefFlashesDoNotFire()
    {
        var edge = new ActivationEdge(100);
        Assert.False(edge.Observe(true, 0));
        Assert.False(edge.Observe(true, 500));
        Assert.False(edge.Observe(false, 600));
        Assert.False(edge.Observe(true, 650));
        Assert.False(edge.Observe(true, 900));
        Assert.False(edge.IsArmed);
        Assert.False(edge.Observe(false, 1000));
        Assert.False(edge.Observe(false, 1100));
        Assert.True(edge.IsArmed);
        Assert.False(edge.Observe(true, 1200));
        Assert.False(edge.Observe(false, 1250));
        Assert.False(edge.Observe(true, 1300));
        Assert.True(edge.Observe(true, 1400));
        Assert.False(edge.Observe(true, 3000));
    }

    [Fact]
    public async Task TwoActivationsRunSequentiallyAndRequireInactiveAfterCompletion()
    {
        using var stop = new CancellationTokenSource(5000);
        var reads = 0; var runs = 0; var concurrent = 0; var maxConcurrent = 0;
        var states = new List<ColorTriggerState>();
        var screen = new Screen(() =>
        {
            var n = ++reads;
            // Initial active must not fire; long active after first run must not retrigger.
            var active = n <= 3 || (n >= 7 && n <= 14) || n >= 18;
            return active ? new(0, 255, 0) : new(0, 0, 0);
        });
        var monitor = new ColorTriggerMonitor(screen);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => monitor.RunAsync(new(), 50, async token =>
        {
            maxConcurrent = Math.Max(maxConcurrent, ++concurrent);
            runs++;
            await Task.Delay(40, token);
            concurrent--;
            if (runs == 2) stop.Cancel();
        }, stop.Token, states.Add));
        Assert.Equal(2, runs); Assert.Equal(1, maxConcurrent);
        Assert.Equal(2, states.Count(s => s == ColorTriggerState.Executing));
        Assert.True(reads >= 19);
    }

    [Fact]
    public async Task CancelWhileWatchingProducesNoWorkflow()
    {
        using var stop = new CancellationTokenSource();
        var runs = 0;
        var read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = new ColorTriggerMonitor(new Screen(() => { read.TrySetResult(); return new(0, 0, 0); }))
            .RunAsync(new(), 100, _ => { runs++; return Task.CompletedTask; }, stop.Token);
        await read.Task.WaitAsync(TimeSpan.FromSeconds(2)); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(0, runs);
    }

    [Fact]
    public async Task ReadFailurePropagatesWithoutRunningWorkflow()
    {
        var runs = 0;
        var monitor = new ColorTriggerMonitor(new Screen(() => throw new IOException("unavailable")));
        await Assert.ThrowsAsync<IOException>(() => monitor.RunAsync(new(), 100,
            _ => { runs++; return Task.CompletedTask; }, default));
        Assert.Equal(0, runs);
    }

    [Fact]
    public async Task WorkflowFailureStopsMonitoringInsteadOfRetrying()
    {
        var reads = 0; var runs = 0;
        var monitor = new ColorTriggerMonitor(new Screen(() => ++reads <= 3 ? new(0, 0, 0) : new(0, 255, 0)));
        using var stop = new CancellationTokenSource(3000);
        await Assert.ThrowsAsync<InvalidOperationException>(() => monitor.RunAsync(new(), 50,
            _ => { runs++; throw new InvalidOperationException("output failed"); }, stop.Token));
        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task ActivationExecutesExistingTimedSequenceAndReleasesKeys()
    {
        var reads = 0; var sender = new Sender();
        var runtime = new RuntimeController(new SequenceRunner(sender), sender);
        var profile = new KeyProfile { Enabled = true, DurationSeconds = .15, Steps = [new("E", 20) { HoldMs = 5 }] };
        using var stop = new CancellationTokenSource(3000);
        var monitor = new ColorTriggerMonitor(new Screen(() => ++reads <= 3 ? new(0, 0, 0) : new(0, 255, 0)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => monitor.RunAsync(new(), 50, async _ =>
        {
            runtime.Arm(); await runtime.TriggerAsync(profile); stop.Cancel();
        }, stop.Token));
        Assert.True(sender.Downs > 1); Assert.Equal(sender.Downs, sender.Ups);
        Assert.Equal(1, sender.Releases); Assert.False(runtime.IsBusy); Assert.False(runtime.LastRunFailed);
    }

    [Fact]
    public async Task StopDuringWorkflowCancelsOutputAndDoesNotRearm()
    {
        var reads = 0; var runs = 0; var sender = new Sender();
        var runtime = new RuntimeController(new SequenceRunner(sender), sender);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stop = new CancellationTokenSource(5000);
        var monitor = new ColorTriggerMonitor(new Screen(() => ++reads <= 3 ? new(0, 0, 0) : new(0, 255, 0)));
        var task = monitor.RunAsync(new(), 50, async token =>
        {
            runs++;
            var profile = new KeyProfile { Enabled = true, DurationSeconds = 60, Steps = [new("E", 20) { HoldMs = 5 }] };
            runtime.Arm();
            var running = runtime.TriggerAsync(profile);
            entered.SetResult();
            await running;
        }, stop.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        stop.Cancel(); runtime.Stop();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(1, runs); Assert.False(runtime.IsBusy); Assert.Equal(sender.Downs, sender.Ups);
        Assert.True(sender.Releases > 0);
    }

    [Fact]
    public async Task VersionThreeMigratesAndTriggerOptionsRoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            await File.WriteAllTextAsync(path, """{"Version":3,"Profiles":[{"Name":"old","Steps":[{"Key":"E"}]}]}""");
            var store = new JsonConfigStore(path); var config = await store.LoadAsync();
            Assert.Equal(4, config.Version); Assert.False(config.Profiles[0].ColorTriggerEnabled);
            var p = config.Profiles[0]; p.ColorTriggerEnabled = true; p.TriggerDebounceMs = 250; p.DurationSeconds = 6;
            await store.SaveAsync(config);
            var loaded = (await store.LoadAsync()).Profiles[0];
            Assert.True(loaded.ColorTriggerEnabled); Assert.Equal(250, loaded.TriggerDebounceMs);
            p.DurationSeconds = 0;
            Assert.Throws<InvalidDataException>(() => AutomationValidation.Validate(p));
            p.DurationSeconds = 6; p.TriggerDebounceMs = 0;
            Assert.Throws<InvalidDataException>(() => AutomationValidation.Validate(p));
        }
        finally { File.Delete(path); File.Delete(path + ".bak"); }
    }
}
