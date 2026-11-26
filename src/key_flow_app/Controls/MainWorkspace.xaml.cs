using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using key_flow_core;

namespace key_flow_app.Controls;

public partial class MainWorkspace : UserControl
{
    private Point _dragStart;
    private Border? _highlightedDropTarget;

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

    private void ProfileDragStart(object sender, MouseButtonEventArgs e)
        => _dragStart = e.GetPosition(ProfilesList);

    private void ProfileDragMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || DataContext is not MainViewModel model || !model.CanEdit) return;
        if (Math.Abs(e.GetPosition(ProfilesList).X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(e.GetPosition(ProfilesList).Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        if (e.OriginalSource is not DependencyObject source) return;
        if (FindAncestor<ToggleButton>(source) is not null || FindAncestor<Button>(source) is not null) return;
        var container = ItemsControl.ContainerFromElement(ProfilesList, source) as ListBoxItem;
        if (container?.DataContext is not MainProfileRow row) return;

        var data = new DataObject(typeof(KeyProfile), row.Profile);
        DragDrop.DoDragDrop(container, data, DragDropEffects.Move);
    }

    private void GroupsListDragOver(object sender, DragEventArgs e)
    {
        ClearDropHighlight();
        if (DataContext is not MainViewModel model || !model.CanEdit || !e.Data.GetDataPresent(typeof(KeyProfile)))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var profile = (KeyProfile)e.Data.GetData(typeof(KeyProfile))!;
        var item = ItemsControl.ContainerFromElement(GroupsList, e.OriginalSource as DependencyObject) as ListBoxItem;
        if (item?.DataContext is not ProfileGroup group || group.MemberIds.Contains(profile.Id))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        _highlightedDropTarget = FindDescendant<Border>(item, border => border.Name == "GroupDropSurface");
        if (_highlightedDropTarget is not null)
        {
            _highlightedDropTarget.Background = new SolidColorBrush(Color.FromRgb(222, 235, 252));
            _highlightedDropTarget.BorderBrush = new SolidColorBrush(Color.FromRgb(144, 183, 235));
        }
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void GroupsListDrop(object sender, DragEventArgs e)
    {
        ClearDropHighlight();
        if (DataContext is not MainViewModel model || !model.CanEdit || !e.Data.GetDataPresent(typeof(KeyProfile))) return;
        var profile = (KeyProfile)e.Data.GetData(typeof(KeyProfile))!;
        var item = ItemsControl.ContainerFromElement(GroupsList, e.OriginalSource as DependencyObject) as ListBoxItem;
        if (item?.DataContext is not ProfileGroup group || group.MemberIds.Contains(profile.Id)) return;

        model.SelectedProfile = profile;
        model.MoveTargetGroup = group;
        if (model.MoveProfileToGroupCommand.CanExecute(group)) model.MoveProfileToGroupCommand.Execute(group);
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void ClearDropHighlight()
    {
        if (_highlightedDropTarget is null) return;
        _highlightedDropTarget.Background = Brushes.Transparent;
        _highlightedDropTarget.BorderBrush = Brushes.Transparent;
        _highlightedDropTarget = null;
    }

    private static T? FindAncestor<T>(DependencyObject source) where T : DependencyObject
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is T match) return match;
        return null;
    }

    private static T? FindDescendant<T>(DependencyObject source, Func<T, bool> predicate) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(source); i++)
        {
            var child = VisualTreeHelper.GetChild(source, i);
            if (child is T match && predicate(match)) return match;
            var nested = FindDescendant(child, predicate);
            if (nested is not null) return nested;
        }
        return null;
    }

}
