namespace key_flow_windows;

public sealed record KeyChord(string MainKey, uint Modifiers)
{
    private static readonly Dictionary<string, int> KeysByName = Enumerable.Range(1, 254)
        .Select(v => (Name: VirtualKeyNames.FromVirtualKey(v), Vk: v)).Where(p => p.Name.Length > 0)
        .ToDictionary(p => p.Name, p => p.Vk, StringComparer.OrdinalIgnoreCase);
    public int VirtualKey => KeysByName[MainKey];
    public string[] Keys => Prefixes(Modifiers).Append(MainKey).ToArray();
    public string Text => string.Join("+", Keys.Select(k => k.Length == 2 && k[0] == 'D' && char.IsDigit(k[1]) ? k[1..] : k));
    private static IEnumerable<string> Prefixes(uint modifiers)
    {
        if ((modifiers & 2) != 0) yield return "Ctrl";
        if ((modifiers & 1) != 0) yield return "Alt";
        if ((modifiers & 4) != 0) yield return "Shift";
    }
    public static uint Modifier(string key) => key switch { "Ctrl" => 2, "Alt" => 1, "Shift" => 4, _ => 0 };
    public static KeyChord Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("按键不能为空");
        var tokens = text.Split('+').Select(t => t.Trim()).ToArray();
        var names = tokens.Select(t => t.Length == 1 && char.IsAsciiDigit(t[0]) ? "D" + t : t)
            .Select(t => KeysByName.Keys.FirstOrDefault(k => k.Equals(t, StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException($"不支持的按键：{t}")).ToArray();
        uint modifiers = 0;
        foreach (var name in names.SkipLast(1))
        {
            var bit = Modifier(name);
            if (bit == 0 || (modifiers & bit) != 0) throw new ArgumentException("组合键应为 Ctrl、Alt、Shift 加一个主键");
            modifiers |= bit;
        }
        if (names.Length > 1 && Modifier(names[^1]) != 0) throw new ArgumentException("组合键需要一个非修饰主键");
        return new(names[^1], modifiers);
    }
    public bool CanTrigger(KeyChord shortcut)
    {
        if (Modifier(shortcut.MainKey) != 0) return Keys.Contains(shortcut.MainKey);
        return MainKey == shortcut.MainKey && (Modifiers & shortcut.Modifiers) == shortcut.Modifiers;
    }
}

// Tracks capture through release so the recorded chord cannot trigger execution.
public sealed class ChordCapture
{
    private readonly HashSet<int> held = [];
    private string? pendingModifier;
    private bool completed;
    public bool IsReleased => held.Count == 0;
    public void Reset() { held.Clear(); pendingModifier = null; completed = false; }
    private static int Normalize(int vk) => vk switch { 0xA0 or 0xA1 => 0x10, 0xA2 or 0xA3 => 0x11, 0xA4 or 0xA5 => 0x12, _ => vk };
    public string? Down(int vk, uint modifiers)
    {
        held.Add(vk);
        if (completed) return null;
        var name = VirtualKeyNames.FromVirtualKey(Normalize(vk));
        if (name.Length == 0 || (modifiers & 8) != 0) return null;
        if (KeyChord.Modifier(name) != 0) { pendingModifier = name; return null; }
        completed = true;
        return new KeyChord(name, modifiers & 7).Text;
    }
    public string? Up(int vk)
    {
        held.Remove(vk);
        if (completed || !IsReleased || pendingModifier is null) return null;
        completed = true;
        return pendingModifier;
    }
}
