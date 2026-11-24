using System.Windows;
using System.Windows.Input;
using key_flow_core;
using key_flow_windows;

namespace key_flow_app;

public sealed partial class MainViewModel
{
    private CancellationTokenSource? _monitorCancellation;
    public bool IsMonitoring => _monitorCancellation is not null;

    private void StopEverything(string message)
    {
        _monitorCancellation?.Cancel();
        _runtime.Stop(message);
    }

    private async Task StartMonitoringAsync()
    {
        if (!CanEdit) return;
        var profile = ProfileOperations.Copy(SelectedProfile, false);
        profile.Enabled = true;
        // Activation starts a complete timed workflow; it is not a per-key gate.
        profile.ColorCondition.Enabled = false;
        foreach (var step in profile.Steps) step.HoldMs = RepeatTiming.HoldFor(step.WaitAfterMs);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_automationCancellation.Token);
        _monitorCancellation = cancellation;
        OnPropertyChanged(nameof(IsMonitoring)); OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(WindowTitle));
        CommandManager.InvalidateRequerySuggested();
        StateText = "监听中";
        LastMessage = "等待色块先恢复未激活，再次激活时执行";
        _logger.Info($"颜色规则开启 name={profile.Name}, debounceMs={profile.TriggerDebounceMs}, duration={profile.DurationSeconds}s, point=({profile.ColorCondition.X},{profile.ColorCondition.Y}), target={profile.ColorCondition.Target}, tolerance={profile.ColorCondition.Tolerance}");
        string finalMessage = "监听和流程已停止";
        try
        {
            await Task.Run(() => new ColorTriggerMonitor(_screenReader).RunAsync(
                profile.ColorCondition, profile.TriggerDebounceMs,
                token => Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    token.ThrowIfCancellationRequested();
                    if (_disposed) throw new OperationCanceledException(token);
                    _logger.Info("颜色激活，开始执行流程");
                    _runtime.Arm();
                    await _runtime.TriggerAsync(profile);
                    token.ThrowIfCancellationRequested();
                    if (_runtime.LastRunFailed) throw new InvalidOperationException("流程执行或按键释放失败");
                    _logger.Info("颜色流程完成，等待重新激活");
                }).Task.Unwrap(), cancellation.Token,
                state => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_disposed || cancellation.IsCancellationRequested || _monitorCancellation != cancellation) return;
                    StateText = state == ColorTriggerState.Executing ? "流程运行中" : "监听中";
                    LastMessage = state switch
                    {
                        ColorTriggerState.Unknown => "未知颜色，等待明确的激活或未激活状态",
                        ColorTriggerState.Executing => "色块已激活，正在执行按键流程",
                        ColorTriggerState.Watching => "已检测到未激活，等待色块激活",
                        _ => "等待色块恢复未激活；持续亮起不会重复执行"
                    };
                    _logger.Info(LastMessage);
                }))));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            _logger.Error("颜色触发流程失败，关闭监听", exception);
            finalMessage = "检测或流程执行失败，已停止。请检查取色位置和配置，详情已记录";
            if (!_disposed) _runtime.Stop(finalMessage);
        }
        finally
        {
            _monitorCancellation = null;
            if (!_disposed)
            {
                StateText = "已停止"; LastMessage = finalMessage;
                OnPropertyChanged(nameof(IsMonitoring)); OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(WindowTitle));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }
}
