using InputProbe.Windows;

if (args.Length != 1) { Console.Error.WriteLine("Usage: DdProbe <vendor HID DLL path>. Initializes DLL; does not send input."); return 2; }
try
{
    var keyboard = new DdNativeKeyboard(args[0]);
    Console.WriteLine("DD initialization succeeded. No key events sent.");
    foreach (var vk in new[] { 0x31, 0x41, 0x10, 0x11, 0x12, 0x25 })
    {
        var code = keyboard.ToCode(vk);
        Console.WriteLine($"VK=0x{vk:X2} DD={code}");
        if (code <= 0) return 1;
    }
    return 0;
}
catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
