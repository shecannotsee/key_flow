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
        // The activation pool is the source of truth for queued/running work.
        // _activeProfileActivations is an observation counter and can remain
        // non-zero briefly while a cancelled request is unwinding.
        => _monitorCancellations.ContainsKey(profile) || _activationPool.IsActive(profile);

    private void StopEverything(string message)
    {
        _isListening = false;
        OnPropertyChanged(nameof(IsListening));
        ConfigureHotkeys();
        foreach (var cancellation in _monitorCancellations.Values.ToArray()) cancellation.Cancel();
        _activationPool.CancelAll();
        _runtime.Stop(message);
    }

    private void StopProfile(KeyProfile profile, string message)
    {
        var wasActive = IsProfileActive(profile);
        _logger.Info($"方案停止请求 name={profile.Name}, active={wasActive}, pool={_activationPool.IsActive(profile)}, runtimeBusy={_runtime.IsBusy}");
        _monitorCancellations.GetValueOrDefault(profile)?.Cancel();
        _activationPool.Cancel(profile);
        _activeProfileActivations.Remove(profile);
        if (wasActive || _runtime.IsBusy) _runtime.Stop(message);
        // The binding is unchanged when a profile stops. Keeping the listener's
        // per-source key-down state avoids treating another input channel's
        // copy of this same physical key press as a fresh toggle-on event.
        RefreshMainPresentation();
    }

    private void SetIgnoreAllSchemes(bool ignore)
    {
        lock (_ignoreAllSchemesGate)
        {
            if (_ignoreAllSchemes == ignore) return;
            _ignoreAllSchemes = ignore;
        }
        OnPropertyChanged(nameof(IgnoreAllSchemes));
        if (ignore)
        {
            ConfigureHotkeys();
            foreach (var cancellation in _monitorCancellations.Values.ToArray()) cancellation.Cancel();
            _activationPool.CancelAll();
            _runtime.Stop("已忽略所有方案");
            StateText = _isListening ? "监听中 · 已忽略所有方案" : "已忽略所有方案";
            LastMessage = "所有方案已忽略；方案启用状态和配置均已保留";
        }
        else if (_isListening)
        {
            ConfigureHotkeys();
            foreach (var profile in Profiles.Where(profile => ProfileParticipation.IsEligible(profile, Groups) && profile.ColorTriggerEnabled).ToArray())
                _ = StartMonitoringAsync(profile);
            StateText = "监听中";
            LastMessage = "已恢复已启用方案的触发响应";
        }
        else
        {
            StateText = "已停止";
            LastMessage = "已取消忽略；启动监听后方案将恢复响应";
        }
        OnPropertyChanged(nameof(WindowTitle));
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task StartMonitoringAsync(KeyProfile source)
    {
        if (!IsInitialized || !_isListening || IgnoreAllSchemes || _disposed || _profileBusy || !IsParticipating(source) || !source.ColorTriggerEnabled || _monitorCancellations.ContainsKey(source)) return;
        try { ProfileOperations.ValidateForRepeat([source]); }
        catch (InvalidDataException exception) { RejectStart(exception.Message); return; }

        var profile = ProfileOperations.Copy(source, false);
        profile.Enabled = true;
        profile.ColorCondition.Enabled = false;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_automationCancellation.Token);
        _monitorCancellations[source] = cancellation;
        OnPropertyChanged(nameof(IsMonitoring)); OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(WindowTitle));
        CommandManager.InvalidateRequerySuggested();
        StateText = IsMonitoring ? $"监听中 · {_monitorCancellations.Count} 个方案" : "监听中";
        LastMessage = $"方案“{profile.Name}”等待色块先恢复未激活，再次激活时执行";
        _logger.Info($"颜色规则开启 name={profile.Name}, debounceMs={profile.TriggerDebounceMs}, duration={profile.TotalDurationSeconds:F3}s, point=({profile.ColorCondition.X},{profile.ColorCondition.Y}), target={profile.ColorCondition.Target}, tolerance={profile.ColorCondition.Tolerance}");
        try
        {
            await Task.Run(() => new ColorTriggerMonitor(_screenReader).RunAsync(
                profile.ColorCondition, profile.TriggerDebounceMs,
                async token =>
                {
                    token.ThrowIfCancellationRequested();
                    if (_disposed || IgnoreAllSchemes) throw new OperationCanceledException(token);
                    _logger.Info($"颜色激活，方案进入激活池 name={profile.Name}");
                    ActivationSubmission? submission;
                    lock (_ignoreAllSchemesGate)
                        submission = _ignoreAllSchemes ? null : _activationPool.Submit(profile, source, source.RetriggerMode, token);
                    if (submission is null) return;
                    await submission.Value.Completion.ConfigureAwait(false);
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
            var wasCancelled = cancellation.IsCancellationRequested;
            _monitorCancellations.Remove(source);
            if (!_disposed)
            {
                StateText = IsMonitoring ? $"监听中 · {_monitorCancellations.Count} 个方案" : "已停止";
                LastMessage = IsMonitoring ? "其他方案仍在监听" : "监听和流程已停止";
                OnPropertyChanged(nameof(IsMonitoring)); OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(WindowTitle));
                CommandManager.InvalidateRequerySuggested();
                if (wasCancelled && _isListening && !IgnoreAllSchemes && IsParticipating(source) && source.ColorTriggerEnabled)
                    _ = StartMonitoringAsync(source);
            }
        }
    }
}
