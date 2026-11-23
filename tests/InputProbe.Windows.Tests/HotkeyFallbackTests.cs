using System.Reflection;
using System.Windows.Interop;
using InputProbe.App;
using Xunit;

public sealed class HotkeyFallbackTests
{
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
