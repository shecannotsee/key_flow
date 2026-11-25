using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using key_flow_core;
using key_flow_windows;

namespace key_flow_app;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        MainViewModel _viewModel;
        try { _viewModel = new MainViewModel(); }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            new FileInputLogger(AppPaths.Log).Error("窗口启动失败", exception);
            MessageBox.Show("暂时无法打开程序，请使用目录中的启动入口重试。问题详情已记录。", "key_flow", MessageBoxButton.OK, MessageBoxImage.Error);
            Application.Current.Shutdown(1);
            return;
        }
        DataContext = _viewModel;
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsInitialized))
            {
                if (_viewModel.IsInitialized)
                {
                    ResizeMode = ResizeMode.CanResize;
                    MinWidth = 760; MinHeight = 560;
                    if (WindowState == WindowState.Normal) { Width = 980; Height = 720; }
                }
            }
        };
        AddHandler(System.Windows.Controls.Validation.ErrorEvent, new EventHandler<System.Windows.Controls.ValidationErrorEventArgs>((_, e) =>
            _viewModel.ChangeValidationErrors(e.Action == System.Windows.Controls.ValidationErrorEventAction.Added ? 1 : -1)));
        PreviewKeyDown += (_, args) =>
        {
            if (_viewModel.CaptureText.Length == 0) return;
            if (!args.IsRepeat)
                _viewModel.HandleKeyCode(KeyInterop.VirtualKeyFromKey(args.Key == Key.System ? args.SystemKey : args.Key), (uint)Keyboard.Modifiers);
            args.Handled = true;
        };
        PreviewKeyUp += (_, args) => _viewModel.HandleKeyRelease(KeyInterop.VirtualKeyFromKey(args.Key == Key.System ? args.SystemKey : args.Key));
        Deactivated += (_, _) => _viewModel.CancelCapture();
        SourceInitialized += (_, _) => _viewModel.AttachHotkeys(new WindowInteropHelper(this).Handle);
        Closed += (_, _) => _viewModel.Dispose();
    }
}
