using System.Windows;
using System.Windows.Input;
using key_flow_core;

namespace key_flow_app;

public sealed partial class MainViewModel
{
    private readonly SemaphoreSlim _groupSelectionSaveLock = new(1, 1);
    public List<ProfileGroup> Groups { get; private set; } = [];
    public ICommand NewGroupCommand { get; private set; } = null!;
    public ICommand EditGroupCommand { get; private set; } = null!;
    public ICommand SetGroupRunSelectionCommand { get; private set; } = null!;
    // Kept as a compatibility alias for older views; it now changes run selection only.
    public ICommand ToggleGroupCommand { get; private set; } = null!;
    public ICommand DeleteGroupCommand { get; private set; } = null!;
    public ICommand MoveProfileToGroupCommand { get; private set; } = null!;
    public ICommand EnableAllProfilesCommand { get; private set; } = null!;
    public ICommand DisableAllProfilesCommand { get; private set; } = null!;
    private ProfileGroup? _moveTargetGroup;
    public ProfileGroup? MoveTargetGroup
    {
        get => _moveTargetGroup;
        set { _moveTargetGroup = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
    }
    private bool IsParticipating(KeyProfile profile) => ProfileParticipation.IsEligible(profile, Groups);
    private ProfileGroup DefaultGroup => Groups.First(group => group.IsDefault);

    private void SetupGroupCommands()
    {
        NewGroupCommand = new RelayCommand(_ =>
        {
            var number = 1;
            while (Groups.Any(group => group.Name == $"配置组 {number}")) number++;
            var group = new ProfileGroup { Name = $"配置组 {number}", IsSelectedForRun = false, Participates = false };
            if (!ShowGroupEditor(group)) return;
            Groups = [.. Groups, group];
            EnsureSingleMembership(group);
            MainGroup = group;
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
        SetGroupRunSelectionCommand = new RelayCommand(value =>
        {
            if (value is not ProfileGroup group) return;
            HandleGroupRunSelectionChanged(group);
        }, _ => CanEdit);
        ToggleGroupCommand = SetGroupRunSelectionCommand;
        DeleteGroupCommand = new RelayCommand(value => _ = DeleteGroupAsync(value as ProfileGroup), value => CanEdit && value is ProfileGroup group && !group.IsDefault);
        MoveProfileToGroupCommand = new RelayCommand(value =>
        {
            var group = value as ProfileGroup ?? MoveTargetGroup;
            if (group is null || !Profiles.Contains(SelectedProfile) || group.MemberIds.Contains(SelectedProfile.Id)) return;
            foreach (var owner in Groups) owner.MemberIds.RemoveAll(id => id == SelectedProfile.Id);
            group.MemberIds.Add(SelectedProfile.Id);
            SelectedProfile.GroupName = group.Name;
            RefreshGroups();
            MainGroup = group;
            MoveTargetGroup = null;
            LastMessage = $"配置“{SelectedProfile.Name}”已移动到“{group.Name}”";
        }, _ => CanEdit && Profiles.Contains(SelectedProfile) && MoveTargetGroup is not null && !MoveTargetGroup.MemberIds.Contains(SelectedProfile.Id));
        EnableAllProfilesCommand = new RelayCommand(_ => SetGroupProfilesEnabled(MainGroup, true), _ => CanEdit && MainGroup is not null);
        DisableAllProfilesCommand = new RelayCommand(_ => SetGroupProfilesEnabled(MainGroup, false), _ => CanEdit && MainGroup is not null);
    }

    private void SetGroupProfilesEnabled(ProfileGroup? group, bool enabled)
    {
        if (group is null || !CanEdit) return;
        foreach (var profile in Profiles.Where(profile => group.MemberIds.Contains(profile.Id)))
        {
            profile.Enabled = enabled;
            if (!enabled)
            {
                _activationPool.Cancel(profile);
            }
        }
        ConfigureHotkeys();
        RefreshMainPresentation();
        if (enabled) StartListening();
        LastMessage = enabled ? $"已开启“{group.Name}”中的全部配置" : $"已关闭“{group.Name}”中的全部配置";
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
        RefreshMainPresentation();
        ConfigureHotkeys();
        CommandManager.InvalidateRequerySuggested();
        LastMessage = "配置组已更新；点击保存全部保留更改";
    }

    private void HandleGroupRunSelectionChanged(ProfileGroup group)
    {
        // Keep the legacy serialized field synchronized for older builds reading this file.
        group.Participates = group.IsSelectedForRun;
        if (IsListening)
        {
            foreach (var profile in Profiles.Where(profile => group.MemberIds.Contains(profile.Id)).ToArray())
            {
                if (!group.IsSelectedForRun) _activationPool.Cancel(profile);
            }
        }
        RefreshGroups();
        LastMessage = "本次运行配置组选择已保存";
        _ = PersistGroupSelectionAsync();
    }

    private async Task PersistGroupSelectionAsync()
    {
        await _groupSelectionSaveLock.WaitAsync();
        try
        {
            await _store.SaveAsync(new AppConfig { Version = JsonConfigStore.CurrentVersion, Profiles = Profiles, Groups = Groups });
            LastMessage = "本次运行配置组选择已保存";
        }
        catch (Exception exception)
        {
            _logger.Error("保存配置组运行选择失败", exception);
            LastMessage = $"配置组选择未能保存：{exception.Message}";
        }
        finally { _groupSelectionSaveLock.Release(); }
    }

    private async Task DeleteGroupAsync(ProfileGroup? group)
    {
        if (group is null || group.IsDefault || !CanEdit) return;
        var fallback = DefaultGroup;
        if (MessageBox.Show(Application.Current.MainWindow, $"删除配置组“{group.Name}”？组内 {group.MemberIds.Count} 个方案会迁移到“{fallback.Name}”，不会删除方案。", "删除配置组", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        var oldNames = Profiles.ToDictionary(profile => profile.Id, profile => profile.GroupName);
        var groupsAfterDelete = Groups.Where(item => item != group).Select(item => new ProfileGroup
        {
            Id = item.Id, Name = item.Name, IsDefault = item.IsDefault, Participates = item.Participates, IsSelectedForRun = item.IsSelectedForRun,
            MemberIds = item == fallback ? [.. item.MemberIds, .. group.MemberIds] : item.MemberIds.ToList()
        }).ToList();
        foreach (var profile in Profiles.Where(profile => group.MemberIds.Contains(profile.Id))) profile.GroupName = fallback.Name;
        try
        {
            await _store.SaveAsync(new AppConfig { Version = JsonConfigStore.CurrentVersion, Profiles = Profiles, Groups = groupsAfterDelete });
            Groups = groupsAfterDelete;
            if (ReferenceEquals(MainGroup, group)) MainGroup = groupsAfterDelete.Single(item => item.IsDefault);
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
