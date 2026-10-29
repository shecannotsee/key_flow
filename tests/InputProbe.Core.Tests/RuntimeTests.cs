using InputProbe.Core;
using Xunit;

namespace InputProbe.Core.Tests;

public sealed class RuntimeTests
{
    [Fact]
    public async Task SequenceRepeatsInOrderUntilCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        RecordingSender? sender = null;
        sender = new RecordingSender(() =>
        {
            if (sender!.Clicks.Count >= 3) cancellation.Cancel();
        });
        var runner = new SequenceRunner(sender);
        var profile = new KeyProfile { Steps = [new("A", 1), new("B", 1)] };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(profile, cancellation.Token));

        Assert.Equal(["A", "B", "A"], sender.Clicks);
    }

    [Fact]
    public async Task StopReleasesKeysAndLeavesStoppedState()
    {
        var sender = new RecordingSender();
        var controller = new RuntimeController(new SequenceRunner(sender), sender);
        var profile = new KeyProfile { Enabled = true, Steps = [new("A", 1)] };
        controller.Arm();
        var running = controller.TriggerAsync(profile);
        await Task.Delay(20);
        controller.Stop("测试停止");
        await running;

        Assert.Equal(RuntimeState.Stopped, controller.State);
        Assert.True(sender.ReleaseCount > 0);
    }

    [Fact]
    public async Task ConfigRoundTripPreservesSequence()
    {
        var path = Path.Combine(Path.GetTempPath(), $"input-probe-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonConfigStore(path);
            var source = new AppConfig { Profiles = [new KeyProfile { Name = "测试", Steps = [new("F8", 25)] }] };
            await store.SaveAsync(source);
            var loaded = await store.LoadAsync();
            Assert.Equal("测试", loaded.Profiles[0].Name);
            Assert.Equal("F8", loaded.Profiles[0].Steps[0].Key);
            Assert.Equal(25, loaded.Profiles[0].Steps[0].WaitAfterMs);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ConfigRejectsUnknownVersionAndInvalidSteps()
    {
        var path = Path.Combine(Path.GetTempPath(), $"input-probe-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonConfigStore(path);
            await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(new AppConfig
            {
                Version = 99,
                Profiles = [new KeyProfile { Name = "测试", Steps = [new("A")] }]
            }));
            await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(new AppConfig
            {
                Profiles = [new KeyProfile { Name = "测试", Steps = [new("", -1)] }]
            }));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task EmptySequenceIsRejectedBeforeRunning()
    {
        var runner = new SequenceRunner(new RecordingSender());
        await Assert.ThrowsAsync<ArgumentException>(() => runner.RunAsync(new KeyProfile(), CancellationToken.None));
    }

    private sealed class RecordingSender(Action? onClick = null) : IKeySender
    {
        public List<string> Clicks { get; } = [];
        public int ReleaseCount { get; private set; }
        public void KeyDown(string key) { }
        public void KeyUp(string key) { Clicks.Add(key); onClick?.Invoke(); }
        public void ReleaseAll() => ReleaseCount++;
    }
}
