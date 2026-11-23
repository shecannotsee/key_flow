using System.Reflection;
using System.Runtime.InteropServices;
using InputProbe.Windows;
using Xunit;

public sealed class SendInputEncodingTests
{
    [Fact]
    public void NativeInputSizeMatchesWindowsAbi()
    {
        var input = typeof(SendInputKeySender).GetNestedType("INPUT", BindingFlags.NonPublic)!;
        Assert.Equal(IntPtr.Size == 8 ? 40 : 28, Marshal.SizeOf(input));
    }

    [Theory]
    [InlineData(SendInputMode.VirtualKey, 0x25, 1)]
    [InlineData(SendInputMode.ScanCode, 0x25, 9)]
    [InlineData(SendInputMode.VirtualKey, 0x41, 0)]
    [InlineData(SendInputMode.ScanCode, 0x41, 8)]
    public void ExtendedKeysAndReleaseHaveCorrectFlags(SendInputMode mode, int vk, int flags)
    {
        var sender = new SendInputKeySender(mode: mode);
        var create = typeof(SendInputKeySender).GetMethod("CreateInput", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (var up in new[] { false, true })
        {
            var input = create.Invoke(sender, [(ushort)vk, up])!;
            var union = input.GetType().GetField("U")!.GetValue(input)!;
            var keyboard = union.GetType().GetField("ki")!.GetValue(union)!;
            Assert.Equal((uint)(flags | (up ? 2 : 0)), keyboard.GetType().GetField("dwFlags")!.GetValue(keyboard));
            Assert.Equal(mode == SendInputMode.ScanCode ? (ushort)0 : (ushort)vk, keyboard.GetType().GetField("wVk")!.GetValue(keyboard));
            Assert.NotEqual((ushort)0, keyboard.GetType().GetField("wScan")!.GetValue(keyboard));
        }
    }
}
