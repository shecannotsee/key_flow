using System.Windows;
using key_flow_core;

namespace key_flow_app;

public partial class GroupEditorWindow : Window
{
    private readonly ProfileGroup group;
    private readonly List<MemberChoice> members;
    public GroupEditorWindow(ProfileGroup group, IEnumerable<KeyProfile> profiles)
    {
        InitializeComponent();
        this.group = group;
        GroupNameBox.Text = group.Name;
        members = profiles.Select(profile => new MemberChoice { Profile = profile, Included = group.MemberIds.Contains(profile.Id) }).ToList();
        MembersList.ItemsSource = members;
    }
    private void ApplyClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(GroupNameBox.Text)) { GroupNameBox.Focus(); return; }
        group.Name = GroupNameBox.Text.Trim();
        group.MemberIds = members.Where(member => member.Included).Select(member => member.Profile.Id).ToList();
        DialogResult = true;
    }
    public sealed class MemberChoice
    {
        public KeyProfile Profile { get; init; } = null!;
        public bool Included { get; set; }
    }
}
