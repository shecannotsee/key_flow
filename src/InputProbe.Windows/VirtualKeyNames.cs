namespace InputProbe.Windows;

public static class VirtualKeyNames
{
    public static string FromVirtualKey(int value) => value switch
    {
        >= 0x30 and <= 0x39 => $"D{value - 0x30}",
        >= 0x41 and <= 0x5A => ((char)value).ToString(),
        >= 0x70 and <= 0x7B => $"F{value - 0x6F}",
        0x20 => "Space",
        0x0D => "Enter",
        0x1B => "Escape",
        0x08 => "Backspace",
        0x09 => "Tab",
        0x2D => "Insert",
        0x2E => "Delete",
        0x21 => "PageUp",
        0x22 => "PageDown",
        0x24 => "Home",
        0x23 => "End",
        0x25 => "Left",
        0x26 => "Up",
        0x27 => "Right",
        0x28 => "Down",
        0x10 => "Shift",
        0x11 => "Ctrl",
        0x12 => "Alt",
        0xBA => ";",
        0xBB => "=",
        0xBC => ",",
        0xBD => "-",
        0xBE => ".",
        0xBF => "/",
        0xC0 => "`",
        0xDB => "[",
        0xDC => "\\",
        0xDD => "]",
        0xDE => "'",
        _ => string.Empty
    };
}
