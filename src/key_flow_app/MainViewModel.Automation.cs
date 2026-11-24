using System.Windows.Input;
using key_flow_core;
using key_flow_windows;

namespace key_flow_app;

// Presentation orchestration only; sampling and matching live in Windows/Core.
public sealed partial class MainViewModel
{
    private readonly ScreenColorReader _screenReader = new();
    private readonly CancellationTokenSource _automationCancellation = new();
    private string _colorStatus = "尚未检测";
    public string ColorStatus { get => _colorStatus; private set { _colorStatus = value; OnPropertyChanged(); } }
    public ICommand CaptureAlternateStartCommand { get; private set; } = null!;
    public ICommand ClearAlternateStartCommand { get; private set; } = null!;
    public ICommand PickColorCommand { get; private set; } = null!;
    public ICommand TestColorCommand { get; private set; } = null!;

    private void SetupAutomationCommands()
    {
        SetupScreenCaptureCommands();
        CaptureAlternateStartCommand = new RelayCommand(_ => BeginCapture(CaptureMode.AlternateStart), _ => CanEdit);
        ClearAlternateStartCommand = new RelayCommand(_ => { SelectedProfile.AlternateStartHotkey = ""; ConfigureHotkeys(); }, _ => CanEdit);
        PickColorCommand = new RelayCommand(async _ => await PickColorAsync(), _ => CanEdit && _captureMode == CaptureMode.None);
        TestColorCommand = new RelayCommand(_ => TestColor(), _ => CanEdit && _validationErrors == 0);
    }

    private async Task PickColorAsync()
    {
        var profile = SelectedProfile;
        CancelCapture();
        _profileBusy = true;
        _systemHotkeys?.Clear();
        OnPropertyChanged(nameof(CanEdit));
        CommandManager.InvalidateRequerySuggested();
        try
        {
            for (var seconds = 3; seconds > 0; seconds--)
            {
                ColorStatus = $"请把鼠标移到目标位置，{seconds} 秒后取色";
                LastMessage = ColorStatus;
                await Task.Delay(1000, _automationCancellation.Token);
            }
            var point = _screenReader.CursorPosition();
            var color = _screenReader.Read(point.X, point.Y);
            profile.ColorCondition.X = point.X;
            profile.ColorCondition.Y = point.Y;
            profile.ColorCondition.Width = profile.ColorCondition.Height = 1;
            profile.ColorCondition.UseInactiveTarget = false;
            profile.ColorCondition.Target = color.ToString();
            ColorStatus = $"已取色 {color}，位置 ({point.X}, {point.Y})；请保存配置";
            LastMessage = ColorStatus;
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { ReportColorError(e); }
        finally
        {
            _profileBusy = false;
            if (!_disposed) { ConfigureHotkeys(); OnPropertyChanged(nameof(CanEdit)); CommandManager.InvalidateRequerySuggested(); }
        }
    }

    private void TestColor()
    {
        try
        {
            AutomationValidation.Validate(SelectedProfile);
            var condition = SelectedProfile.ColorCondition;
            var actual = ColorMatcher.Sample(_screenReader, condition);
            ColorStatus = $"当前 {actual} · {(ColorMatcher.Classify(actual, condition) switch { true => "已激活", false => "未激活", null => "未知状态（两色均未明确匹配）" })}";
            _logger.Info($"颜色检测 x={condition.X}, y={condition.Y}, target={condition.Target}, tolerance={condition.Tolerance}: {ColorStatus}");
        }
        catch (Exception e) { ReportColorError(e); }
    }

    private void ReportColorError(Exception e)
    {
        _logger.Error("屏幕取色失败", e);
        ColorStatus = $"取色失败：{e.Message}";
        LastMessage = ColorStatus;
    }
}
