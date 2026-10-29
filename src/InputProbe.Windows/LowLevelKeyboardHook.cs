using System.Diagnostics;
using System.Runtime.InteropServices;

namespace InputProbe.Windows;

public sealed class LowLevelKeyboardHook : IDisposable
{
    private readonly HookProc _callback;
    private IntPtr _hook;
    public event Action<int, bool>? KeyChanged;
    public bool IsInstalled => _hook != IntPtr.Zero;
    public int InstallError { get; }

    public LowLevelKeyboardHook()
    {
        _callback = OnHook;
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        _hook = SetWindowsHookEx(13, _callback, GetModuleHandle(null), 0);
        InstallError = _hook == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
    }

    private IntPtr OnHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var message = wParam.ToInt32();
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (message is 0x100 or 0x104) KeyChanged?.Invoke((int)data.vkCode, true);
            if (message is 0x101 or 0x105) KeyChanged?.Invoke((int)data.vkCode, false);
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        GC.SuppressFinalize(this);
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] private struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}
