using System.Runtime.InteropServices;
using System.Windows;

namespace KeyFlow.App;
public partial class App : Application
{
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        SetCurrentProcessExplicitAppUserModelID("KeyFlow.Desktop");
        instance = new Mutex(true, "Local\\KeyFlow.Desktop", out var first);
        if (!first)
        {
            MessageBox.Show("KeyFlow 已经打开，请使用现有窗口。", "KeyFlow", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(); return;
        }
        base.OnStartup(e);
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
    [DllImport("shell32.dll")] private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
