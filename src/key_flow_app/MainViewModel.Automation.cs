using System.IO;
using System.Windows.Input;
using key_flow_core;
using key_flow_windows;

namespace key_flow_app;

public sealed partial class MainViewModel
{
    private CancellationTokenSource? _keyTestCancellation;
    private int _keyTestDurationSeconds = 3;
    public int KeyTestDurationSeconds
    {
        get => _keyTestDurationSeconds;
        set
        {
            if (value is < 1 or > 30) throw new ArgumentOutOfRangeException(nameof(value), "测试时长应为 1 到 30 秒");
            if (value == _keyTestDurationSeconds) return;
            _keyTestDurationSeconds = value;
            OnPropertyChanged();
        }
    }
    public bool IsKeyTestRunning => _keyTestCancellation is not null;
    public bool CanTestWorkflow => IsInitialized && !_disposed && !_runtime.IsBusy && !_activationPool.IsBusy && !_profileBusy && _validationErrors == 0;
    public ICommand TestWorkflowCommand { get; private set; } = null!;
    public ICommand StopKeyTestCommand { get; private set; } = null!;
    public ICommand CaptureAlternateStartCommand { get; private set; } = null!;
    public ICommand ClearAlternateStartCommand { get; private set; } = null!;

    private void SetupAutomationCommands()
    {
        TestWorkflowCommand = new RelayCommand(async _ => await TestSelectedWorkflowAsync(), _ => CanTestWorkflow);
        StopKeyTestCommand = new RelayCommand(_ => StopKeyTest(), _ => IsKeyTestRunning);
        CaptureAlternateStartCommand = new RelayCommand(_ => BeginCapture(CaptureMode.AlternateStart), _ => CanEdit);
        ClearAlternateStartCommand = new RelayCommand(_ => { SelectedProfile.AlternateStartHotkey = ""; ConfigureHotkeys(); }, _ => CanEdit);
    }

    private async Task TestSelectedWorkflowAsync()
    {
        if (!CanTestWorkflow) return;
        var source = SelectedProfile;
        try
        {
            ProfileOperations.ValidateForRepeat([source]);
            var testProfile = ProfileOperations.Copy(source, false);
            testProfile.Enabled = true;
            testProfile.DurationSeconds = KeyTestDurationSeconds;
            testProfile.DurationMilliseconds = 0;
            _keyTestCancellation = new CancellationTokenSource();
            OnPropertyChanged(nameof(IsKeyTestRunning));
            OnPropertyChanged(nameof(CanTestWorkflow));
            CommandManager.InvalidateRequerySuggested();
            LastMessage = $"正在试运行“{source.Name}”的按键流程，最长 {KeyTestDurationSeconds} 秒";
            _runtime.Arm();
            await _runtime.TriggerAsync(testProfile, _keyTestCancellation.Token);
            if (!_keyTestCancellation.IsCancellationRequested)
                LastMessage = _runtime.LastRunFailed ? "按键测试失败，请检查输入设备和日志" : "按键测试完成，所有按键已释放";
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or InvalidOperationException)
        {
            LastMessage = $"无法测试：{exception.Message}";
        }
        finally
        {
            _keyTestCancellation?.Dispose();
            _keyTestCancellation = null;
            if (!_disposed)
            {
                OnPropertyChanged(nameof(IsKeyTestRunning));
                OnPropertyChanged(nameof(CanEdit));
                OnPropertyChanged(nameof(CanTestWorkflow));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    private void StopKeyTest()
    {
        if (_keyTestCancellation is null) return;
        _keyTestCancellation.Cancel();
        _runtime.Stop("按键测试已停止并释放按键");
        LastMessage = "按键测试已停止，所有按键已释放";
    }
}
