using System.Runtime.InteropServices;
using System.Windows;

namespace key_flow_app;
public partial class App : Application
{
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        SetCurrentProcessExplicitAppUserModelID("key_flow_desktop");
        instance = new Mutex(true, "Local\\key_flow_desktop", out var first);
        if (!first)
        {
            MessageBox.Show("key_flow 已经打开，请使用现有窗口。", "key_flow", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(); return;
        }
        base.OnStartup(e);
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
    [DllImport("shell32.dll")] private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
