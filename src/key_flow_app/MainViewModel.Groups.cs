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
    public ICommand MoveProfileToGroupCommand { get; private set; } = null!;
    private bool IsParticipating(KeyProfile profile) => ProfileParticipation.IsEligible(profile, Groups);
    private ProfileGroup DefaultGroup => Groups.First(group => group.IsDefault);

    private void SetupGroupCommands()
    {
        NewGroupCommand = new RelayCommand(_ =>
        {
            var number = 1;
            while (Groups.Any(group => group.Name == $"配置组 {number}")) number++;
            var group = new ProfileGroup { Name = $"配置组 {number}", Participates = false };
            if (!ShowGroupEditor(group)) return;
            Groups = [.. Groups, group];
            EnsureSingleMembership(group);
            RefreshGroups();
        }, _ => CanEdit);
        EditGroupCommand = new RelayCommand(value =>
        {
            if (value is not ProfileGroup group) return;
            if (ShowGroupEditor(group))
            {
                EnsureSingleMembership(group);
                RefreshGroups();
            }
        }, _ => CanEdit);
        ToggleGroupCommand = new RelayCommand(value =>
        {
            if (value is not ProfileGroup group) return;
            group.Participates = !group.Participates;
            foreach (var profile in Profiles.Where(profile => group.MemberIds.Contains(profile.Id)).ToArray())
            {
                if (!group.Participates)
                {
                    _monitorCancellations.GetValueOrDefault(profile)?.Cancel();
                    _activationPool.Cancel(profile);
                }
            }
            RefreshGroups();
            if (group.Participates && IsListening)
                foreach (var profile in Profiles.Where(profile => group.MemberIds.Contains(profile.Id) && profile.Enabled && profile.ColorTriggerEnabled).ToArray())
                    _ = StartMonitoringAsync(profile);
        }, _ => CanEdit);
        DeleteGroupCommand = new RelayCommand(value => _ = DeleteGroupAsync(value as ProfileGroup), value => CanEdit && value is ProfileGroup group && !group.IsDefault);
        MoveProfileToGroupCommand = new RelayCommand(value =>
        {
            if (value is not ProfileGroup group || !Profiles.Contains(SelectedProfile) || group.MemberIds.Contains(SelectedProfile.Id)) return;
            foreach (var owner in Groups) owner.MemberIds.RemoveAll(id => id == SelectedProfile.Id);
            group.MemberIds.Add(SelectedProfile.Id);
            SelectedProfile.GroupName = group.Name;
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
            var dialog = new GroupEditorWindow(group) { Owner = Application.Current.MainWindow };
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

    private async Task DeleteGroupAsync(ProfileGroup? group)
    {
        if (group is null || group.IsDefault || !CanEdit) return;
        var fallback = DefaultGroup;
        if (MessageBox.Show(Application.Current.MainWindow, $"删除配置组“{group.Name}”？组内 {group.MemberIds.Count} 个方案会迁移到“{fallback.Name}”，不会删除方案。", "删除配置组", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        var oldNames = Profiles.ToDictionary(profile => profile.Id, profile => profile.GroupName);
        var groupsAfterDelete = Groups.Where(item => item != group).Select(item => new ProfileGroup
        {
            Id = item.Id, Name = item.Name, IsDefault = item.IsDefault, Participates = item.Participates,
            MemberIds = item == fallback ? [.. item.MemberIds, .. group.MemberIds] : item.MemberIds.ToList()
        }).ToList();
        foreach (var profile in Profiles.Where(profile => group.MemberIds.Contains(profile.Id))) profile.GroupName = fallback.Name;
        try
        {
            await _store.SaveAsync(new AppConfig { Version = JsonConfigStore.CurrentVersion, Profiles = Profiles, Groups = groupsAfterDelete });
            Groups = groupsAfterDelete;
            if (ReferenceEquals(MainGroup, group)) MainGroup = fallback;
            RefreshGroups();
            LastMessage = $"配置组已删除，方案已迁移到“{fallback.Name}”并保存";
        }
        catch (Exception exception)
        {
            foreach (var profile in Profiles) profile.GroupName = oldNames[profile.Id];
            _logger.Error("删除配置组失败", exception);
            LastMessage = $"删除失败，配置未更改：{exception.Message}";
        }
    }

    private void EnsureSingleMembership(ProfileGroup changed)
    {
        var selected = changed.MemberIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var other in Groups.Where(other => other != changed))
            other.MemberIds.RemoveAll(selected.Contains);
        var assigned = Groups.SelectMany(group => group.MemberIds).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fallback = DefaultGroup;
        foreach (var profile in Profiles)
        {
            if (!assigned.Contains(profile.Id)) fallback.MemberIds.Add(profile.Id);
            var owner = Groups.FirstOrDefault(group => group.MemberIds.Contains(profile.Id)) ?? fallback;
            profile.GroupName = owner.Name;
        }
    }
}
