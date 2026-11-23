using System.Runtime.InteropServices;
using InputProbe.Core;

namespace InputProbe.Windows;

public interface IDdKeyboard
{
    int ToCode(int virtualKey);
    int Key(int code, int action);
}

public sealed class DeferredDdKeyboard(Func<IDdKeyboard> factory) : IDdKeyboard
{
    private readonly Lazy<IDdKeyboard> instance = new(factory);
    public void Initialize() => _ = instance.Value;
    public int ToCode(int virtualKey) => instance.Value.ToCode(virtualKey);
    public int Key(int code, int action) => instance.Value.Key(code, action);
}

// The vendor library is kept loaded for the process lifetime. No driver installation
// or security setting changes occur here. Use one instance per app process.
public sealed class DdNativeKeyboard : IDdKeyboard
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int Unary(int value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int Binary(int code, int action);
    private readonly Unary convert;
    private readonly Binary key;
    private static readonly object Gate = new();
    private static bool attempted;

    public DdNativeKeyboard(string dllPath, Action<string>? diagnostic = null)
    {
        var path = Path.GetFullPath(dllPath);
        if (!File.Exists(path)) throw new FileNotFoundException("DD DLL 不存在", path);
        lock (Gate)
        {
            if (attempted) throw new InvalidOperationException("DD 后端每个进程仅初始化一次，请重启程序");
            attempted = true;
            diagnostic?.Invoke($"DD 阶段 before-load：{InputDiagnostics.DescribeModifiers()}");
            var module = NativeLibrary.Load(path, typeof(DdNativeKeyboard).Assembly,
                DllImportSearchPath.UseDllDirectoryForDependencies | DllImportSearchPath.System32);
            diagnostic?.Invoke($"DD 阶段 after-load：{InputDiagnostics.DescribeModifiers()}");
            convert = Marshal.GetDelegateForFunctionPointer<Unary>(NativeLibrary.GetExport(module, "DD_todc"));
            key = Marshal.GetDelegateForFunctionPointer<Binary>(NativeLibrary.GetExport(module, "DD_key"));
            var initialize = Marshal.GetDelegateForFunctionPointer<Unary>(NativeLibrary.GetExport(module, "DD_btn"));
            var result = InitializationCursorRecovery.ForDesktop(diagnostic).Run(() => initialize(0));
            diagnostic?.Invoke($"DD 阶段 after-DD_btn(0) result={result}：{InputDiagnostics.DescribeModifiers()}");
            if (result != 1)
                throw new InvalidOperationException($"DD_btn(0) 初始化失败，返回 {result}。请检查配套驱动安装、设备占用及厂商认证。");
        }
    }
    public int ToCode(int virtualKey) => convert(virtualKey);
    public int Key(int code, int action) => key(code, action);
}

public sealed class DdKeySender(IDdKeyboard keyboard) : IKeySender
{
    private static readonly Dictionary<string, int> VirtualKeys = Enumerable.Range(1, 254)
        .Select(vk => (Name: VirtualKeyNames.FromVirtualKey(vk), Vk: vk))
        .Where(pair => pair.Name.Length > 0)
        .ToDictionary(pair => pair.Name, pair => pair.Vk, StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> held = new(StringComparer.OrdinalIgnoreCase);
    private readonly object gate = new();
    private bool closed;

    public void KeyDown(string key)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            var chord = KeyChord.Parse(key);
            if (chord.Keys.Length > 1)
            {
                foreach (var part in chord.Keys) KeyDown(part);
                return;
            }
            key = chord.MainKey;
            if (held.ContainsKey(key)) return;
            if (!VirtualKeys.TryGetValue(key, out var vk)) throw new ArgumentException($"不支持的按键：{key}");
            var code = keyboard.ToCode(vk);
            if (code <= 0) throw new InvalidOperationException($"DD_todc({vk}) 转换失败：{code}");
            // A failed native call may have side effects: retain for cleanup attempts.
            held.Add(key, code);
            Check(keyboard.Key(code, 1), key, "Down");
        }
    }
    public void KeyUp(string key)
    {
        lock (gate)
        {
            var chord = KeyChord.Parse(key);
            if (chord.Keys.Length > 1)
            {
                var errors = new List<Exception>();
                foreach (var part in chord.Keys.Reverse())
                    try { KeyUp(part); } catch (Exception e) { errors.Add(e); }
                if (errors.Count > 0) throw new AggregateException("组合键释放失败", errors);
                return;
            }
            key = chord.MainKey;
            if (!held.TryGetValue(key, out var code)) return;
            Check(keyboard.Key(code, 2), key, "Up");
            held.Remove(key);
        }
    }
    public void ReleaseAll()
    {
        lock (gate)
        {
            var errors = new List<Exception>();
            foreach (var key in held.Keys.Reverse().ToArray())
            {
                try { KeyUp(key); }
                catch (Exception first)
                {
                    try { KeyUp(key); }
                    catch (Exception second) { errors.Add(new AggregateException(first, second)); }
                }
            }
            if (errors.Count > 0) throw new AggregateException("DD 按键释放失败；仍保留未释放键供重试", errors);
        }
    }
    public void Shutdown()
    {
        lock (gate)
        {
            closed = true;
            ReleaseAll();
        }
    }
    private static void Check(int result, string key, string action)
    {
        if (result != 1) throw new InvalidOperationException($"DD_key {key} {action} 返回 {result}，预期 1");
    }
}
