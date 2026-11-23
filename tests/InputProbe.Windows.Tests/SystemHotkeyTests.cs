using System.Windows.Interop;
using InputProbe.App;
using InputProbe.Windows;
using Xunit;

public sealed class SystemHotkeyTests
{
    [Fact]
    public void RegistrationConflictAndDisposeReleaseAreObservable()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var firstWindow = new HwndSource(new HwndSourceParameters("hotkey-test-1"));
                using var secondWindow = new HwndSource(new HwndSourceParameters("hotkey-test-2"));
                using var first = new SystemHotkeys(firstWindow.Handle);
                using var second = new SystemHotkeys(secondWindow.Handle);
                Assert.Equal(0, first.Register(1, 0x86, 0x6)); // Ctrl+Shift+F23 avoids normal shortcuts.
                Assert.NotEqual(0, second.Register(1, 0x86, 0x6));
                first.Clear();
                Assert.Equal(0, second.Register(1, 0x86, 0x6));
                second.Dispose();
                Assert.Equal(0, first.Register(1, 0x86, 0x6));
                var diagnostic = InputDiagnostics.DescribeForeground();
                Assert.Contains("toolIntegrity=", diagnostic);
                Assert.Contains("targetIntegrity=", diagnostic);
            }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "STA test timed out");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
