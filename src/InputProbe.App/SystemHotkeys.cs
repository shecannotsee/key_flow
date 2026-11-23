using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace InputProbe.App;

// Uses the existing WPF message loop, not a keyboard interception hook.
public sealed class SystemHotkeys : IDisposable
{
    private readonly HwndSource source;
    private readonly HashSet<int> ids = [];
    private bool disposed;
    public event Action<int>? Pressed;
    public SystemHotkeys(nint window)
    {
        source = HwndSource.FromHwnd(window) ?? throw new InvalidOperationException("绐楀彛灏氭湭鍒涘缓");
        source.AddHook(OnMessage);
    }
    public int Register(int id, uint key, uint modifiers = 0)
    {
        if (!RegisterHotKey(source.Handle, id, modifiers | 0x4000, key)) return Marshal.GetLastWin32Error();
        ids.Add(id);
        return 0;
    }
    public void Clear()
    {
        foreach (var id in ids) UnregisterHotKey(source.Handle, id);
        ids.Clear();
    }
    private nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x312 && ids.Contains((int)wParam))
        { handled = true; Pressed?.Invoke((int)wParam); }
        return 0;
    }
    public void Dispose() { if (disposed) return; disposed = true; Clear(); source.RemoveHook(OnMessage); }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint window, int id);
}
