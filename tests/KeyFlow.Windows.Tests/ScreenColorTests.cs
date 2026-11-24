using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using KeyFlow.Core;
using KeyFlow.Windows;
using Xunit;

namespace KeyFlow.Windows.Tests;

public sealed class ScreenColorTests
{
    [Fact]
    public void ReadsActualVisibleWindowPixels()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                window = new Window { Width = 160, Height = 160, Left = 40, Top = 40,
                    WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Topmost = true,
                    ShowInTaskbar = false, Background = new SolidColorBrush(Color.FromRgb(32, 160, 224)) };
                window.Show();
                var frame = new DispatcherFrame();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
                timer.Start(); Dispatcher.PushFrame(frame);
                var position = window.PointToScreen(new Point(80, 80));
                var actual = new ScreenColorReader().Read((int)position.X, (int)position.Y);
                Assert.True(ColorMatcher.Matches(actual, new() { Target = "#20A0E0", Tolerance = 2 }), $"Actual {actual}");
                Assert.Throws<InvalidOperationException>(() => new ScreenColorReader().Read(int.MaxValue, int.MaxValue));
            }
            catch (Exception e) { error = e; }
            finally { window?.Close(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }

    [Fact]
    public void CopyAndValidationIncludeAlternateKeyAndColor()
    {
        var p = new KeyProfile { StartHotkey = "X", AlternateStartHotkey = "F", DurationSeconds = 6, Steps = [new("E")] };
        ProfileOperations.ValidateForRepeat([p]);
        var copy = ProfileOperations.Copy(p);
        copy.ColorCondition.X = 99;
        Assert.Equal(0, p.ColorCondition.X); Assert.Equal(6, copy.DurationSeconds);
        p.AlternateStartHotkey = "E";
        Assert.Throws<InvalidDataException>(() => ProfileOperations.ValidateForRepeat([p]));
    }
}
