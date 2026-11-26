using key_flow_app;
using key_flow_core;
using Xunit;

public sealed class MainPresentationTests
{
    [Fact]
    public void GroupAndSearchIntersectWithoutChangingParticipation()
    {
        var first = new KeyProfile { Name = "Alpha", IsInActivationPool = false };
        var second = new KeyProfile { Name = "Beta", IsInActivationPool = true };
        var group = new ProfileGroup { Name = "Office", IsSelectedForRun = true, Participates = true, MemberIds = [first.Id] };
        KeyProfile[] profiles = [first, second];
        ProfileGroup[] groups = [group];
        Assert.Equal([first], MainProfileRow.Filter(profiles, groups, group, " ALPHA "));
        Assert.Empty(MainProfileRow.Filter(profiles, groups, group, "Beta"));
        Assert.Equal([first], MainProfileRow.Filter(profiles, groups, null, "office"));
        Assert.Equal(profiles, MainProfileRow.Filter(profiles, groups, null, ""));
        Assert.False(first.IsInActivationPool);
        Assert.True(second.IsInActivationPool);
        Assert.True(group.Participates);
        Assert.Equal([first.Id], group.MemberIds);
    }

    [Fact]
    public void GroupActivationAndExecutionRemainDistinctAndRowsKeepIdentity()
    {
        var profile = new KeyProfile { Name = "Alpha", StartHotkey = "Shift+1", Enabled = true };
        var row = new MainProfileRow(profile, true, "空闲");
        Assert.Equal("已启用 · 组已激活", row.Participation);
        Assert.False(row.HasActivity);
        var notifications = 0;
        row.PropertyChanged += (_, _) => notifications++;
        row.Refresh(true, "执行 / 排队");
        Assert.True(row.HasActivity);
        Assert.Same(profile, row.Profile);
        profile.Name = "Renamed";
        row.Refresh(false, "空闲");
        Assert.Equal("Renamed", row.Name);
        Assert.Equal("Shift+1", row.Trigger);
        Assert.Equal("已启用 · 等待组激活", row.Participation);
        Assert.False(row.HasActivity);
        Assert.Equal(2, notifications);
    }
}
