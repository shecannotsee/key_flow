using KeyFlow.Core;

namespace KeyFlow.Windows;

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
        foreach (var p in profiles)
        {
            if (string.IsNullOrWhiteSpace(p.Name) || p.Steps is null || p.Steps.Count == 0)
                throw new InvalidDataException("配置名称或按键序列为空");
            KeyChord start, stop;
            try
            {
                start = KeyChord.Parse(p.StartHotkey);
                stop = p.ToggleHotkey ? start : KeyChord.Parse(p.StopHotkey);
                if (start.MainKey == "F12" || stop.MainKey == "F12" || (!p.ToggleHotkey && start == stop))
                    throw new ArgumentException("开始/停止键无效或重复");
                var emergency = KeyChord.Parse("Ctrl+Shift+F10");
                if (start == emergency || (!p.ToggleHotkey && stop == emergency)) throw new ArgumentException("请避开紧急停止快捷键");
                foreach (var s in p.Steps)
                {
                    var chord = KeyChord.Parse(s.Key);
                    if (s.WaitAfterMs is < 1 or > 3_600_000 || chord.CanTrigger(start) ||
                        (!p.ToggleHotkey && chord.CanTrigger(stop)) || chord.CanTrigger(emergency))
                        throw new ArgumentException("按键或间隔无效，或序列可能触发启停快捷键");
                }
            }
            catch (ArgumentException e) { throw new InvalidDataException(e.Message, e); }
        }
    }
}
