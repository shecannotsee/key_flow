using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;
using key_flow_app;
using Xunit;

public sealed class ProfileEditorWindowTests
{
    [Fact]
    public void EditorLoadsReadOnlyRadioBindingsWithoutCrashing()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Application? app = null;
            ProfileEditorWindow? window = null;
            try
            {
                app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/key_flow;component/Themes/AppleTheme.xaml", UriKind.Relative)
                });
                window = new ProfileEditorWindow
                {
                    DataContext = new EditorState(),
                    Left = -10000,
                    Top = -10000,
                    Opacity = 0,
                    ShowInTaskbar = false
                };
                window.Show();
                window.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));

                var stateButtons = FindLogicalChildren<RadioButton>(window)
                    .Where(button => button.Content?.ToString() is "单键重复" or "多个键顺序执行" or "持续重复，直到停止" or "限时重复，到时间自动停止")
                    .ToArray();
                Assert.Equal(4, stateButtons.Length);
                Assert.All(stateButtons, button => Assert.Equal(BindingMode.OneWay,
                    BindingOperations.GetBinding(button, ToggleButton.IsCheckedProperty)?.Mode));
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                window?.Close();
                app?.Shutdown();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Editor WPF smoke test timed out");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static IEnumerable<T> FindLogicalChildren<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match) yield return match;
            foreach (var descendant in FindLogicalChildren<T>(child)) yield return descendant;
        }
    }

    private sealed class EditorState
    {
        public bool IsSingleKeyMode => true;
        public bool IsSequenceMode => false;
        public bool IsUnlimitedMode => true;
        public bool IsTimedMode => false;
    }
}
