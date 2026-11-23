using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace InputProbe.App;

// Uses the existing WPF message loop, not a keyboard interception hook.
public sealed class SystemHotkeys : IDisposable
{
    private readonly HwndSource source;
    private readonly HashSet<int> ids = [];
    private readonly Dictionary<int, (uint Key, uint Modifiers)> bindings = [];
    private readonly Dictionary<int, bool> held = [];
    private readonly Dictionary<int, long> lastSignal = [];
    private readonly DispatcherTimer poll;
    private readonly Func<uint, bool> keyState;
    private readonly RawKeyboardInput raw;
    private readonly HashSet<uint> rawHeld = [];
    public int RawInputError => raw.RegistrationError;
    public string LastSource { get; private set; } = "WM_HOTKEY";
    private bool disposed;
    public event Action<int>? Pressed;
    public SystemHotkeys(nint window, Func<uint, bool>? keyState = null)
    {
        this.keyState = keyState ?? Down;
        source = HwndSource.FromHwnd(window) ?? throw new InvalidOperationException("绐楀彛灏氭湭鍒涘缓");
        source.AddHook(OnMessage);
        raw = new RawKeyboardInput(window);
        raw.KeyChanged += OnRawKey;
        poll = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Input, (_, _) => Poll(), source.Dispatcher);
        poll.Start();
    }
    public int Register(int id, uint key, uint modifiers = 0)
    {
        if (!RegisterHotKey(source.Handle, id, modifiers | 0x4000, key)) return Marshal.GetLastWin32Error();
        ids.Add(id);
        bindings[id] = (key, modifiers);
        held[id] = IsDown(key, modifiers);
        return 0;
    }
    public void Clear()
    {
        foreach (var id in ids) UnregisterHotKey(source.Handle, id);
        ids.Clear();
        bindings.Clear(); held.Clear(); lastSignal.Clear();
        rawHeld.Clear();
    }
    private nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x312 && ids.Contains((int)wParam))
        { handled = true; Signal((int)wParam, "WM_HOTKEY"); }
        return 0;
    }
    private void Poll()
    {
        foreach (var (id, binding) in bindings.ToArray())
        {
            var down = IsDown(binding.Key, binding.Modifiers);
            var wasDown = held.GetValueOrDefault(id);
            held[id] = down;
            if (down && !wasDown) Signal(id, "GetAsyncKeyState");
        }
    }
    private void OnRawKey(uint key, bool down)
    {
        var changed = down ? rawHeld.Add(key) : rawHeld.Remove(key);
        if (!down || !changed) return;
        foreach (var (id, binding) in bindings.ToArray())
            if (binding.Key == key && IsDown(binding.Key, binding.Modifiers, RawDown)) Signal(id, "RawInput");
    }
    private bool RawDown(uint key) => key switch
    {
        0x10 => rawHeld.Contains(0x10) || rawHeld.Contains(0xA0) || rawHeld.Contains(0xA1),
        0x11 => rawHeld.Contains(0x11) || rawHeld.Contains(0xA2) || rawHeld.Contains(0xA3),
        0x12 => rawHeld.Contains(0x12) || rawHeld.Contains(0xA4) || rawHeld.Contains(0xA5),
        _ => rawHeld.Contains(key)
    };
    private bool IsDown(uint key, uint modifiers)
        => IsDown(key, modifiers, keyState);
    private static bool IsDown(uint key, uint modifiers, Func<uint, bool> keyState)
    {
        uint active = 0;
        if (keyState(0x12)) active |= 1;
        if (keyState(0x11)) active |= 2;
        if (keyState(0x10)) active |= 4;
        if (keyState(0x5B) || keyState(0x5C)) active |= 8;
        // A modifier can itself be the configured key.
        if (key == 0x12) active &= ~1u;
        if (key == 0x11) active &= ~2u;
        if (key == 0x10) active &= ~4u;
        return active == modifiers && keyState(key);
    }
    private static bool Down(uint key) => (GetAsyncKeyState((int)key) & 0x8000) != 0;
    private void Signal(int id, string origin)
    {
        var now = Environment.TickCount64;
        if (lastSignal.TryGetValue(id, out var previous) && now - previous < 150) return;
        lastSignal[id] = now;
        LastSource = origin;
        Pressed?.Invoke(id);
    }
    public void Dispose() { if (disposed) return; disposed = true; poll.Stop(); raw.Dispose(); Clear(); source.RemoveHook(OnMessage); }
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint window, int id);
}
