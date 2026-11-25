using key_flow_core;
using Xunit;

public sealed class ProfileGroupTests
{
    [Fact]
    public void OnlyEnabledProfilesInActivatedOwningGroupAreEligible()
    {
        var profile = new KeyProfile { Enabled = true, Steps = [new("E")] };
        var group = new ProfileGroup { Participates = false, MemberIds = [profile.Id] };
        Assert.False(ProfileParticipation.IsEligible(profile, [group]));
        group.Participates = true;
        Assert.True(ProfileParticipation.IsEligible(profile, [group]));
        profile.Enabled = false;
        Assert.False(ProfileParticipation.IsEligible(profile, [group]));
        profile.Enabled = true;
        profile.IsInActivationPool = false;
        Assert.True(ProfileParticipation.IsEligible(profile, [group]));
    }

    [Fact]
    public async Task GroupsRoundTripAndLastProfileCanBeDeletedWithoutReturningOnReload()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var profile = new KeyProfile { Enabled = true, Steps = [new("E")] };
            var config = new AppConfig { Profiles = [profile], Groups = [new ProfileGroup { Name = "默认配置组", IsDefault = true, Participates = true, MemberIds = [profile.Id] }] };
            var store = new JsonConfigStore(path);
            await store.SaveAsync(config);
            config = await store.LoadAsync();
            Assert.True(ProfileParticipation.Includes(config.Profiles[0], config.Groups));
            ProfileParticipation.Remove(config, config.Profiles[0]);
            await store.SaveAsync(config);
            var loaded = await store.LoadAsync();
            Assert.Empty(loaded.Profiles);
            Assert.Empty(loaded.Groups[0].MemberIds);
        }
        finally { File.Delete(path); File.Delete(path + ".bak"); }
    }

    [Fact]
    public async Task LegacyParticipationMigratesToEnabledAndDefaultGroupIsCreatedOnce()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            await File.WriteAllTextAsync(path, """{"Version":9,"Profiles":[{"Id":"legacy","Name":"Old","GroupName":"Daily","IsInActivationPool":true,"Steps":[{"Key":"E","WaitAfterMs":80,"HoldMs":30}]}],"Groups":[{"Id":"daily","Name":"Daily","Participates":false,"MemberIds":["legacy"]}]}""");
            var store = new JsonConfigStore(path);
            var config = await store.LoadAsync();
            Assert.Equal(2, config.Groups.Count);
            var legacyGroup = Assert.Single(config.Groups, group => group.Name == "Daily");
            Assert.Contains(config.Profiles[0].Id, legacyGroup.MemberIds);
            Assert.True(legacyGroup.Participates);
            Assert.True(config.Profiles[0].IsInActivationPool);
            Assert.True(config.Profiles[0].Enabled);
            Assert.Single(config.Groups, group => group.IsDefault);
            Assert.Equal(80, config.Profiles[0].Steps[0].WaitAfterMs);
            await store.SaveAsync(config);
            var reloaded = await store.LoadAsync();
            Assert.Single(reloaded.Groups, group => group.IsDefault);
            Assert.Equal(1, reloaded.Groups.SelectMany(group => group.MemberIds).Count(id => id == "legacy"));
        }
        finally { File.Delete(path); }
    }
}
