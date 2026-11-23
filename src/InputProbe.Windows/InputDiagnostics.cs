using System.Diagnostics;
using System.Runtime.InteropServices;

namespace InputProbe.Windows;

public static class InputDiagnostics
{
    public static string DescribeModifiers() => $"LWin={IsDown(0x5B)},RWin={IsDown(0x5C)},Ctrl={IsDown(0x11)},Alt={IsDown(0x12)},Shift={IsDown(0x10)}";
    private static bool IsDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    public static nint ForegroundHandle => GetForegroundWindow();
    public static string DescribeForeground()
    {
        var hwnd = ForegroundHandle;
        GetWindowThreadProcessId(hwnd, out var pid);
        using var current = Process.GetCurrentProcess();
        var own = Integrity(current.Id);
        string name;
        try { using var target = Process.GetProcessById((int)pid); name = target.ProcessName; }
        catch { name = "unknown"; }
        return $"foreground=0x{hwnd:X}, pid={pid}, process={name}, toolIntegrity={own}, targetIntegrity={Integrity((int)pid)}";
    }
    // TokenIntegrityLevel RID: 4096=low, 8192=medium, 12288=high, 16384=system.
    private static string Integrity(int pid)
    {
        var process = OpenProcess(0x1000, false, pid);
        if (process == 0) return $"unknown(error={Marshal.GetLastWin32Error()})";
        nint token = 0, buffer = 0;
        try
        {
            if (!OpenProcessToken(process, 8, out token)) return $"unknown(error={Marshal.GetLastWin32Error()})";
            GetTokenInformation(token, 25, 0, 0, out var length);
            if (length == 0) return "unknown";
            buffer = Marshal.AllocHGlobal((int)length);
            if (!GetTokenInformation(token, 25, buffer, length, out _)) return $"unknown(error={Marshal.GetLastWin32Error()})";
            var sid = Marshal.ReadIntPtr(buffer);
            var count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
            return Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1))).ToString();
        }
        finally
        {
            if (buffer != 0) Marshal.FreeHGlobal(buffer);
            if (token != 0) CloseHandle(token);
            CloseHandle(process);
        }
    }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(nint process, uint access, out nint token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(nint token, int type, nint buffer, uint length, out uint needed);
    [DllImport("advapi32.dll")] private static extern nint GetSidSubAuthorityCount(nint sid);
    [DllImport("advapi32.dll")] private static extern nint GetSidSubAuthority(nint sid, uint index);
}
