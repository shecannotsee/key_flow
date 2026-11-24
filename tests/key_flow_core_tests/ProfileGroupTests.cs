using key_flow_core;
using Xunit;

public sealed class ProfileGroupTests
{
    [Fact]
    public void GroupAndIndividualParticipationAreIndependentAndOverlapsDoNotDuplicateProfiles()
    {
        var profile = new KeyProfile { Steps = [new("E")] };
        var first = new ProfileGroup { Participates = true, MemberIds = [profile.Id] };
        var second = new ProfileGroup { Participates = true, MemberIds = [profile.Id] };
        Assert.True(ProfileParticipation.Includes(profile, [first, second]));
        first.Participates = false;
        Assert.True(ProfileParticipation.Includes(profile, [first, second]));
        second.Participates = false;
        Assert.False(ProfileParticipation.Includes(profile, [first, second]));
        profile.IsInActivationPool = true;
        Assert.True(ProfileParticipation.Includes(profile, [first, second]));
    }

    [Fact]
    public async Task GroupsRoundTripAndLastProfileCanBeDeletedWithoutReturningOnReload()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var profile = new KeyProfile { Steps = [new("E")] };
            var config = new AppConfig { Profiles = [profile], Groups = [new ProfileGroup { Name = "日常", Participates = true, MemberIds = [profile.Id] }] };
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
    public async Task LegacyLabelsBecomeGroupsWithoutChangingIndividualParticipation()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            await File.WriteAllTextAsync(path, """{"Version":8,"Profiles":[{"Name":"Old","GroupName":"Daily","IsInActivationPool":true,"Steps":[{"Key":"E","WaitAfterMs":80,"HoldMs":30}]}]}""");
            var config = await new JsonConfigStore(path).LoadAsync();
            Assert.Single(config.Groups);
            Assert.Contains(config.Profiles[0].Id, config.Groups[0].MemberIds);
            Assert.True(config.Profiles[0].IsInActivationPool);
            Assert.False(config.Groups[0].Participates);
            Assert.Equal(80, config.Profiles[0].Steps[0].WaitAfterMs);
        }
        finally { File.Delete(path); }
    }
}
