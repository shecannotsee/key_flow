using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using InputProbe.Windows;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length < 1) { Console.Error.WriteLine("Pass isolated App.exe path, optionally --dd and DLL path (default config, F8 start)."); return 2; }
        var start = new ProcessStartInfo(System.IO.Path.GetFullPath(args[0])) { WorkingDirectory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(args[0]))! };
        foreach (var argument in args.Skip(1)) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        if (!process.WaitForInputIdle(10000))
        {
            Console.Error.WriteLine("App did not initialize its message loop within 10 seconds.");
            process.CloseMainWindow();
            return 2;
        }
        var app = new Application();
        var box = new TextBox { AcceptsReturn = true, Text = "Input Probe integration receiver", FontSize = 20 };
        var window = new Window { Title = "Input Probe · independent test receiver", Width = 600, Height = 300, Content = box };
        var sender = new SendInputKeySender();
        var down = 0; var up = 0; var stoppedCount = 0; var result = 1; var phase = 0;
        var held = new Dictionary<Key, long>();
        var durations = new List<double>();
        box.PreviewKeyDown += (_, e) => { if (e.Key is Key.D1 or Key.D2) { down++; held[e.Key] = Stopwatch.GetTimestamp(); } };
        box.PreviewKeyUp += (_, e) => { if (e.Key is Key.D1 or Key.D2) { up++; if (held.Remove(e.Key, out var timestamp)) durations.Add(Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds); } };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            try
            {
                if (phase == 0)
                {
                    window.Topmost = true; window.Activate(); box.Focus();
                    SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(window).Handle);
                }
                else if (phase == 1)
                {
                    AssertForeground(window);
                    sender.Press("F8");
                }
                else if (phase == 3)
                {
                    AssertForeground(window);
                    sender.KeyDown("Ctrl"); sender.KeyDown("Shift"); sender.Press("F10");
                    sender.KeyUp("Shift"); sender.KeyUp("Ctrl");
                }
                else if (phase == 4) stoppedCount = down;
                else if (phase == 5)
                {
                    result = down >= 4 && down == up && held.Count == 0 && down == stoppedCount ? 0 : 1;
                    Console.WriteLine($"result={result}, down={down}, up={up}, afterStop={down-stoppedCount}, receiverHoldMs=[{string.Join(",", durations.Select(d => d.ToString("F1")))}]");
                    window.Close();
                }
                phase++;
            }
            catch (Exception e) { Console.Error.WriteLine(e); window.Close(); }
        };
        window.Closed += (_, _) => { timer.Stop(); sender.ReleaseAll(); process.CloseMainWindow(); };
        timer.Start(); app.Run(window);
        return result;
    }
    private static void AssertForeground(Window window)
    {
        if (InputDiagnostics.ForegroundHandle != new System.Windows.Interop.WindowInteropHelper(window).Handle)
            throw new InvalidOperationException("Receiver lost focus; refusing to send test keys to another window.");
    }
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
}
