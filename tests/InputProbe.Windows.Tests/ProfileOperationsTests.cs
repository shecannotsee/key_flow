using System.IO;
using InputProbe.Core;
using InputProbe.Windows;
using Xunit;

public sealed class ProfileOperationsTests
{
    [Fact]
    public async Task ToggleModePersistsAndInactiveStopKeyDoesNotConflict()
    {
        var profile = new KeyProfile { ToggleHotkey = true, Steps = [new("F9", 80)] };
        ProfileOperations.ValidateForRepeat([profile]);
        Assert.True(ProfileOperations.Copy(profile).ToggleHotkey);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            await new JsonConfigStore(path).SaveAsync(new AppConfig { Profiles = [profile] });
            Assert.True((await new JsonConfigStore(path).LoadAsync()).Profiles[0].ToggleHotkey);
            await File.WriteAllTextAsync(path, """{"Version":2,"Profiles":[{"Name":"Old","StartHotkey":"F8","StopHotkey":"F9","Steps":[{"Key":"D8","WaitAfterMs":80}]}]}""");
            Assert.False((await new JsonConfigStore(path).LoadAsync()).Profiles[0].ToggleHotkey);
        }
        finally { File.Delete(path); }
        profile.ToggleHotkey = false;
        Assert.Throws<InvalidDataException>(() => ProfileOperations.ValidateForRepeat([profile]));
    }
    [Fact]
    public void CopyHasIndependentStepsAndPreservesHotkeys()
    {
        var original = new KeyProfile { Name = "Test", Steps = [new("D8", 80)] };
        var copy = ProfileOperations.Copy(original);
        copy.Steps[0].WaitAfterMs = 200;
        Assert.Equal(80, original.Steps[0].WaitAfterMs);
        Assert.Equal("Test 副本", copy.Name);
        Assert.Equal(original.StartHotkey, copy.StartHotkey);
    }
    [Theory]
    [InlineData("F8", 80)]
    [InlineData("Unknown", 80)]
    [InlineData("D8", 0)]
    public void ImportRejectsInvalidOrConflictingSequences(string key, int interval)
    {
        Assert.Throws<InvalidDataException>(() => ProfileOperations.ValidateForRepeat([new KeyProfile { Steps = [new(key, interval)] }]));
    }
    [Fact]
    public async Task ExportAndImportRoundTripPreservesRepeatSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var config = new AppConfig { Profiles = [new KeyProfile { Name = "Test", Steps = [new("D8", 80), new("D1", 130)] }] };
            await new JsonConfigStore(path).SaveAsync(config);
            var imported = await new JsonConfigStore(path).LoadAsync();
            ProfileOperations.ValidateForRepeat(imported.Profiles);
            Assert.Equal(130, imported.Profiles[0].Steps[1].WaitAfterMs);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void ExplicitInitializationDoesNotSendKeysAndRunsOnce()
    {
        var calls = 0;
        var keyboard = new DeferredDdKeyboard(() => { calls++; return new NoInputKeyboard(); });
        keyboard.Initialize(); keyboard.Initialize();
        Assert.Equal(1, calls);
    }
    private sealed class NoInputKeyboard : IDdKeyboard
    {
        public int ToCode(int key) => throw new Exception("No key conversion during initialization");
        public int Key(int code, int action) => throw new Exception("No key output during initialization");
    }
}
