using System.Diagnostics;
using System.Runtime.InteropServices;
using InputProbe.Core;

namespace InputProbe.Windows;

public sealed class SendInputKeySender : IKeySender
{
    private readonly HashSet<string> _pressed = new(StringComparer.OrdinalIgnoreCase);
    public void KeyDown(string key) => Send(key, false);
    public void KeyUp(string key) => Send(key, true);
    public void ReleaseAll() { foreach (var key in _pressed.ToArray()) KeyUp(key); }

    private void Send(string key, bool up)
    {
        if (!KeyMap.TryGetValue(key, out var vk)) return;
        var input = new INPUT { type = 1, U = new INPUTUNION { ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? 2u : 0u } } };
        if (SendInput(1, [input], Marshal.SizeOf<INPUT>()) == 0) return;
        if (up) _pressed.Remove(key); else _pressed.Add(key);
    }

    private static readonly Dictionary<string, ushort> KeyMap = Enumerable.Range(0, 10)
        .ToDictionary(i => $"D{i}", i => (ushort)(0x30 + i))
        .Concat(Enumerable.Range(1, 12).ToDictionary(i => $"F{i}", i => (ushort)(0x70 + i - 1)))
        .Concat(new Dictionary<string, ushort> { ["Space"] = 0x20, ["Enter"] = 0x0D, ["Escape"] = 0x1B })
        .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);

    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public INPUTUNION U; }
    [StructLayout(LayoutKind.Explicit)] private struct INPUTUNION { [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public nint dwExtraInfo; }
}

public sealed class ForegroundWindowGuard : IForegroundWindowGuard
{
    public bool IsTargetForeground(string targetProcess)
    {
        if (string.IsNullOrWhiteSpace(targetProcess)) return false;
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;
        GetWindowThreadProcessId(hwnd, out var pid);
        try { return string.Equals(Process.GetProcessById((int)pid).ProcessName, targetProcess, StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
