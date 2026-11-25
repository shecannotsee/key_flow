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
                foreach (var step in new[] { 0, 1, 2 })
                {
                    window.DataContext = new EditorState { EditorStep = step };
                    window.UpdateLayout();
                    Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
                    window.Opacity = 1;
                    SaveVisual(window, $"editor-{step}.png");
                }
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
        var state = new SelectorState { Profiles = [first, second, third], SelectedProfile = second, Groups = [new ProfileGroup { Name = "日常组合", Participates = true, MemberIds = [first.Id, second.Id] }] };
        var selector = new ProfileSelector { DataContext = state, Margin = new Thickness(20) };
        var surface = new Border { Background = Brushes.White, Child = selector };
        var host = new Window { Content = surface, Width = 660, Height = 760, Left = -10000, Top = -10000, ShowInTaskbar = false, Opacity = 0 };
        try
        {
            host.Show(); host.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
            var list = FindLogicalChildren<ListBox>(selector).Single();
            Assert.Equal(3, list.Items.Count);
            Assert.Same(second, list.SelectedItem);
            var searchBox = FindLogicalChildren<TextBox>(selector).Single();
            Assert.Equal("按方案名或分组搜索", searchBox.ToolTip);
            Assert.False(second.IsInActivationPool);
            var colorSwitch = FindLogicalChildren<CheckBox>(new key_flow_app.Controls.TriggerEditorPage())
                .Single(box => box.Content?.ToString() == "启用颜色变化触发");
            Assert.Equal(Visibility.Visible, colorSwitch.Visibility);
            var group = new ProfileGroup { Name = "配套配置", MemberIds = [first.Id, second.Id] };
            var editor = new GroupEditorWindow(group, state.Profiles);
            try { editor.Show(); editor.UpdateLayout(); } finally { editor.Close(); }
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
        public List<ProfileGroup> Groups { get; init; } = [];
        public bool CanEdit => true;
        public string SelectedProfileLabel => SelectedProfile.Name;
        public IEnumerable<KeyProfile> ParticipatingProfiles => Profiles.Where(profile => ProfileParticipation.Includes(profile, Groups));
        public bool HasParticipatingProfiles => ParticipatingProfiles.Any();
    }

    private static void SaveVisual(FrameworkElement element, string name)
    {
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "key_flow.sln"))) root = root.Parent;
        if (root is null) return;
        var directory = Path.Combine(root.FullName, ".artifacts", "ui-review"); Directory.CreateDirectory(directory);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, name)); encoder.Save(file);
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
        public int EditorStep { get; init; }
        public bool CanEdit => true;
        public bool ResetsOnRetrigger => true;
        public bool IgnoresRetrigger => false;
        public bool UseToggleHotkey { get; set; }
        public KeyProfile SelectedProfile { get; } = new() { Name = "我的方案 1", SequenceMode = true, Steps = [new("E", 80), new("F", 100)], ColorTriggerEnabled = false };

        public bool IsSingleKeyMode => true;
        public bool IsSequenceMode => false;
        public bool IsUnlimitedMode => true;
        public bool IsTimedMode => false;
    }
}
