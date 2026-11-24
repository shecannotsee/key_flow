using key_flow_core;

namespace key_flow_windows;

public static class ProfileOperations
{
    public static KeyProfile Copy(KeyProfile profile, bool suffix = true) => new()
    {
        Name = profile.Name + (suffix ? " 副本" : ""), Enabled = profile.Enabled,
        IsInActivationPool = profile.IsInActivationPool,
        StartHotkey = profile.StartHotkey, StopHotkey = profile.StopHotkey, ToggleHotkey = profile.ToggleHotkey,
        ColorTriggerEnabled = profile.ColorTriggerEnabled, TriggerDebounceMs = profile.TriggerDebounceMs,
        DurationSeconds = profile.DurationSeconds, DurationMilliseconds = profile.DurationMilliseconds,
        AlternateStartHotkey = profile.AlternateStartHotkey,
        ColorCondition = profile.ColorCondition.Copy(),
        Steps = profile.Steps.Select(s => new KeyStep(s.Key, s.WaitAfterMs) { HoldMs = s.HoldMs }).ToList(),
        SequenceMode = profile.SequenceMode
    };

    public static void ValidateForRepeat(IEnumerable<KeyProfile> profiles)
    {
        foreach (var p in profiles)
        {
            if (string.IsNullOrWhiteSpace(p.Name) || p.Steps is null || p.Steps.Count == 0)
                throw new InvalidDataException("配置名称或按键序列为空");
            if ((!p.SequenceMode && p.Steps.Count != 1) || (p.SequenceMode && p.Steps.Count < 2))
                throw new InvalidDataException("单键重复需要一个按键，顺序执行至少需要两个按键");
            AutomationValidation.Validate(p);
            KeyChord start, stop;
            try
            {
                start = KeyChord.Parse(p.StartHotkey);
                stop = p.ToggleHotkey ? start : KeyChord.Parse(p.StopHotkey);
                if (start.MainKey == "F12" || stop.MainKey == "F12" || (!p.ToggleHotkey && start == stop))
                    throw new ArgumentException("开始/停止键无效或重复");
                var emergency = KeyChord.Parse("Ctrl+Shift+F10");
                if (start == emergency || (!p.ToggleHotkey && stop == emergency)) throw new ArgumentException("请避开紧急停止快捷键");
                KeyChord? alternate = string.IsNullOrWhiteSpace(p.AlternateStartHotkey) ? null : KeyChord.Parse(p.AlternateStartHotkey);
                if (alternate is not null && (alternate.MainKey == "F12" || alternate == start || alternate == stop || alternate == emergency))
                    throw new ArgumentException("备用开始键与其他快捷键冲突");
                foreach (var s in p.Steps)
                {
                    var chord = KeyChord.Parse(s.Key);
                    if (s.WaitAfterMs is < 1 or > 3_600_000 || chord.CanTrigger(start) ||
                        (!p.ToggleHotkey && chord.CanTrigger(stop)) || (alternate is not null && chord.CanTrigger(alternate)) || chord.CanTrigger(emergency))
                        throw new ArgumentException("按键或间隔无效，或序列可能触发启停快捷键");
                }
            }
            catch (ArgumentException e) { throw new InvalidDataException(e.Message, e); }
        }
    }
}
