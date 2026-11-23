using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using InputProbe.Core;

internal static class Program
{
    private static readonly CancellationTokenSource Stop = new();
    private static readonly Native.Handler Handler = OnClose;
    private static Sender? sender;

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var check = args.Contains("--check");
        var vk = args.Contains("--vk");
        if (args.Any(a => a is not ("--check" or "--vk"))) return 2;
        using var identity = WindowsIdentity.GetCurrent();
        var admin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        var size = Marshal.SizeOf<Native.Input>();
        var scan = Native.MapVirtualKey(0x38, 0);
        Console.WriteLine($"SendInput 独立测试；Administrator={admin}; INPUT size={size}; scan8=0x{scan:X}; mode={(vk ? "VirtualKey" : "ScanCode")}");
        if (size != (IntPtr.Size == 8 ? 40 : 28) || scan == 0) return 1;
        if (check) { Console.WriteLine("结构与键码检查完成，未发送输入；此检查不验证游戏接收。"); return 0; }
        if (!admin) { Console.Error.WriteLine("需要管理员权限，请使用 Start-SendInput-Demo.cmd。"); return 1; }
        Console.Title = "SendInput Demo — 8 / 80ms — Ctrl+C 停止";
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; Stop.Cancel(); };
        if (!Native.SetConsoleCtrlHandler(Handler, true)) return 1;
        sender = new Sender(vk, (ushort)scan);
        try
        {
            Console.WriteLine("立即重复数字 8：间隔 80ms、按住 30ms。切换至测试窗口观察。Ctrl+C 或关闭本窗口停止。");
            Console.WriteLine("请先停止其他按键工具。调用成功仅代表输入插入系统队列，不代表游戏接受。");
            PulseLoop.Run(sender, [new KeyStep("D8", 80) { HoldMs = 30 }], Stop.Token,
                count => { if (count == 1 || count % 100 == 0) Console.WriteLine($"{DateTimeOffset.Now:O} completed={count}; {Native.Foreground()}"); });
            return 0;
        }
        catch (OperationCanceledException) when (Stop.IsCancellationRequested) { return 0; }
        catch (Exception e) { Release(); Console.Error.WriteLine(e); Console.WriteLine("按 Enter 关闭。"); Console.ReadLine(); return 1; }
        finally { Release(); Native.SetConsoleCtrlHandler(Handler, false); }
    }
    private static void Release()
    {
        Stop.Cancel();
        try { sender?.Shutdown(); } catch (Exception e) { Console.Error.WriteLine($"释放失败：{e.Message}"); }
    }
    private static bool OnClose(uint signal)
    {
        if (signal is not (2 or 5 or 6)) return false;
        Release(); return true;
    }
}

internal sealed class Sender(bool virtualKey, ushort scan) : IKeySender
{
    private readonly object gate = new();
    private bool held, closed;
    public void KeyDown(string key)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (key != "D8") throw new ArgumentException("Demo only supports D8");
            if (held) return;
            held = true;
            Send(false);
        }
    }
    public void KeyUp(string key)
    {
        lock (gate)
        {
            if (!held) return;
            Send(true); held = false;
        }
    }
    public void ReleaseAll() { lock (gate) { try { KeyUp("D8"); } catch { KeyUp("D8"); } } }
    public void Shutdown() { lock (gate) { closed = true; ReleaseAll(); } }
    private void Send(bool up)
    {
        var input = new Native.Input { Type = 1, Data = new Native.Union { Keyboard = new Native.Keyboard
        { Vk = virtualKey ? (ushort)0x38 : (ushort)0, Scan = virtualKey ? (ushort)0 : scan,
          Flags = (virtualKey ? 0u : 8u) | (up ? 2u : 0u) } } };
        Marshal.SetLastPInvokeError(0);
        var sent = Native.SendInput(1, [input], Marshal.SizeOf<Native.Input>());
        var error = Marshal.GetLastPInvokeError();
        if (sent != 1) throw new InvalidOperationException($"SendInput {(up ? "up" : "down")}: sent={sent}/1, error={error}; {Native.Foreground()}. 错误码不能单独确定 UIPI 原因。");
    }
}

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct Input { public uint Type; public Union Data; }
    [StructLayout(LayoutKind.Explicit)] internal struct Union
    {
        [FieldOffset(0)] public Keyboard Keyboard;
        [FieldOffset(0)] public Mouse Mouse;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Keyboard { public ushort Vk, Scan; public uint Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct Mouse { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    internal delegate bool Handler(uint signal);
    [DllImport("user32.dll", SetLastError = true)] internal static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] internal static extern uint MapVirtualKey(uint code, uint type);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool SetConsoleCtrlHandler(Handler handler, bool add);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    internal static string Foreground()
    {
        var window = GetForegroundWindow(); GetWindowThreadProcessId(window, out var pid);
        return $"foreground=0x{window:X}, pid={pid}";
    }
}
