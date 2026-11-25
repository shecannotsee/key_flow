using System.ComponentModel;
using System.Windows.Input;
using key_flow_core;

namespace key_flow_app;

// Main-window presentation only. Commands, participation and scheduling remain unchanged.
public sealed partial class MainViewModel
{
    private ProfileGroup? _mainGroup;
    private string _mainSearch = "";
    private string _mainExecutingName = "";
    private readonly Dictionary<KeyProfile, MainProfileRow> _mainRowCache = [];
    private List<MainProfileRow> _mainRows = [];
    private List<MainProfileRow> _mainParticipants = [];
    public ProfileGroup? MainGroup
    {
        get => _mainGroup;
        set { _mainGroup = value; RefreshMainPresentation(); }
    }
    public string MainSearch
    {
        get => _mainSearch;
        set { _mainSearch = value ?? ""; RefreshMainPresentation(); }
    }
    public ICommand ShowAllProfilesCommand => new RelayCommand(_ => MainGroup = null);
    public bool MainAllSelected => MainGroup is null;
    public bool MainHasGroup => MainGroup is not null;
    public string MainScopeTitle => MainGroup?.Name ?? "全部配置";
    public string MainGroupAction => MainGroup?.Participates == true ? "取消组激活" : "激活此组";
    public string MainProfileAction => SelectedProfile.IsInActivationPool ? "取消单独激活" : "单独激活";
    public string MainState => IsRunning ? "运行中" : IsMonitoring ? "颜色监听中" : "已停止";
    public string MainActivity => IsRunning ? $"正在执行：{_mainExecutingName}" : IsMonitoring ? "等待颜色条件满足" : "当前没有方案执行";
    public string MainCounts => $"{Profiles.Count(IsParticipating)} 个已激活 · {_activationPool.ActiveCount} 个执行或排队";
    public bool MainHasSelection => Profiles.Contains(SelectedProfile) && MainRows.Any(row => ReferenceEquals(row.Profile, SelectedProfile));
    public string MainSelectionLabel => MainHasSelection ? $"所选：{SelectedProfile.Name}" : "未选择配置";
    public MainProfileRow? MainSelectedRow
    {
        get => MainRows.FirstOrDefault(row => ReferenceEquals(row.Profile, SelectedProfile));
        set { if (value is not null) SelectedProfile = value.Profile; }
    }
    public bool MainHasError => LastMessage.Contains("失败", StringComparison.Ordinal);
    public string MainEmptyText => Profiles.Count == 0 ? "还没有配置，点击“新建配置”开始" : "没有匹配的配置，试试其他关键词或分组";
    public IReadOnlyList<MainProfileRow> MainRows => _mainRows;
    public IReadOnlyList<MainProfileRow> MainParticipants => _mainParticipants;

    private void RefreshMainPresentation()
    {
        if (_mainGroup is not null && !Groups.Contains(_mainGroup)) _mainGroup = null;
        foreach (var obsolete in _mainRowCache.Keys.Where(profile => !Profiles.Contains(profile)).ToArray()) _mainRowCache.Remove(obsolete);
        foreach (var profile in Profiles)
        {
            var activity = _activeProfileActivations.GetValueOrDefault(profile) > 0 ? "执行 / 排队" :
                _monitorCancellations.ContainsKey(profile) ? "颜色监听" : "空闲";
            if (!_mainRowCache.TryGetValue(profile, out var row)) _mainRowCache[profile] = row = new(profile, IsParticipating(profile), activity);
            row.Refresh(IsParticipating(profile), activity);
        }
        var visible = MainProfileRow.Filter(Profiles, Groups, MainGroup, MainSearch).Select(profile => _mainRowCache[profile]).ToList();
        var participants = Profiles.Where(IsParticipating).Select(profile => _mainRowCache[profile]).ToList();
        if (!_mainRows.SequenceEqual(visible))
        {
            _mainRows = visible;
            PropertyChanged?.Invoke(this, new(nameof(MainRows)));
        }
        if (!_mainParticipants.SequenceEqual(participants))
        {
            _mainParticipants = participants;
            PropertyChanged?.Invoke(this, new(nameof(MainParticipants)));
        }
        foreach (var name in new[] { nameof(MainGroup), nameof(MainSearch), nameof(MainAllSelected), nameof(MainHasGroup),
            nameof(MainScopeTitle), nameof(MainGroupAction), nameof(MainProfileAction), nameof(MainState), nameof(MainActivity),
            nameof(MainCounts), nameof(MainHasSelection), nameof(MainSelectionLabel), nameof(MainSelectedRow), nameof(MainHasError), nameof(MainEmptyText) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class MainProfileRow(KeyProfile profile, bool participates, string activity) : INotifyPropertyChanged
{
    public KeyProfile Profile { get; } = profile;
    public bool Participates { get; private set; } = participates;
    public string Activity { get; private set; } = activity;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh(bool participates, string activity)
    {
        Participates = participates;
        Activity = activity;
        PropertyChanged?.Invoke(this, new(null));
    }
    public string Name => Profile.Name;
    public string Trigger => string.IsNullOrWhiteSpace(Profile.AlternateStartHotkey)
        ? Profile.StartHotkey : $"{Profile.StartHotkey} / {Profile.AlternateStartHotkey}";
    public string TriggerDetails => $"{Trigger} · {(Profile.ColorTriggerEnabled ? "颜色条件" : "按键触发")}";
    public string Participation => !Participates ? "未激活" : Profile.IsInActivationPool ? "单独激活" : "组内激活";
    public bool HasActivity => Activity is "执行 / 排队" or "颜色监听";
    public static IEnumerable<KeyProfile> Filter(IEnumerable<KeyProfile> profiles, IEnumerable<ProfileGroup> groups,
        ProfileGroup? selectedGroup, string search)
    {
        var query = search.Trim();
        return profiles.Where(profile => (selectedGroup is null || selectedGroup.MemberIds.Contains(profile.Id)) &&
            (query.Length == 0 || profile.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             groups.Any(group => group.MemberIds.Contains(profile.Id) && group.Name.Contains(query, StringComparison.OrdinalIgnoreCase))));
    }
}
