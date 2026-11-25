using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace key_flow_app.Controls;

public partial class MainWorkspace : UserControl
{
    public MainWorkspace() => InitializeComponent();

    // Business selection is locked while executing. Keep browsing/scrolling enabled,
    // but never let visual selection imply that a different configuration is being edited.
    private void ProfileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainViewModel model || model.CanEdit) return;
        Dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(() =>
            ProfilesList.SetCurrentValue(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, model.MainSelectedRow)));
    }
    private void ProfilePreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not MainViewModel model) return;
        if (model.OpenEditorCommand.CanExecute(null)) model.OpenEditorCommand.Execute(null);
        e.Handled = true;
    }
}
