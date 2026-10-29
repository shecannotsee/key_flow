using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace InputProbe.Windows;

public sealed class LowLevelKeyboardHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const uint WmQuit = 0x0012;
    private readonly HookProc _callback;
    private readonly ManualResetEventSlim _ready = new(false);
    private Thread? _thread;
    private IntPtr _hook;
    private uint _threadId;
    private bool _disposed;

    public event Action<int, bool>? KeyChanged;
    public bool IsInstalled => Volatile.Read(ref _hook) != IntPtr.Zero;
    public int InstallError { get; private set; }

    public LowLevelKeyboardHook()
    {
        _callback = OnHook;
        _thread = new Thread(HookThread) { IsBackground = true, Name = "InputProbe.KeyboardHook" };
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(2));
    }

    private void HookThread()
    {
        _threadId = GetCurrentThreadId();
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        _hook = SetWindowsHookEx(WhKeyboardLl, _callback, GetModuleHandle(module?.ModuleName), 0);
        InstallError = _hook == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
        _ready.Set();
        if (_hook == IntPtr.Zero) return;

        while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr OnHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var message = wParam.ToInt32();
            var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            if (message is 0x100 or 0x104) KeyChanged?.Invoke((int)data.VkCode, true);
            else if (message is 0x101 or 0x105) KeyChanged?.Invoke((int)data.VkCode, false);
        }
        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_threadId != 0) PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        if (_thread is { IsAlive: true }) _thread.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
        GC.SuppressFinalize(this);
    }

    [StructLayout(LayoutKind.Sequential)] private struct KbdLlHookStruct { public uint VkCode, ScanCode, Flags, Time; public IntPtr DwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct Message { public IntPtr HWnd; public uint Msg; public IntPtr WParam, LParam; public uint Time; public int PtX, PtY; }
    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern int GetMessage(out Message message, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref Message message);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}
