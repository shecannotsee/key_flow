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
    private SendInputMode _mode = mode;
    public SendInputMode Mode
    {
        get => _mode;
        set
        {
            EnsureReady();
            if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
            if (_pressed.Count != 0) throw new InvalidOperationException("按键尚未释放，不能切换输入模式");
            _mode = value;
        }
    }
    private readonly HashSet<string> _pressed = new(StringComparer.OrdinalIgnoreCase);
    private bool _shutdown;
    private nint _lastForeground = -1;
    public string LastStatus { get; private set; } = "尚未发送";
    public void KeyDown(string key) => Send(key, false);
    public void KeyUp(string key)
    {
        // Stop/Shutdown may already have released the key during the hold delay.
        if (_shutdown && !_pressed.Contains(key)) return;
        Send(key, true);
    }
    public void Press(string key)
    {
        EnsureReady();
        var vk = ResolveKey(key);
        var inputs = new[] { CreateInput(vk, false), CreateInput(vk, true) };
        SendInputs(inputs, key, "Click", sent => { if (sent > 0) _pressed.Add(key); });
        _pressed.Remove(key);
        LastStatus = $"已发送 {key} Click ({_mode})";
        _logger.Info(LastStatus);
    }
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
        EnsureReady();
        var vk = ResolveKey(key);
        SendInputs([CreateInput(vk, up)], key, up ? "Up" : "Down");
        if (up) _pressed.Remove(key); else _pressed.Add(key);
        LastStatus = $"已发送 {key} {(up ? "Up" : "Down")} ({_mode})";
        _logger.Info(LastStatus);
    }

    private void EnsureReady()
    {
        if (!_shutdown) return;
        var exception = new InvalidOperationException("输入后端已关闭");
        _logger.Error("输入发送被拒绝", exception);
        throw exception;
    }

    private ushort ResolveKey(string key)
    {
        if (KeyMap.TryGetValue(key, out var vk)) return vk;
        LastStatus = $"未映射按键: {key}";
        var exception = new ArgumentException(LastStatus, nameof(key));
        _logger.Error(LastStatus, exception);
        throw exception;
    }

    private INPUT CreateInput(ushort vk, bool up)
    {
        var scanCode = MapVirtualKey(vk, 4);
        if (_mode == SendInputMode.ScanCode && scanCode == 0)
        {
            var exception = new InvalidOperationException($"无法映射扫描码 (VK 0x{vk:X2})");
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
        return new INPUT { type = 1, U = new INPUTUNION { ki = new KEYBDINPUT
        {
            wVk = _mode == SendInputMode.VirtualKey ? vk : (ushort)0,
            wScan = inputScan,
            dwFlags = flags
        } } };
    }

    private void SendInputs(INPUT[] inputs, string key, string action, Action<uint>? onPartial = null)
    {
        var foreground = InputDiagnostics.ForegroundHandle;
        if (foreground != _lastForeground)
        {
            _lastForeground = foreground;
            _logger.Info(InputDiagnostics.DescribeForeground());
        }
        Marshal.SetLastPInvokeError(0);
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        var error = Marshal.GetLastWin32Error();
        if (sent == inputs.Length) return;
        onPartial?.Invoke(sent);
        LastStatus = $"SendInput 失败: requested={inputs.Length}, inserted={sent}, error={error}; 错误码不能判定是否 UIPI";
        var exception = new System.ComponentModel.Win32Exception(error, LastStatus);
        _logger.Error($"{LastStatus} ({key} {action})", exception);
        throw exception;
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
