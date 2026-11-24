using Gma.System.MouseKeyHook;
using System.Windows.Forms;
using KeyFlow.Core;

namespace KeyFlow.Windows;

public readonly record struct ScreenSelection(int X, int Y, int Width, int Height, RgbColor? Color);

// Temporary listener, independent of keyboard output and permanent hotkey subscriptions.
public static class ScreenClickCapture
{
    public static Task<ScreenSelection> CaptureAsync(ColorCondition? fixedRegion, CancellationToken stop, Action? ready = null)
    {
        var completion = new TaskCompletionSource<ScreenSelection>(TaskCreationOptions.RunContinuationsAsynchronously);
        var snapshot = fixedRegion?.Copy();
        var thread = new Thread(() =>
        {
            ScreenSelection? result = null;
            Exception? failure = null;
            bool cancelled = false;
            try
            {
                using var loop = new ApplicationContext();
                using var events = Hook.GlobalEvents();
                using var timer = new System.Windows.Forms.Timer { Interval = 50 };
                var screen = new ScreenColorReader();
                (int X, int Y)? start = null;
                events.MouseDownExt += (_, e) =>
                {
                    if (e.Button != MouseButtons.Left) return;
                    e.Handled = true;
                    if (start is not null) return;
                    try
                    {
                        start = screen.CursorPosition();
                        if (snapshot is not null)
                            result = new(snapshot.X, snapshot.Y, snapshot.Width, snapshot.Height, ColorMatcher.Sample(screen, snapshot));
                    }
                    catch (Exception ex) { failure = ex; }
                };
                events.MouseUpExt += (_, e) =>
                {
                    if (e.Button != MouseButtons.Left || (start is null && failure is null)) return;
                    e.Handled = true;
                    try
                    {
                        if (snapshot is null && start is { } from)
                        {
                            var end = screen.CursorPosition();
                            result = new(Math.Min(from.X, end.X), Math.Min(from.Y, end.Y),
                                checked(Math.Abs(end.X - from.X) + 1), checked(Math.Abs(end.Y - from.Y) + 1), null);
                        }
                    }
                    catch (Exception ex) { failure = ex; }
                    loop.ExitThread();
                };
                events.KeyDown += (_, e) =>
                {
                    if (e.KeyCode != Keys.Escape) return;
                    cancelled = true; loop.ExitThread();
                };
                timer.Tick += (_, _) =>
                {
                    if (stop.IsCancellationRequested) { cancelled = true; loop.ExitThread(); }
                };
                if (stop.IsCancellationRequested) cancelled = true;
                else { timer.Start(); ready?.Invoke(); Application.Run(loop); }
            }
            catch (Exception ex) { failure = ex; }
            // Finish only after listener disposal; the UI can safely register hotkeys again.
            if (failure is not null) completion.TrySetException(failure);
            else if (cancelled || result is null) completion.TrySetCanceled();
            else completion.TrySetResult(result.Value);
        }) { IsBackground = true, Name = "KeyFlow screen capture" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return completion.Task;
    }
}
