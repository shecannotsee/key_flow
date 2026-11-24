using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Globalization;

namespace key_flow_core;

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
    private bool _enabled, _useInactiveTarget;
    private int _width = 1, _height = 1;
    private string _inactiveTarget = "#000000";
    public int Width { get => _width; set { _width = value; Changed(); } }
    public int Height { get => _height; set { _height = value; Changed(); } }
    public bool UseInactiveTarget { get => _useInactiveTarget; set { _useInactiveTarget = value; Changed(); } }
    public string InactiveTarget { get => _inactiveTarget; set { _inactiveTarget = value; Changed(); } }
    private int _x, _y, _tolerance = 15;
    private string _target = "#00FF00";
    public bool Enabled { get => _enabled; set { _enabled = value; Changed(); } }
    public int X { get => _x; set { _x = value; Changed(); } }
    public int Y { get => _y; set { _y = value; Changed(); } }
    public string Target { get => _target; set { _target = value; Changed(); } }
    public int Tolerance { get => _tolerance; set { _tolerance = value; Changed(); } }
    public ColorCondition Copy() => new() { Enabled = Enabled, X = X, Y = Y, Target = Target, Tolerance = Tolerance, Width = Width, Height = Height, UseInactiveTarget = UseInactiveTarget, InactiveTarget = InactiveTarget };
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public static class ColorMatcher
{
    // Nine representative pixels, not a full-image average. Same sampler for capture and evaluation.
    public static RgbColor Sample(IScreenColorReader screen, ColorCondition c)
    {
        if (c.Width is < 1 or > 16384 || c.Height is < 1 or > 16384) throw new InvalidDataException("区域尺寸应为 1 至 16384 像素");
        if (c.Width == 1 && c.Height == 1) return screen.Read(c.X, c.Y);
        int r = 0, g = 0, b = 0;
        for (int row = 0; row < 3; row++)
            for (int col = 0; col < 3; col++)
            {
                var pixel = screen.Read(checked(c.X + (c.Width - 1) * col / 2), checked(c.Y + (c.Height - 1) * row / 2));
                r += pixel.R; g += pixel.G; b += pixel.B;
            }
        return new((byte)(r / 9), (byte)(g / 9), (byte)(b / 9));
    }

    public static bool? Classify(RgbColor actual, ColorCondition c)
    {
        var active = Matches(actual, c);
        if (!c.UseInactiveTarget) return active;
        var inactive = c.Copy(); inactive.Target = c.InactiveTarget;
        var isInactive = Matches(actual, inactive);
        return active == isInactive ? null : active;
    }

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
        if (profile.TriggerDebounceMs is < 50 or > 10000)
            throw new InvalidDataException("激活确认时间应为 50 至 10000 毫秒");
        if (profile.ColorTriggerEnabled && profile.DurationSeconds <= 0)
            throw new InvalidDataException("颜色触发流程需要设置大于 0 秒的持续时间");
        var c = profile.ColorCondition ?? throw new InvalidDataException("颜色条件不能为空");
        var active = RgbColor.Parse(c.Target);
        var inactive = RgbColor.Parse(c.InactiveTarget);
        if (c.Width is < 1 or > 16384 || c.Height is < 1 or > 16384 || (long)c.X + c.Width - 1 > int.MaxValue || (long)c.Y + c.Height - 1 > int.MaxValue)
            throw new InvalidDataException("取色区域尺寸或坐标无效");
        if (c.UseInactiveTarget && Math.Abs(active.R - inactive.R) <= 2 * c.Tolerance && Math.Abs(active.G - inactive.G) <= 2 * c.Tolerance && Math.Abs(active.B - inactive.B) <= 2 * c.Tolerance)
            throw new InvalidDataException("激活色与未激活色过于接近，请重新采集或减小容差");
        if (c.Tolerance is < 0 or > 255) throw new InvalidDataException("颜色容差应为 0 至 255");
    }
}
