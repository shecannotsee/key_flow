using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace key_flow_app.Controls;

public partial class ProfileSelector : UserControl
{
    public ProfileSelector() => InitializeComponent();

    private void ProfilesSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Live regrouping removes/reinserts the item. Keep its selection, but never restore a deleted item.
        if (sender is not ListBox list || list.SelectedItem is not null || e.RemovedItems.Count != 1) return;
        var previous = e.RemovedItems[0];
        Dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(() =>
        {
            if (list.SelectedItem is null && list.Items.Contains(previous))
                list.SetCurrentValue(Selector.SelectedItemProperty, previous);
        }));
    }
}
