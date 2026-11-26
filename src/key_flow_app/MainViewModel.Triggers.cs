using System.Windows.Input;
using key_flow_core;

namespace key_flow_app;

public sealed partial class MainViewModel
{
    private bool IsProfileActive(KeyProfile profile)
        => _activationPool.IsActive(profile);

    private void StopEverything(string message)
    {
        _isListening = false;
        OnPropertyChanged(nameof(IsListening));
        ConfigureHotkeys();
        _activationPool.CancelAll();
        StopKeyTest();
        _runtime.Stop(message);
    }

    private void StopProfile(KeyProfile profile, string message)
    {
        var wasExecuting = _activationPool.IsExecuting(profile);
        _logger.Info($"方案停止请求 name={profile.Name}, active={IsProfileActive(profile)}, executing={wasExecuting}, runtimeBusy={_runtime.IsBusy}");
        _activationPool.Cancel(profile);
        _activeProfileActivations.Remove(profile);
        if (wasExecuting) _runtime.Stop(message);
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
            _activationPool.CancelAll();
            _runtime.Stop("已忽略所有方案");
            StateText = _isListening ? "监听中 · 已忽略所有方案" : "已忽略所有方案";
            LastMessage = "所有方案已忽略；方案启用状态和配置均已保留";
        }
        else if (_isListening)
        {
            ConfigureHotkeys();
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
}
