using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Globalization;

namespace KeyFlow.Core;

public readonly record struct RgbColor(byte R, byte G, byte B)
{
    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
    public static RgbColor Parse(string text)
    {
        if (text is null || text.Length != 7 || text[0] != '#' ||
            !uint.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            throw new InvalidDataException("目标颜色必须是 #RRGGBB，例如 #00FF00");
        return new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }
}

public interface IScreenColorReader { RgbColor Read(int x, int y); }

public sealed class ColorCondition : INotifyPropertyChanged
{
    private bool _enabled;
    private int _x, _y, _tolerance = 15;
    private string _target = "#00FF00";
    public bool Enabled { get => _enabled; set { _enabled = value; Changed(); } }
    public int X { get => _x; set { _x = value; Changed(); } }
    public int Y { get => _y; set { _y = value; Changed(); } }
    public string Target { get => _target; set { _target = value; Changed(); } }
    public int Tolerance { get => _tolerance; set { _tolerance = value; Changed(); } }
    public ColorCondition Copy() => new() { Enabled = Enabled, X = X, Y = Y, Target = Target, Tolerance = Tolerance };
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public static class ColorMatcher
{
    public static bool Matches(RgbColor actual, ColorCondition condition)
    {
        var expected = RgbColor.Parse(condition.Target);
        return Math.Abs(actual.R - expected.R) <= condition.Tolerance &&
            Math.Abs(actual.G - expected.G) <= condition.Tolerance &&
            Math.Abs(actual.B - expected.B) <= condition.Tolerance;
    }
}

public static class AutomationValidation
{
    public static void Validate(KeyProfile profile)
    {
        if (!double.IsFinite(profile.DurationSeconds) || profile.DurationSeconds is < 0 or > 86400)
            throw new InvalidDataException("持续时间应为 0 至 86400 秒，0 表示持续运行");
        var c = profile.ColorCondition ?? throw new InvalidDataException("颜色条件不能为空");
        RgbColor.Parse(c.Target);
        if (c.Tolerance is < 0 or > 255) throw new InvalidDataException("颜色容差应为 0 至 255");
    }
}
