using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;
using key_flow_app;
using key_flow_app.Controls;
using key_flow_core;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
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
                VerifySelectorGroupingAndSelection();
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

    private static void VerifySelectorGroupingAndSelection()
    {
        var first = new KeyProfile { Name = "我的方案 1", GroupName = "日常", IsInActivationPool = true };
        var second = new KeyProfile { Name = "我的方案 2", GroupName = "日常" };
        var third = new KeyProfile { Name = "我的方案 3", GroupName = "其他", IsInActivationPool = true };
        var state = new SelectorState { Profiles = [first, second, third], SelectedProfile = second };
        var selector = new ProfileSelector { DataContext = state, Margin = new Thickness(20) };
        var surface = new Border { Background = Brushes.White, Child = selector };
        var host = new Window { Content = surface, Width = 660, Height = 760, Left = -10000, Top = -10000, ShowInTaskbar = false, Opacity = 0 };
        try
        {
            host.Show(); host.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
            var list = FindLogicalChildren<ListBox>(selector).Single();
            Assert.Equal(2, list.Items.Groups!.Count);
            Assert.Same(second, list.SelectedItem);
            Assert.Empty(FindLogicalChildren<TextBox>(selector)); // Rename belongs to the editor.
            second.GroupName = "其他";
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
            var other = list.Items.Groups.Cast<CollectionViewGroup>().Single(group => (string)group.Name == "其他");
            Assert.Contains(second, other.Items.Cast<object>());
            Assert.Equal(2, other.ItemCount);
            Assert.Same(second, list.SelectedItem);
            Assert.False(second.IsInActivationPool); // Selection/regrouping never opts a profile into triggers.
            host.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)surface.ActualWidth, (int)surface.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(surface);
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "key_flow.sln"))) root = root.Parent;
            if (root is not null)
            {
                var directory = Path.Combine(root.FullName, ".artifacts", "ui-review");
                Directory.CreateDirectory(directory);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(directory, "profile-selector.png")); encoder.Save(file);
            }
        }
        finally { host.Close(); }
    }

    private sealed class SelectorState
    {
        public List<KeyProfile> Profiles { get; init; } = [];
        public KeyProfile SelectedProfile { get; set; } = new();
        public IEnumerable<KeyProfile> ParticipatingProfiles => Profiles.Where(profile => profile.IsInActivationPool);
        public bool HasParticipatingProfiles => ParticipatingProfiles.Any();
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
