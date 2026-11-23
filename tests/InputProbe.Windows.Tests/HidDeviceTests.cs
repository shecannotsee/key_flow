using System.IO;
using InputProbe.Core;
using InputProbe.Windows;
using Xunit;

public sealed class HidDeviceTests
{
    [Fact]
    public void ComboReportsUseHidUsagesAndReleaseIndividually()
    {
        var wire = new Wire();
        using var sender = new HidDeviceKeySender(wire);
        sender.KeyDown("Ctrl"); sender.KeyDown("A");
        Assert.Equal(new byte[] {1,0,4,0,0,0,0,0}, wire.Reports[^1]);
        sender.KeyUp("A");
        Assert.Equal(new byte[] {1,0,0,0,0,0,0,0}, wire.Reports[^1]);
        sender.ReleaseAll();
        Assert.Equal(new byte[8], wire.Reports[^1]);
    }
    [Fact]
    public void InvalidAndSeventhKeyDoNotSendReports()
    {
        var wire = new Wire();
        using var sender = new HidDeviceKeySender(wire);
        foreach (var key in new[] {"A","B","C","D","E","F"}) sender.KeyDown(key);
        Assert.Throws<InvalidOperationException>(() => sender.KeyDown("G"));
        Assert.Throws<ArgumentException>(() => sender.KeyDown("Unknown"));
        Assert.Equal(6, wire.Reports.Count);
        sender.KeyDown("Ctrl"); // Modifiers do not occupy the six ordinary-key slots.
        Assert.Equal(1, wire.Reports[^1][0]);
    }
    [Fact]
    public void FailureDoesNotCommitStateAndShutdownClosesEvenIfReleaseFails()
    {
        var wire = new Wire { Fail = true };
        var sender = new HidDeviceKeySender(wire);
        Assert.Throws<IOException>(() => sender.KeyDown("A"));
        wire.Fail = false;
        sender.KeyDown("B");
        Assert.Equal(new byte[] {0,0,5,0,0,0,0,0}, wire.Reports[^1]);
        wire.Fail = true;
        Assert.Throws<IOException>(() => sender.Shutdown());
        Assert.True(wire.Closed);
        sender.KeyUp("B");
        Assert.Throws<ObjectDisposedException>(() => sender.KeyDown("B"));
    }
    [Fact]
    public async Task ExistingRunnerCanUseHidAdapterAndReleasesOnCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var wire = new Wire { OnSend = () => cancellation.Cancel() };
        using var sender = new HidDeviceKeySender(wire);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SequenceRunner(sender).RunAsync(
            new KeyProfile { Steps = [new("D1")] }, cancellation.Token));
        Assert.Equal(new byte[] {0,0,0x1E,0,0,0,0,0}, wire.Reports[0]);
        Assert.Equal(new byte[8], wire.Reports[^1]);
    }
    private sealed class Wire : IHidReportTransport
    {
        public List<byte[]> Reports { get; } = [];
        public bool Fail { get; set; }
        public bool Closed { get; private set; }
        public Action? OnSend { get; init; }
        public void Send(byte[] report) { if (Fail) throw new IOException("Injected failure"); Reports.Add((byte[])report.Clone()); OnSend?.Invoke(); }
        public void Dispose() => Closed = true;
    }
}
