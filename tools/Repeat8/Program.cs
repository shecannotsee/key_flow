using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using InputProbe.Windows;
using InputProbe.Core;

internal static class Program
{
    private static readonly CancellationTokenSource Stop = new();
    private static DdKeySender? sender;
    // Keep delegate rooted for the entire native console-handler lifetime.
    private static readonly Handler CloseHandler = OnClose;

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length is < 1 or > 2 || (args.Length == 2 && args[1] != "--check"))
        {
            Console.Error.WriteLine("Usage: Repeat8 <DD HID DLL path> [--check]");
            return 2;
        }
        var checkOnly = args.Length == 2;
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; Stop.Cancel(); };
        if (!SetConsoleCtrlHandler(CloseHandler, true))
        {
            Console.Error.WriteLine($"Console stop handler failed: {Marshal.GetLastWin32Error()}");
            return 1;
        }
        try
        {
            var api = new DdNativeKeyboard(args[0]);
            var code = api.ToCode(0x38); // Top-row 8, not numpad 8.
            if (code <= 0) throw new InvalidOperationException($"DD_todc(0x38) failed: {code}");
            sender = new DdKeySender(api);
            Console.WriteLine($"DD HID ready. Top-row 8: VK=0x38, DD={code}.");
            if (checkOnly) { Console.WriteLine("Check passed; no key events sent."); return 0; }
            Console.WriteLine("每 80ms 按一次数字 8（按住 30ms）。Ctrl+C 或关闭窗口停止。");
            Console.WriteLine("立即开始；不要同时运行旧按键程序。");
            var clock = Stopwatch.StartNew();
            PulseLoop.Run(sender, [new KeyStep("D8", 80)], Stop.Token, onClick: count =>
            {
                if (count == 1 || count % 100 == 0)
                    Console.WriteLine($"DD 成功调用 {count} 次；运行 {clock.Elapsed.TotalSeconds:F1}s（不代表目标已接收）");
            });
            return 0;
        }
        catch (OperationCanceledException) when (Stop.IsCancellationRequested) { return 0; }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"停止：{exception}");
            return 1;
        }
        finally
        {
            Stop.Cancel();
            Release();
            SetConsoleCtrlHandler(CloseHandler, false);
        }
    }

    private static void Release()
    {
        try { sender?.Shutdown(); }
        catch (Exception exception) { Console.Error.WriteLine($"按键释放失败：{exception.Message}"); }
    }
    private static bool OnClose(uint signal)
    {
        if (signal is not (2 or 5 or 6)) return false;
        Stop.Cancel();
        Release();
        return true;
    }
    private delegate bool Handler(uint signal);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCtrlHandler(Handler handler, bool add);
}
