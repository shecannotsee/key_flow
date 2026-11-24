using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using KeyFlow.Core;
using KeyFlow.Windows;
using Xunit;

namespace KeyFlow.Windows.Tests;

public sealed class ScreenColorTests
{
    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint x, uint y, uint data, nuint extra);

    [Fact]
    public async Task ClickCaptureCancellationRemovesTemporaryListener()
    {
        using var stop = new CancellationTokenSource();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var capture = ScreenClickCapture.CaptureAsync(null, stop.Token, () => ready.SetResult());
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(3)); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => capture.WaitAsync(TimeSpan.FromSeconds(3)));
    }

    private sealed class CountingSender : IKeySender
    {
        public int Downs, Ups;
        public void KeyDown(string key) => Downs++;
        public void KeyUp(string key) => Ups++;
        public void ReleaseAll() { }
    }

    [Fact]
    public void VisibleColorTransitionTriggersTimedKeySequence()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            DispatcherTimer? timer = null;
            using var stop = new CancellationTokenSource(5000);
            try
            {
                window = new Window { Width = 160, Height = 160, Left = 40, Top = 40,
                    WindowStyle = WindowStyle.None, Topmost = true, ShowInTaskbar = false,
                    Background = Brushes.Black };
                window.Show(); window.UpdateLayout();
                var point = window.PointToScreen(new Point(80, 80));
                var sender = new CountingSender();
                var profile = new KeyProfile { DurationSeconds = .2, Steps = [new("E", 30) { HoldMs = 5 }] };
                var condition = new ColorCondition { X = (int)point.X, Y = (int)point.Y, Target = "#00FF00", Tolerance = 2 };
                var executions = 0;
                timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                timer.Tick += (_, _) => { timer.Stop(); window.Background = Brushes.Lime; };
                timer.Start();
                var monitorTask = Task.Run(async () =>
                {
                    try
                    {
                        await new ColorTriggerMonitor(new ScreenColorReader()).RunAsync(condition, 100, async token =>
                        {
                            executions++;
                            await new SequenceRunner(sender).RunAsync(profile, token);
                            stop.Cancel();
                        }, stop.Token);
                    }
                    catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                });
                var frame = new DispatcherFrame();
                _ = monitorTask.ContinueWith(_ => window.Dispatcher.BeginInvoke(new Action(() => frame.Continue = false)));
                Dispatcher.PushFrame(frame);
                monitorTask.GetAwaiter().GetResult();
                Assert.Equal(1, executions); Assert.True(sender.Downs > 1); Assert.Equal(sender.Downs, sender.Ups);
            }
            catch (Exception e) { error = e; }
            finally { stop.Cancel(); timer?.Stop(); window?.Close(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }

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
                // The temporary hook consumes this test click; sample the fixed region, not the cursor location.
                using var stop = new CancellationTokenSource(3000);
                using var ready = new ManualResetEventSlim();
                var capture = ScreenClickCapture.CaptureAsync(new ColorCondition { X = (int)position.X, Y = (int)position.Y, Width = 5, Height = 5 }, stop.Token, ready.Set);
                Assert.True(ready.Wait(TimeSpan.FromSeconds(2)));
                mouse_event(0x0002, 0, 0, 0, 0);
                mouse_event(0x0004, 0, 0, 0, 0);
                var sampled = capture.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                Assert.Equal((int)position.X, sampled.X); Assert.Equal(5, sampled.Width);
                Assert.Equal(actual, sampled.Color);
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
        var p = new KeyProfile { StartHotkey = "X", AlternateStartHotkey = "F", DurationSeconds = 6,
            ColorTriggerEnabled = true, TriggerDebounceMs = 250, Steps = [new("E")] };
        ProfileOperations.ValidateForRepeat([p]);
        var copy = ProfileOperations.Copy(p);
        copy.ColorCondition.X = 99;
        Assert.Equal(0, p.ColorCondition.X); Assert.Equal(6, copy.DurationSeconds);
        Assert.True(copy.ColorTriggerEnabled); Assert.Equal(250, copy.TriggerDebounceMs);
        p.AlternateStartHotkey = "E";
        Assert.Throws<InvalidDataException>(() => ProfileOperations.ValidateForRepeat([p]));
    }
}
