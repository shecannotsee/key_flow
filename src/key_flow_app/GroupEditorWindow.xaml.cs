using System.Windows;
using key_flow_core;

namespace key_flow_app;

public partial class GroupEditorWindow : Window
{
    private readonly ProfileGroup group;
    public GroupEditorWindow(ProfileGroup group)
    {
        InitializeComponent();
        this.group = group;
        GroupNameBox.Text = group.Name;
    }
    // Kept for compatibility with older UI hosts; group membership is no longer edited here.
    public GroupEditorWindow(ProfileGroup group, IEnumerable<KeyProfile> _) : this(group) { }
    private void ApplyClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(GroupNameBox.Text)) { GroupNameBox.Focus(); return; }
        group.Name = GroupNameBox.Text.Trim();
        DialogResult = true;
    }
}
