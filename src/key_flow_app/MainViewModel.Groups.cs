using System.Windows;
using System.Windows.Input;
using key_flow_core;

namespace key_flow_app;

public sealed partial class MainViewModel
{
    public List<ProfileGroup> Groups { get; private set; } = [];
    public ICommand NewGroupCommand { get; private set; } = null!;
    public ICommand EditGroupCommand { get; private set; } = null!;
    public ICommand ToggleGroupCommand { get; private set; } = null!;
    public ICommand DeleteGroupCommand { get; private set; } = null!;
    private bool IsParticipating(KeyProfile profile) => ProfileParticipation.Includes(profile, Groups);

    private void SetupGroupCommands()
    {
        NewGroupCommand = new RelayCommand(_ =>
        {
            var number = 1;
            while (Groups.Any(group => group.Name == $"配置组 {number}")) number++;
            var group = new ProfileGroup { Name = $"配置组 {number}" };
            if (!ShowGroupEditor(group)) return;
            Groups = [.. Groups, group];
            RefreshGroups();
        }, _ => CanEdit);
        EditGroupCommand = new RelayCommand(value =>
        {
            if (value is not ProfileGroup group) return;
            if (ShowGroupEditor(group)) RefreshGroups();
        }, _ => CanEdit);
        ToggleGroupCommand = new RelayCommand(value =>
        {
            if (value is not ProfileGroup group) return;
            group.Participates = !group.Participates;
            RefreshGroups();
        }, _ => CanEdit);
        DeleteGroupCommand = new RelayCommand(value =>
        {
            if (value is not ProfileGroup group) return;
            if (MessageBox.Show(Application.Current.MainWindow, $"删除配置组“{group.Name}”？组内方案将保留。", "删除配置组", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            Groups = Groups.Where(item => item != group).ToList();
            RefreshGroups();
        }, _ => CanEdit);
    }

    private bool ShowGroupEditor(ProfileGroup group)
    {
        if (!CanEdit) return false;
        CancelCapture();
        _profileBusy = true;
        _systemHotkeys?.Clear();
        OnPropertyChanged(nameof(CanEdit));
        try
        {
            var dialog = new GroupEditorWindow(group, Profiles) { Owner = Application.Current.MainWindow };
            return dialog.ShowDialog() == true;
        }
        finally
        {
            _profileBusy = false;
            ConfigureHotkeys();
            OnPropertyChanged(nameof(CanEdit));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void RefreshGroups()
    {
        OnPropertyChanged(nameof(Groups));
        OnPropertyChanged(nameof(ActivationPoolStatus));
        ConfigureHotkeys();
        CommandManager.InvalidateRequerySuggested();
        LastMessage = "配置组已更新；点击保存全部保留更改";
    }
}
