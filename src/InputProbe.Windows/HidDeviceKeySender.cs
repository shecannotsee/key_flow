using System.ComponentModel;
using System.Runtime.InteropServices;
using InputProbe.Core;
using Microsoft.Win32.SafeHandles;

namespace InputProbe.Windows;

public interface IHidReportTransport : IDisposable
{
    void Send(byte[] report);
}

public sealed class HidDeviceTransport : IHidReportTransport
{
    private readonly SafeFileHandle handle;
    // CTL_CODE(FILE_DEVICE_UNKNOWN, 0x800, METHOD_BUFFERED, FILE_WRITE_ACCESS).
    private const uint SubmitKeyboard = 0x0022A000;
    public HidDeviceTransport()
    {
        handle = CreateFile(@"\\.\InputProbeHid", 0x40000000, 0, 0, 3, 0, 0);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, "无法打开 InputProbeHid 设备：请检查驱动是否已加载及管理员权限");
        }
    }
    public void Send(byte[] report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.Length != 8) throw new ArgumentException("HID 键盘报告必须为 8 字节", nameof(report));
        if (!DeviceIoControl(handle, SubmitKeyboard, report, 8, 0, 0, out _, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "HID 键盘报告提交失败");
    }
    public void Dispose() => handle.Dispose();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input, uint inputLength, nint output, uint outputLength, out uint returned, nint overlapped);
}

// Uses HID usage IDs, not Windows virtual keys or scan codes. No SendInput fallback.
public sealed class HidDeviceKeySender(IHidReportTransport transport) : IKeySender, IDisposable
{
    private readonly object gate = new();
    private readonly HashSet<byte> pressed = [];
    private bool closed;

    public void KeyDown(string key) => Change(key, true);
    public void KeyUp(string key) => Change(key, false);
    private void Change(string key, bool down)
    {
        lock (gate)
        {
            if (closed)
            {
                if (!down) return; // A cancelled runner can finish after Shutdown.
                throw new ObjectDisposedException(nameof(HidDeviceKeySender));
            }
            var usage = Resolve(key);
            var next = new HashSet<byte>(pressed);
            if (down) next.Add(usage); else next.Remove(usage);
            var report = Encode(next); // Reject overflow before sending anything.
            transport.Send(report);
            pressed.Clear();
            pressed.UnionWith(next);
        }
    }
    public void ReleaseAll()
    {
        lock (gate)
        {
            if (closed) return;
            transport.Send(new byte[8]);
            pressed.Clear();
        }
    }
    public void Shutdown()
    {
        lock (gate)
        {
            if (closed) return;
            try { ReleaseAll(); }
            finally { closed = true; transport.Dispose(); }
        }
    }
    public void Dispose() => Shutdown();
    private static byte[] Encode(HashSet<byte> usages)
    {
        var keys = usages.Where(x => x < 0xE0).OrderBy(x => x).ToArray();
        if (keys.Length > 6) throw new InvalidOperationException("当前键盘报告最多同时保持 6 个普通键");
        var report = new byte[8];
        foreach (var modifier in usages.Where(x => x >= 0xE0)) report[0] |= (byte)(1 << (modifier - 0xE0));
        keys.CopyTo(report, 2);
        return report;
    }
    private static byte Resolve(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || !Usages.TryGetValue(key, out var usage))
            throw new ArgumentException($"不支持的 HID 按键：{key}", nameof(key));
        return usage;
    }
    private static readonly Dictionary<string, byte> Usages = BuildUsages();
    private static Dictionary<string, byte> BuildUsages()
    {
        var result = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
        {
            ["D0"] = 0x27, ["Enter"] = 0x28, ["Escape"] = 0x29, ["Backspace"] = 0x2A,
            ["Tab"] = 0x2B, ["Space"] = 0x2C, ["-"] = 0x2D, ["="] = 0x2E,
            ["["] = 0x2F, ["]"] = 0x30, ["\\"] = 0x31, [";"] = 0x33, ["'"] = 0x34,
            ["`"] = 0x35, [","] = 0x36, ["."] = 0x37, ["/"] = 0x38,
            ["Insert"] = 0x49, ["Home"] = 0x4A, ["PageUp"] = 0x4B, ["Delete"] = 0x4C,
            ["End"] = 0x4D, ["PageDown"] = 0x4E, ["Right"] = 0x4F, ["Left"] = 0x50,
            ["Down"] = 0x51, ["Up"] = 0x52, ["Ctrl"] = 0xE0, ["Shift"] = 0xE1, ["Alt"] = 0xE2
        };
        for (var i = 0; i < 26; i++) result[((char)('A' + i)).ToString()] = (byte)(4 + i);
        for (var i = 1; i <= 9; i++) result[$"D{i}"] = (byte)(0x1D + i);
        for (var i = 1; i <= 12; i++) result[$"F{i}"] = (byte)(0x39 + i);
        return result;
    }
}
