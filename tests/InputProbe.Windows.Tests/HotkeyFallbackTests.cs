using System.Reflection;
using System.Windows.Interop;
using InputProbe.App;
using Xunit;

public sealed class HotkeyFallbackTests
{
    [Fact]
    public void ForegroundChangeClearsMissedModifierReleaseAndReportsCandidate()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var window = new HwndSource(new HwndSourceParameters("focus-transition-test"));
                nint foreground = 1;
                using var hotkeys = new SystemHotkeys(window.Handle, _ => false, () => foreground);
                Assert.Equal(0, hotkeys.Register(9, 0x87));
                var count = 0;
                var diagnostics = new List<string>();
                hotkeys.Pressed += _ => count++;
                hotkeys.Diagnostic += diagnostics.Add;
                var input = typeof(SystemHotkeys).GetMethod("OnRawKey", BindingFlags.NonPublic | BindingFlags.Instance)!;
                input.Invoke(hotkeys, new object[] { 0x12u, true }); // Alt down before switching windows.
                input.Invoke(hotkeys, new object[] { 0x87u, true });
                Assert.Equal(0, count); // Extra modifier correctly rejects the shortcut.
                input.Invoke(hotkeys, new object[] { 0x87u, false });
                foreground = 2; // Alt-up was missed, but async state says it is released.
                input.Invoke(hotkeys, new object[] { 0x87u, true });
                Assert.Equal(1, count);
                Assert.Contains(diagnostics, line => line.Contains("matched=False"));
                Assert.Contains(diagnostics, line => line.Contains("matched=True"));
            }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    [Fact]
    public void RawInputRegistrationAndEdgeDispatchWorkWithoutPollingState()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var window = new HwndSource(new HwndSourceParameters("raw-input-test"));
                using var hotkeys = new SystemHotkeys(window.Handle, _ => false);
                Assert.Equal(0, hotkeys.RawInputError);
                Assert.Equal(0, hotkeys.Register(8, 0x84, 6));
                var count = 0;
                hotkeys.Pressed += _ => count++;
                var input = typeof(SystemHotkeys).GetMethod("OnRawKey", BindingFlags.NonPublic | BindingFlags.Instance)!;
                input.Invoke(hotkeys, new object[] { 0x11u, true });
                input.Invoke(hotkeys, new object[] { 0x10u, true });
                input.Invoke(hotkeys, new object[] { 0x84u, true });
                input.Invoke(hotkeys, new object[] { 0x84u, true });
                Assert.Equal(1, count);
                Assert.Equal("RawInput", hotkeys.LastSource);
                hotkeys.Clear();
                input.Invoke(hotkeys, new object[] { 0x84u, true });
                Assert.Equal(1, count);
            }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    [Fact]
    public void PollDetectsEdgeWithoutMessageAndClearDisablesIt()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var window = new HwndSource(new HwndSourceParameters("fallback-test"));
                var down = new HashSet<uint>();
                using var hotkeys = new SystemHotkeys(window.Handle, key => down.Contains(key));
                Assert.Equal(0, hotkeys.Register(7, 0x85, 6)); // Ctrl+Shift+F22
                var count = 0;
                hotkeys.Pressed += _ => count++;
                var poll = typeof(SystemHotkeys).GetMethod("Poll", BindingFlags.NonPublic | BindingFlags.Instance)!;
                down.UnionWith([0x85u, 0x11u]); // Missing Shift must not trigger.
                poll.Invoke(hotkeys, null);
                Assert.Equal(0, count);
                down.Add(0x10);
                poll.Invoke(hotkeys, null);
                Assert.Equal(1, count);
                Assert.Equal("GetAsyncKeyState", hotkeys.LastSource);
                poll.Invoke(hotkeys, null);
                Assert.Equal(1, count); // Held key does not repeat.
                hotkeys.Clear();
                poll.Invoke(hotkeys, null);
                Assert.Equal(1, count);
            }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
