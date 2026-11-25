using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace key_flow_core;

public sealed class ProfileGroup : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>Stable marker for the built-in group. Names remain editable.</summary>
    public bool IsDefault { get; set; }
    private string _name = "配置组";
    private bool _participates;
    public string Name { get => _name; set { _name = value; Changed(); } }
    public bool Participates { get => _participates; set { _participates = value; Changed(); } }
    private List<string> _memberIds = [];
    public List<string> MemberIds { get => _memberIds; set { _memberIds = value; Changed(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public static class ProfileParticipation
{
    public static bool Includes(KeyProfile profile, IEnumerable<ProfileGroup> groups)
        => IsEligible(profile, groups);

    public static void Remove(AppConfig config, KeyProfile profile)
    {
        config.Profiles.Remove(profile);
        foreach (var group in config.Groups) group.MemberIds.RemoveAll(id => id == profile.Id);
    }

    public static ProfileGroup? GroupFor(KeyProfile profile, IEnumerable<ProfileGroup> groups)
        => groups.FirstOrDefault(group => group.MemberIds.Contains(profile.Id));

    public static bool IsEligible(KeyProfile profile, IEnumerable<ProfileGroup> groups)
    {
        var group = GroupFor(profile, groups);
        return profile.Enabled && group?.Participates == true;
    }
}
