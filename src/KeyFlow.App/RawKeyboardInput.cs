using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace KeyFlow.App;

// Receives keyboard reports on this app's window even when it is not foreground.
// Does not suppress, redirect or synthesize keyboard input.
public sealed class RawKeyboardInput : IDisposable
{
    private readonly HwndSource source;
    public event Action<uint, bool>? KeyChanged;
    public int RegistrationError { get; }
    public RawKeyboardInput(nint window)
    {
        source = HwndSource.FromHwnd(window) ?? throw new InvalidOperationException("窗口尚未创建");
        var devices = new[] { new Device { Page = 1, Usage = 6, Flags = 0x100, Target = window } };
        if (!RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf<Device>()))
        { RegistrationError = Marshal.GetLastWin32Error(); return; }
        source.AddHook(OnMessage);
    }
    private nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != 0xFF) return 0;
        uint size = 0;
        var headerSize = (uint)Marshal.SizeOf<Header>();
        if (GetRawInputData(lParam, 0x10000003, 0, ref size, headerSize) == uint.MaxValue || size < headerSize + 16 || size > 4096) return 0;
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            var read = GetRawInputData(lParam, 0x10000003, buffer, ref size, headerSize);
            if (read == uint.MaxValue || read < headerSize + 16 || Marshal.ReadInt32(buffer) != 1) return 0;
            var keyboard = buffer + (int)headerSize;
            var flags = (ushort)Marshal.ReadInt16(keyboard, 2);
            var vk = (ushort)Marshal.ReadInt16(keyboard, 6);
            if (vk is 0 or 255) return 0;
            KeyChanged?.Invoke(vk, (flags & 1) == 0);
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return 0; // Allow DefWindowProc to perform foreground WM_INPUT cleanup.
    }
    public void Dispose()
    {
        if (RegistrationError != 0) return;
        source.RemoveHook(OnMessage);
        RegisterRawInputDevices([new Device { Page = 1, Usage = 6, Flags = 1, Target = 0 }], 1, (uint)Marshal.SizeOf<Device>());
    }
    [StructLayout(LayoutKind.Sequential)] private struct Device { public ushort Page, Usage; public uint Flags; public nint Target; }
    [StructLayout(LayoutKind.Sequential)] private struct Header { public uint Type, Size; public nint Device, WParam; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterRawInputDevices(Device[] devices, uint count, uint size);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetRawInputData(nint input, uint command, nint data, ref uint size, uint headerSize);
}
