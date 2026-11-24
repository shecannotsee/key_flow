using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace key_flow_app;

public partial class ProfileEditorWindow : Window
{
    public ProfileEditorWindow()
    {
        InitializeComponent();
        AddHandler(System.Windows.Controls.Validation.ErrorEvent,
            new EventHandler<System.Windows.Controls.ValidationErrorEventArgs>((_, e) =>
                (DataContext as MainViewModel)?.ChangeValidationErrors(
                    e.Action == System.Windows.Controls.ValidationErrorEventAction.Added ? 1 : -1)));
        PreviewKeyDown += (_, e) =>
        {
            if (DataContext is not MainViewModel vm || vm.CaptureText.Length == 0) return;
            if (!e.IsRepeat) vm.HandleKeyCode(KeyInterop.VirtualKeyFromKey(e.Key == Key.System ? e.SystemKey : e.Key), (uint)Keyboard.Modifiers);
            e.Handled = true;
        };
        PreviewKeyUp += (_, e) => (DataContext as MainViewModel)?.HandleKeyRelease(KeyInterop.VirtualKeyFromKey(e.Key == Key.System ? e.SystemKey : e.Key));
        Deactivated += (_, _) => (DataContext as MainViewModel)?.CancelCapture();
        Closed += (_, _) => (DataContext as MainViewModel)?.EditorClosed(this);
    }
}
