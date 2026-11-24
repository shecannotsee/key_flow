using System.ComponentModel;
using System.Runtime.InteropServices;
using KeyFlow.Core;

namespace KeyFlow.Windows;

// Coordinates are physical pixels in the visible virtual desktop, not WPF DIPs.
public sealed class ScreenColorReader : IScreenColorReader
{
    public (int X, int Y) CursorPosition()
    {
        if (!GetPhysicalCursorPos(out var point)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return (point.X, point.Y);
    }

    public RgbColor Read(int x, int y)
    {
        var previous = SetThreadDpiAwarenessContext(new nint(-4));
        try
        {
            var dc = GetDC(0);
            if (dc == 0) throw new InvalidOperationException("无法读取当前桌面");
            try
            {
                var color = GetPixel(dc, x, y);
                if (color == uint.MaxValue) throw new InvalidOperationException("此位置无法取色，请确认坐标在可见屏幕内");
                return new((byte)color, (byte)(color >> 8), (byte)(color >> 16));
            }
            finally { ReleaseDC(0, dc); }
        }
        finally { if (previous != 0) SetThreadDpiAwarenessContext(previous); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetPhysicalCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(nint dc, int x, int y);
}
