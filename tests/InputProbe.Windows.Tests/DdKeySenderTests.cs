using InputProbe.Core;
using InputProbe.Windows;
using Xunit;

public sealed class DdKeySenderTests
{
    [Fact]
    public void OpeningAndClosingWithoutStartingDoesNotInitializeDd()
    {
        var calls = 0;
        var api = new Api();
        var sender = new DdKeySender(new DeferredDdKeyboard(() => { calls++; return api; }));
        sender.ReleaseAll();
        Assert.Equal(0, calls);
        sender.KeyDown("D8");
        sender.KeyUp("D8");
        sender.KeyDown("D8");
        sender.Shutdown();
        Assert.Equal(1, calls);
        var unused = new DdKeySender(new DeferredDdKeyboard(() => throw new Exception("Must not load on close")));
        unused.Shutdown();
    }
    [Fact]
    public void ConvertsVirtualKeysAndReleasesCombinationInReverseOrder()
    {
        var api = new Api();
        var sender = new DdKeySender(api);
        sender.KeyDown("Ctrl"); sender.KeyDown("A"); sender.ReleaseAll();
        Assert.Equal(new[] { (1017, 1), (1065, 1), (1065, 2), (1017, 2) }, api.Events);
        sender.Shutdown();
        sender.KeyUp("A");
        Assert.Throws<ObjectDisposedException>(() => sender.KeyDown("A"));
    }
    [Fact]
    public void RejectsUnknownKeyAndFailedConversionBeforeSending()
    {
        var api = new Api { Conversion = -1 };
        var sender = new DdKeySender(api);
        Assert.Throws<ArgumentException>(() => sender.KeyDown("Unknown"));
        Assert.Throws<InvalidOperationException>(() => sender.KeyDown("A"));
        Assert.Empty(api.Events);
    }
    [Fact]
    public void FailedDownIsStillReleasedAndFailedReleaseDoesNotSkipOtherKeys()
    {
        var api = new Api();
        var sender = new DdKeySender(api);
        sender.KeyDown("Ctrl");
        api.FailCode = 1065;
        Assert.Throws<InvalidOperationException>(() => sender.KeyDown("A"));
        Assert.Throws<AggregateException>(() => sender.ReleaseAll());
        Assert.Contains((1017, 2), api.Events);
        api.FailCode = 0;
        sender.Shutdown();
        Assert.Equal((1065, 2), api.Events[^1]);
    }
    [Fact]
    public async Task RunnerCancellationReleasesDdKey()
    {
        using var cts = new CancellationTokenSource();
        var api = new Api { OnDown = () => cts.Cancel() };
        var sender = new DdKeySender(api);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new SequenceRunner(sender).RunAsync(new KeyProfile { Steps = [new("A")] }, cts.Token));
        Assert.Equal(new[] { (1065, 1), (1065, 2) }, api.Events);
    }
    [Fact]
    public async Task RuntimeLogsCleanupFailureWithoutFaultingOrContinuingLoop()
    {
        var api = new Api { FailCode = 1065 };
        var sender = new DdKeySender(api);
        var log = new Log();
        var runtime = new RuntimeController(new SequenceRunner(sender), sender, log);
        runtime.Arm();
        await runtime.TriggerAsync(new KeyProfile { Enabled = true, Steps = [new("A")] });
        runtime.Stop(); runtime.Shutdown();
        Assert.Equal(RuntimeState.Stopped, runtime.State);
        Assert.True(log.Errors >= 3);
        Assert.Equal(1, api.Events.Count(e => e.Action == 1));
    }
    private sealed class Log : IInputLogger
    {
        public int Errors;
        public void Info(string message) { }
        public void Error(string message, Exception exception) => Errors++;
    }
    private sealed class Api : IDdKeyboard
    {
        public int? Conversion;
        public int FailCode;
        public Action? OnDown;
        public List<(int Code, int Action)> Events { get; } = [];
        public int ToCode(int vk) => Conversion ?? vk + 1000;
        public int Key(int code, int action)
        {
            Events.Add((code, action));
            if (action == 1) OnDown?.Invoke();
            return code == FailCode ? 0 : 1;
        }
    }
}
