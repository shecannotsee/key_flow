using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using key_flow_windows;

namespace key_flow_app;

// Combines system hotkeys, raw input, polling and an independent low-level listener.
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
    private readonly GlobalKeyboardListener listener;
    private readonly HashSet<uint> hookHeld = [];
    private long hookEvents;
    public string ListenerStatus => listener.StartupError?.Message ?? (listener.IsReady ? "ready" : "unavailable");
    private readonly HashSet<uint> rawHeld = [];
    public int RawInputError => raw.RegistrationError;
    public string LastSource { get; private set; } = "WM_HOTKEY";
    private bool disposed;
    private readonly Func<nint> foreground;
    private nint lastForeground;
    private long nextHealth;
    private long rawEvents, pollTicks;
    public event Action<string>? Diagnostic;
    public event Action<int>? Pressed;
    public SystemHotkeys(nint window, Func<uint, bool>? keyState = null, Func<nint>? foreground = null)
    {
        this.keyState = keyState ?? Down;
        this.foreground = foreground ?? (() => InputDiagnostics.ForegroundHandle);
        lastForeground = this.foreground();
        source = HwndSource.FromHwnd(window) ?? throw new InvalidOperationException("绐楀彛灏氭湭鍒涘缓");
        source.AddHook(OnMessage);
        raw = new RawKeyboardInput(window);
        raw.KeyChanged += OnRawKey;
        listener = new GlobalKeyboardListener();
        listener.KeyChanged += (key, down) =>
        {
            if (disposed || source.Dispatcher.HasShutdownStarted) return;
            source.Dispatcher.BeginInvoke(new Action(() => { if (!disposed) OnHookKey(key, down); }));
        };
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
        rawHeld.Clear(); hookHeld.Clear();
    }
    private nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x312 && ids.Contains((int)wParam))
        { handled = true; Signal((int)wParam, "WM_HOTKEY"); }
        return 0;
    }
    private void Poll()
    {
        RefreshForeground();
        pollTicks++;
        if (Environment.TickCount64 >= nextHealth)
        {
            nextHealth = Environment.TickCount64 + 5000;
            Diagnostic?.Invoke($"接收心跳 hookEvents={hookEvents}, rawEvents={rawEvents}, pollTicks={pollTicks}, bindings={bindings.Count}; {InputDiagnostics.DescribeForeground()}");
        }
        foreach (var (id, binding) in bindings.ToArray())
        {
            var down = IsDown(binding.Key, binding.Modifiers);
            var wasDown = held.GetValueOrDefault(id);
            held[id] = down;
            if (down && !wasDown) Signal(id, "GetAsyncKeyState");
        }
    }
    private void OnHookKey(uint key, bool down)
    {
        RefreshForeground();
        hookEvents++;
        var changed = down ? hookHeld.Add(key) : hookHeld.Remove(key);
        if (!down || !changed) return;
        bool HookDown(uint k) => k switch
        {
            0x10 => hookHeld.Contains(0x10) || hookHeld.Contains(0xA0) || hookHeld.Contains(0xA1),
            0x11 => hookHeld.Contains(0x11) || hookHeld.Contains(0xA2) || hookHeld.Contains(0xA3),
            0x12 => hookHeld.Contains(0x12) || hookHeld.Contains(0xA4) || hookHeld.Contains(0xA5),
            _ => hookHeld.Contains(k)
        };
        foreach (var (id, binding) in bindings.ToArray())
        {
            if (binding.Key != key) continue;
            var matches = IsDown(binding.Key, binding.Modifiers, HookDown);
            Diagnostic?.Invoke($"KeyboardHook 快捷键 id={id}, matched={matches}");
            if (matches) Signal(id, "KeyboardHook");
        }
    }
    private void OnRawKey(uint key, bool down)
    {
        RefreshForeground();
        rawEvents++;
        var changed = down ? rawHeld.Add(key) : rawHeld.Remove(key);
        if (!down || !changed) return;
        foreach (var (id, binding) in bindings.ToArray())
        {
            if (binding.Key != key) continue;
            var matches = IsDown(binding.Key, binding.Modifiers, RawDown);
            Diagnostic?.Invoke($"RawInput 快捷键 id={id}, matched={matches}, modifiers={string.Join(",", rawHeld.Where(k => k is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5))}");
            if (matches) Signal(id, "RawInput");
        }
    }
    private void RefreshForeground()
    {
        var current = foreground();
        if (current == lastForeground) return;
        lastForeground = current;
        // Resynchronize modifiers after focus changes: their key-up may have been missed.
        rawHeld.Clear(); hookHeld.Clear();
        foreach (var key in new uint[] { 0x10, 0x11, 0x12, 0x5B, 0x5C })
            if (keyState(key)) { rawHeld.Add(key); hookHeld.Add(key); }
        Diagnostic?.Invoke($"前台切换，已同步修饰键；{InputDiagnostics.DescribeForeground()}");
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
    public void Dispose() { if (disposed) return; disposed = true; poll.Stop(); listener.Dispose(); raw.Dispose(); Clear(); source.RemoveHook(OnMessage); }
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint window, int id);
}
