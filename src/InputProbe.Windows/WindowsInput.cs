using System.Diagnostics;
using System.Runtime.InteropServices;
using InputProbe.Core;

namespace InputProbe.Windows;

public enum SendInputMode
{
    VirtualKey,
    ScanCode
}

public sealed class SendInputKeySender(IInputLogger? logger = null, SendInputMode mode = SendInputMode.ScanCode) : IKeySender
{
    private readonly IInputLogger _logger = logger ?? new NullInputLogger();
    private readonly SendInputMode _mode = mode;
    private readonly HashSet<string> _pressed = new(StringComparer.OrdinalIgnoreCase);
    private bool _shutdown;
    public string LastStatus { get; private set; } = "尚未发送";
    public void KeyDown(string key) => Send(key, false);
    public void KeyUp(string key) => Send(key, true);
    public void ReleaseAll() { foreach (var key in _pressed.ToArray()) KeyUp(key); }
    public void Shutdown()
    {
        ReleaseAll();
        _shutdown = true;
        LastStatus = "输入后端已关闭";
        _logger.Info(LastStatus);
    }

    private void Send(string key, bool up)
    {
        if (_shutdown)
        {
            var exception = new InvalidOperationException("输入后端已关闭");
            _logger.Error("输入发送被拒绝", exception);
            throw exception;
        }
        if (!KeyMap.TryGetValue(key, out var vk))
        {
            LastStatus = $"未映射按键: {key}";
            var exception = new ArgumentException(LastStatus, nameof(key));
            _logger.Error(LastStatus, exception);
            throw exception;
        }
        var scanCode = MapVirtualKey(vk, 4);
        if (_mode == SendInputMode.ScanCode && scanCode == 0)
        {
            var exception = new InvalidOperationException($"无法映射扫描码: {key} (VK 0x{vk:X2})");
            _logger.Error(exception.Message, exception);
            throw exception;
        }
        var flags = up ? 0x0002u : 0u;
        ushort inputScan = 0;
        if (_mode == SendInputMode.ScanCode)
        {
            flags |= 0x0008u;
            if ((scanCode & 0xFF00) != 0) flags |= 0x0001u;
            inputScan = (ushort)(scanCode & 0x00FF);
        }
        var input = new INPUT
        {
            type = 1,
            U = new INPUTUNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = _mode == SendInputMode.VirtualKey ? vk : (ushort)0,
                    wScan = inputScan,
                    dwFlags = flags
                }
            }
        };
        if (SendInput(1, [input], Marshal.SizeOf<INPUT>()) == 0)
        {
            var error = Marshal.GetLastWin32Error();
            LastStatus = $"SendInput 失败: {error}";
            var exception = new System.ComponentModel.Win32Exception(error, LastStatus);
            _logger.Error(LastStatus, exception);
            throw exception;
        }
        LastStatus = $"已发送 {key} {(up ? "Up" : "Down")} ({_mode})";
        _logger.Info(LastStatus);
        if (up) _pressed.Remove(key); else _pressed.Add(key);
    }

    private static readonly Dictionary<string, ushort> KeyMap = Enumerable.Range(0, 10)
        .ToDictionary(i => $"D{i}", i => (ushort)(0x30 + i))
        .Concat(Enumerable.Range(0, 26).ToDictionary(i => ((char)('A' + i)).ToString(), i => (ushort)(0x41 + i)))
        .Concat(Enumerable.Range(1, 12).ToDictionary(i => $"F{i}", i => (ushort)(0x70 + i - 1)))
        .Concat(new Dictionary<string, ushort> { ["Space"] = 0x20, ["Enter"] = 0x0D, ["Escape"] = 0x1B, ["Backspace"] = 0x08, ["Tab"] = 0x09, ["Insert"] = 0x2D, ["Delete"] = 0x2E, ["PageUp"] = 0x21, ["PageDown"] = 0x22, ["Home"] = 0x24, ["End"] = 0x23, ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28, ["Shift"] = 0x10, ["Ctrl"] = 0x11, ["Alt"] = 0x12, [";"] = 0xBA, ["="] = 0xBB, [","] = 0xBC, ["-"] = 0xBD, ["."] = 0xBE, ["/"] = 0xBF, ["`"] = 0xC0, ["["] = 0xDB, ["\\"] = 0xDC, ["]"] = 0xDD, ["'"] = 0xDE })
        .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);

    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint uCode, uint uMapType);
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public INPUTUNION U; }
    // INPUT's union is sized by MOUSEINPUT on x64 (32 bytes), even when sending keyboard events.
    [StructLayout(LayoutKind.Explicit, Size = 32)] private struct INPUTUNION { [FieldOffset(0)] public KEYBDINPUT ki; }
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
