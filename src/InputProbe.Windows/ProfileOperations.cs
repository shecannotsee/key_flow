using InputProbe.Core;

namespace InputProbe.Windows;

public static class ProfileOperations
{
    public static KeyProfile Copy(KeyProfile profile, bool suffix = true) => new()
    {
        Name = profile.Name + (suffix ? " 副本" : ""), Enabled = profile.Enabled,
        StartHotkey = profile.StartHotkey, StopHotkey = profile.StopHotkey, ToggleHotkey = profile.ToggleHotkey,
        Steps = profile.Steps.Select(s => new KeyStep(s.Key, s.WaitAfterMs) { HoldMs = s.HoldMs }).ToList()
    };

    public static void ValidateForRepeat(IEnumerable<KeyProfile> profiles)
    {
        var names = Enumerable.Range(1, 254).Select(VirtualKeyNames.FromVirtualKey).Where(n => n.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var p in profiles)
        {
            if (string.IsNullOrWhiteSpace(p.Name) || p.Steps is null || p.Steps.Count == 0)
                throw new InvalidDataException("配置名称或按键序列为空");
            if (!names.Contains(p.StartHotkey) || p.StartHotkey == "F12" ||
                (!p.ToggleHotkey && (!names.Contains(p.StopHotkey) ||
                p.StartHotkey.Equals(p.StopHotkey, StringComparison.OrdinalIgnoreCase) || p.StopHotkey == "F12")))
                throw new InvalidDataException("开始/停止键无效、重复或使用了 F12");
            foreach (var s in p.Steps)
                if (!names.Contains(s.Key) || s.WaitAfterMs is < 1 or > 3_600_000 ||
                    s.Key.Equals(p.StartHotkey, StringComparison.OrdinalIgnoreCase) || (!p.ToggleHotkey && s.Key.Equals(p.StopHotkey, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("序列按键、间隔无效或与快捷键冲突");
        }
    }
}
