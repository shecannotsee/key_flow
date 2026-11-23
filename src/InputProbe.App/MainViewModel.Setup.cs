using System.IO;
using System.Windows;
using System.Windows.Input;
using InputProbe.Core;
using InputProbe.Windows;
using Microsoft.Win32;

namespace InputProbe.App;

public sealed partial class MainViewModel
{
    private DeferredDdKeyboard _keyboard = null!;
    private bool _initializationAttempted, _profileBusy;
    public bool IsInitialized { get; private set; }
    public string InitializationText { get; private set; } = "准备使用";
    public ICommand InitializeCommand { get; private set; } = null!;
    public ICommand DuplicateProfileCommand { get; private set; } = null!;
    public ICommand ImportCommand { get; private set; } = null!;
    public ICommand ExportCommand { get; private set; } = null!;

    private void SetupCommands()
    {
        InitializeCommand = new RelayCommand(async _ => await InitializeAsync(), _ => !_initializationAttempted && !_disposed);
        DuplicateProfileCommand = new RelayCommand(_ =>
        {
            var copy = ProfileOperations.Copy(SelectedProfile);
            Profiles = [.. Profiles, copy];
            OnPropertyChanged(nameof(Profiles)); SelectedProfile = copy;
            LastMessage = "已复制配置；点击保存写入本地";
        }, _ => CanEdit && _validationErrors == 0);
        ImportCommand = new RelayCommand(async _ => await TransferProfileAsync(true), _ => CanEdit && _validationErrors == 0);
        ExportCommand = new RelayCommand(async _ => await TransferProfileAsync(false), _ => CanEdit && _validationErrors == 0);
        LastMessage = "点击“准备使用”后解锁功能；准备期间可能出现菜单弹出或鼠标短暂移动";
    }

    private async Task InitializeAsync()
    {
        _initializationAttempted = true;
        InitializationText = "正在准备…"; StateText = "准备中";
        OnPropertyChanged(nameof(InitializationText)); CommandManager.InvalidateRequerySuggested();
        try
        {
            await Task.Run(_keyboard.Initialize);
            if (_disposed) return;
            IsInitialized = true; InitializationText = "已就绪"; StateText = "已停止";
            LastMessage = "准备完成，可以编辑配置或开始执行";
            ConfigureHotkeys();
        }
        catch (Exception e)
        {
            _logger.Error("手动初始化失败", e);
            if (_disposed) return;
            InitializationText = "准备失败，请重启"; StateText = "准备失败"; LastMessage = "暂时无法使用，请关闭程序后重新打开。问题详情已记录";
        }
        finally
        {
            if (!_disposed)
            {
                OnPropertyChanged(nameof(IsInitialized)); OnPropertyChanged(nameof(CanEdit));
                OnPropertyChanged(nameof(InitializationText)); CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    private async Task TransferProfileAsync(bool import)
    {
        string path;
        if (import)
        {
            var dialog = new OpenFileDialog { Filter = "按键配置 (*.json)|*.json", CheckFileExists = true };
            if (dialog.ShowDialog() != true) return;
            path = dialog.FileName;
        }
        else
        {
            var dialog = new SaveFileDialog { Filter = "按键配置 (*.json)|*.json", FileName = "按键配置.json", OverwritePrompt = true };
            if (dialog.ShowDialog() != true) return;
            path = dialog.FileName;
        }
        if (Path.GetFullPath(path).Equals(Path.Combine(AppContext.BaseDirectory, "config.json"), StringComparison.OrdinalIgnoreCase))
        { LastMessage = "请选择单独的交换文件，当前配置请使用保存按钮"; return; }
        _profileBusy = true; OnPropertyChanged(nameof(CanEdit)); CommandManager.InvalidateRequerySuggested();
        try
        {
            var store = new JsonConfigStore(path);
            if (import)
            {
                if (!File.Exists(path)) throw new FileNotFoundException("配置文件不存在", path);
                var config = await store.LoadAsync();
                ProfileOperations.ValidateForRepeat(config.Profiles);
                if (_disposed) return;
                Profiles = [.. Profiles, .. config.Profiles];
                OnPropertyChanged(nameof(Profiles)); SelectedProfile = config.Profiles[0];
                LastMessage = $"已追加导入 {config.Profiles.Count} 个配置；点击保存写入本地";
            }
            else
            {
                ProfileOperations.ValidateForRepeat([SelectedProfile]);
                await store.SaveAsync(new AppConfig { Profiles = [ProfileOperations.Copy(SelectedProfile, false)] });
                LastMessage = "当前配置已导出";
            }
        }
        catch (Exception e) { _logger.Error("配置交换失败", e); LastMessage = $"操作失败：{e.Message}"; }
        finally { _profileBusy = false; OnPropertyChanged(nameof(CanEdit)); CommandManager.InvalidateRequerySuggested(); }
    }
}
