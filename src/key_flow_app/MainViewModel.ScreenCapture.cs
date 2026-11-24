using System.Windows;
using System.Windows.Input;
using key_flow_core;
using key_flow_windows;

namespace key_flow_app;

public sealed partial class MainViewModel
{
    public ICommand SelectColorRegionCommand { get; private set; } = null!;
    public ICommand CaptureActiveColorCommand { get; private set; } = null!;
    public ICommand CaptureInactiveColorCommand { get; private set; } = null!;

    private void SetupScreenCaptureCommands()
    {
        SelectColorRegionCommand = new RelayCommand(async _ => await CaptureScreenAsync(null), _ => CanEdit && _captureMode == CaptureMode.None);
        CaptureActiveColorCommand = new RelayCommand(async _ => await CaptureScreenAsync(true), _ => CanEdit && _validationErrors == 0 && _captureMode == CaptureMode.None);
        CaptureInactiveColorCommand = new RelayCommand(async _ => await CaptureScreenAsync(false), _ => CanEdit && _validationErrors == 0 && _captureMode == CaptureMode.None);
    }

    private async Task CaptureScreenAsync(bool? active)
    {
        var condition = SelectedProfile.ColorCondition;
        var window = (Window?)_editorWindow ?? Application.Current.MainWindow;
        var previousState = window.WindowState;
        CancelCapture();
        _profileBusy = true;
        _systemHotkeys?.Clear();
        OnPropertyChanged(nameof(CanEdit)); CommandManager.InvalidateRequerySuggested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_automationCancellation.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            ColorStatus = active is null ? "在屏幕目标区域按住左键拖动并松开；Esc 取消" : "等待目标状态出现后点击左键；采集固定区域，Esc 取消";
            LastMessage = ColorStatus;
            window.WindowState = WindowState.Minimized;
            var capture = await ScreenClickCapture.CaptureAsync(active is null ? null : condition, timeout.Token);
            if (_disposed) return;
            if (active is null)
            {
                if (capture.Width > 16384 || capture.Height > 16384) throw new InvalidOperationException("区域过大，请重新选择");
                condition.X = capture.X; condition.Y = capture.Y;
                condition.Width = capture.Width; condition.Height = capture.Height;
                condition.UseInactiveTarget = false;
                ColorStatus = $"已选择 ({capture.X}, {capture.Y})，{capture.Width} × {capture.Height}；请重新采集两种状态";
            }
            else
            {
                var color = capture.Color!.Value.ToString();
                if (active.Value) condition.Target = color;
                else { condition.InactiveTarget = color; condition.UseInactiveTarget = true; }
                ColorStatus = $"已采集{(active.Value ? "激活" : "未激活")}色 {color}；请检测并保存配置";
            }
            LastMessage = ColorStatus;
            _logger.Info(ColorStatus);
        }
        catch (OperationCanceledException) { if (!_disposed) ColorStatus = LastMessage = "取色已取消或超时，原采样数据未改变"; }
        catch (Exception e) { if (!_disposed) ReportColorError(e); }
        finally
        {
            _profileBusy = false;
            if (!_disposed)
            {
                window.WindowState = previousState; window.Activate();
                ConfigureHotkeys(); OnPropertyChanged(nameof(CanEdit)); CommandManager.InvalidateRequerySuggested();
            }
        }
    }
}
