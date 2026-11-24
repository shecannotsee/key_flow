using System.IO;
using System.Windows;
using System.Windows.Input;
using key_flow_core;
using key_flow_windows;

namespace key_flow_app;

public sealed partial class MainViewModel
{
    private readonly Dictionary<KeyProfile, CancellationTokenSource> _monitorCancellations = [];
    public bool IsMonitoring => _monitorCancellations.Count > 0;
    private bool IsProfileActive(KeyProfile profile)
        => _monitorCancellations.ContainsKey(profile) || _activeProfileActivations.GetValueOrDefault(profile) > 0;

    private void StopEverything(string message)
    {
        foreach (var cancellation in _monitorCancellations.Values.ToArray()) cancellation.Cancel();
        _activationPool.CancelAll();
        _runtime.Stop(message);
    }

    private async Task StartMonitoringAsync(KeyProfile source)
    {
        if (!IsInitialized || _disposed || _profileBusy || _monitorCancellations.ContainsKey(source)) return;
        try { ProfileOperations.ValidateForRepeat([source]); }
        catch (InvalidDataException exception) { RejectStart(exception.Message); return; }

        var profile = ProfileOperations.Copy(source, false);
        profile.Enabled = true;
        profile.ColorCondition.Enabled = false;
        foreach (var step in profile.Steps) step.HoldMs = RepeatTiming.HoldFor(step.WaitAfterMs);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_automationCancellation.Token);
        _monitorCancellations[source] = cancellation;
        OnPropertyChanged(nameof(IsMonitoring)); OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(WindowTitle));
        CommandManager.InvalidateRequerySuggested();
        StateText = IsMonitoring ? $"监听中 · {_monitorCancellations.Count} 个方案" : "监听中";
        LastMessage = $"方案“{profile.Name}”等待色块先恢复未激活，再次激活时执行";
        _logger.Info($"颜色规则开启 name={profile.Name}, debounceMs={profile.TriggerDebounceMs}, duration={profile.DurationSeconds}s, point=({profile.ColorCondition.X},{profile.ColorCondition.Y}), target={profile.ColorCondition.Target}, tolerance={profile.ColorCondition.Tolerance}");
        try
        {
            await Task.Run(() => new ColorTriggerMonitor(_screenReader).RunAsync(
                profile.ColorCondition, profile.TriggerDebounceMs,
                async token =>
                {
                    token.ThrowIfCancellationRequested();
                    if (_disposed) throw new OperationCanceledException(token);
                    _logger.Info($"颜色激活，方案进入激活池 name={profile.Name}");
                    await _activationPool.EnqueueAsync(profile, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    _logger.Info($"颜色方案完成 name={profile.Name}，等待重新激活");
                }, cancellation.Token,
                state => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_disposed || cancellation.IsCancellationRequested || !_monitorCancellations.ContainsKey(source)) return;
                    StateText = state == ColorTriggerState.Executing ? $"执行中 · {_monitorCancellations.Count} 个方案" : $"监听中 · {_monitorCancellations.Count} 个方案";
                    LastMessage = state switch
                    {
                        ColorTriggerState.Unknown => $"方案“{profile.Name}”：未知颜色，等待明确的激活或未激活状态",
                        ColorTriggerState.Executing => $"方案“{profile.Name}”色块已激活，已加入激活池",
                        ColorTriggerState.Watching => $"方案“{profile.Name}”已检测到未激活，等待色块激活",
                        _ => $"方案“{profile.Name}”等待色块恢复未激活；持续亮起不会重复执行"
                    };
                    _logger.Info(LastMessage);
                }))));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            _logger.Error($"颜色触发流程失败 name={profile.Name}", exception);
            if (!_disposed) _runtime.Stop("颜色检测或方案执行失败，已停止");
        }
        finally
        {
            _monitorCancellations.Remove(source);
            if (!_disposed)
            {
                StateText = IsMonitoring ? $"监听中 · {_monitorCancellations.Count} 个方案" : "已停止";
                LastMessage = IsMonitoring ? "其他方案仍在监听" : "监听和流程已停止";
                OnPropertyChanged(nameof(IsMonitoring)); OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(WindowTitle));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }
}
