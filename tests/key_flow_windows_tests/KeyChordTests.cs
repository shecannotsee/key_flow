using System.IO;
using key_flow_core;
using key_flow_windows;
using Xunit;

public sealed class KeyChordTests
{
    [Theory]
    [InlineData("shift+D1", "Shift+1", 4u, 0x31)]
    [InlineData("Shift+Ctrl+1", "Ctrl+Shift+1", 6u, 0x31)]
    [InlineData("D8", "8", 0u, 0x38)]
    public void ParsesLegacyAndChordKeys(string text, string formatted, uint modifiers, int vk)
    {
        var chord = KeyChord.Parse(text);
        Assert.Equal(formatted, chord.Text); Assert.Equal(modifiers, chord.Modifiers); Assert.Equal(vk, chord.VirtualKey);
    }
    [Theory]
    [InlineData("Ctrl+Ctrl+1")]
    [InlineData("A+B")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Win+1")]
    public void RejectsMalformedChords(string text) => Assert.Throws<ArgumentException>(() => KeyChord.Parse(text));

    [Fact]
    public void CaptureWaitsForMainKeyAndAllReleases()
    {
        var capture = new ChordCapture();
        Assert.Null(capture.Down(0xA0, 4));
        Assert.Equal("Shift+1", capture.Down(0x31, 4));
        Assert.Null(capture.Down(0x31, 4));
        Assert.Null(capture.Up(0x31)); Assert.False(capture.IsReleased);
        Assert.Null(capture.Up(0xA0)); Assert.True(capture.IsReleased);
        capture.Reset(); Assert.Null(capture.Down(0xA2, 2)); Assert.Equal("Ctrl", capture.Up(0xA2));
    }

    [Fact]
    public void DdChordReleasesInReverseOrder()
    {
        var api = new FakeKeyboard(); var sender = new DdKeySender(api);
        sender.KeyDown("Ctrl+Shift+1"); sender.KeyUp("Ctrl+Shift+1"); sender.Shutdown();
        Assert.Equal(new[] { (17,1), (16,1), (49,1), (49,2), (16,2), (17,2) }, api.Events);
    }
    [Fact]
    public void PartialDownFailureStillReleasesAllChordParts()
    {
        var api = new FakeKeyboard { FailMain = true }; var sender = new DdKeySender(api);
        Assert.Throws<InvalidOperationException>(() => sender.KeyDown("Shift+1"));
        sender.KeyUp("Shift+1"); sender.Shutdown();
        Assert.Equal(new[] { (16,1), (49,1), (49,2), (16,2) }, api.Events);
    }
    [Fact]
    public void SequenceCannotTriggerPlainHotkeyOrEmergencyThroughChord()
    {
        Assert.Throws<InvalidDataException>(() => ProfileOperations.ValidateForRepeat([new KeyProfile { StartHotkey="1", Steps=[new("Shift+1",80)] }]));
        Assert.Throws<InvalidDataException>(() => ProfileOperations.ValidateForRepeat([new KeyProfile { Steps=[new("Ctrl+Shift+F10",80)] }]));
        ProfileOperations.ValidateForRepeat([new KeyProfile { StartHotkey="Ctrl+2", Steps=[new("Shift+1",80)] }]);
    }
    private sealed class FakeKeyboard : IDdKeyboard
    {
        public bool FailMain;
        public List<(int,int)> Events = [];
        public int ToCode(int vk) => vk;
        public int Key(int code, int action) { Events.Add((code,action)); return FailMain && code == 49 && action == 1 ? 0 : 1; }
    }
}
