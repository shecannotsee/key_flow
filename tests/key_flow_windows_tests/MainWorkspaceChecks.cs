using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using key_flow_app;
using key_flow_app.Controls;
using key_flow_core;
using Xunit;

internal static class MainWorkspaceChecks
{
    public static void Run()
    {
        var profiles = Enumerable.Range(1, 80).Select(i => new KeyProfile
        {
            Name = $"我的配置 {i:00}" + (i == 2 ? " · 长名称配置用于检查文字截断和提示" : ""),
            StartHotkey = i % 2 == 0 ? "Shift+1" : "F8", IsInActivationPool = i <= 9
        }).ToList();
        var groups = Enumerable.Range(1, 14).Select(i => new ProfileGroup
        {
            Name = $"工作流程组 {i:00}", Participates = i == 1,
            MemberIds = profiles.Skip((i - 1) * 5).Take(5).Select(p => p.Id).ToList()
        }).ToList();
        var state = new WorkspaceState(profiles, groups);
        var control = new MainWorkspace { DataContext = state };
        var host = new Window { Content = control, Width = 980, Height = 720, Left = -10000, Top = -10000, ShowInTaskbar = false };
        try
        {
            host.Show(); Pump(host);
            var list = (ListBox)control.FindName("ProfilesList");
            Assert.Equal(80, list.Items.Count);
            Assert.Same(profiles[0], ((MainProfileRow)list.SelectedItem).Profile);
            var start = (Button)control.FindName("StartButton");
            var stop = (Button)control.FindName("StopButton");
            Assert.Same(state.ArmCommand, start.Command);
            Assert.Same(state.StopCommand, stop.Command);
            var invoke = (IInvokeProvider)new ButtonAutomationPeer(start).GetPattern(PatternInterface.Invoke)!;
            invoke.Invoke(); Pump(host);
            Assert.Equal(1, state.StartCount);

            foreach (var size in new[] { new Size(980, 720), new Size(760, 560) })
            {
                host.Width = size.Width; host.Height = size.Height; Pump(host);
                Assert.True(list.ActualHeight >= 140, $"List too short at {size}: {list.ActualHeight}");
                var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                Assert.Equal(40, row.ActualHeight);
                Assert.Null(list.ItemContainerGenerator.ContainerFromIndex(79)); // Virtualized, not 80 full visual rows.
                var before = list.TranslatePoint(new Point(), control);
                var beforeSize = list.RenderSize;
                state.IsRunning = true; state.Notify(); Pump(host);
                Assert.Equal(before, list.TranslatePoint(new Point(), control));
                Assert.Equal(beforeSize, list.RenderSize);
                Save(control, $"main-running-{size.Width}.png");
                AssertInside(control, start); AssertInside(control, stop); AssertInside(control, list);
                state.IsRunning = false; state.Notify(); Pump(host);
                Save(control, $"main-stopped-{size.Width}.png");
            }
            var search = (TextBox)control.FindName("SearchBox");
            search.Text = "配置 07"; Pump(host);
            Assert.Single(list.Items);
            Assert.Equal(profiles[6], ((MainProfileRow)list.Items[0]).Profile);
            search.Text = "no-match"; Pump(host); Assert.Empty(list.Items);
            Save(control, "main-search-empty.png");
            search.Text = ""; Pump(host);
            var groupList = (ListBox)control.FindName("GroupsList");
            var newGroup = (Button)control.FindName("NewGroupButton");
            Assert.Same(state.NewGroupCommand, newGroup.Command);
            Assert.Equal("＋ 新建", newGroup.Content);
            groupList.SelectedItem = groups[2]; Pump(host);
            Assert.Equal(5, list.Items.Count);
            Assert.All(list.Items.Cast<MainProfileRow>(), row => Assert.Contains(row.Profile.Id, groups[2].MemberIds));
            var groupButton = Descendants<Button>(control).Single(b => ReferenceEquals(b.Command, state.ToggleGroupCommand));
            Assert.Same(groups[2], groupButton.CommandParameter);
            var deleteGroup = (Button)control.FindName("DeleteGroupButton");
            Assert.Same(state.DeleteGroupCommand, deleteGroup.Command);
            Assert.Same(groups[2], deleteGroup.CommandParameter);
            Save(control, "main-group.png");
            state.IsMonitoring = true; state.Notify(); Pump(host); Save(control, "main-monitoring.png");
            var empty = new WorkspaceState([], []);
            control.DataContext = empty; Pump(host);
            Assert.Empty(list.Items);
            Assert.False(start.IsEnabled);
            Save(control, "main-empty.png");
        }
        finally { host.Close(); }
    }

    private static void AssertInside(FrameworkElement root, FrameworkElement element)
    {
        var rect = element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
        Assert.True(rect.Left >= 0 && rect.Top >= 0 && rect.Right <= root.ActualWidth + 1 && rect.Bottom <= root.ActualHeight + 1, $"Clipped {element.Name}: {rect}");
    }
    private static void Pump(Window host)
    {
        host.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
        host.UpdateLayout();
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match) yield return match;
            foreach (var value in Descendants<T>(child)) yield return value;
        }
    }
    private static void Save(FrameworkElement visual, string name)
    {
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "key_flow.sln"))) repo = repo.Parent;
        if (repo is null) return;
        var directory = Path.Combine(repo.FullName, ".artifacts", "ui-review"); Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)visual.ActualWidth, (int)visual.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, name)); encoder.Save(output);
    }

    private sealed class WorkspaceState : INotifyPropertyChanged
    {
        public WorkspaceState(List<KeyProfile> profiles, List<ProfileGroup> groups)
        {
            Profiles = profiles; Groups = groups; MainSelectedProfile = profiles.FirstOrDefault();
            ArmCommand = new RelayCommand(_ => StartCount++);
            ShowAllProfilesCommand = new RelayCommand(_ => MainGroup = null);
        }
        public List<KeyProfile> Profiles { get; }
        public List<ProfileGroup> Groups { get; }
        private ProfileGroup? group;
        public ProfileGroup? MainGroup { get => group; set { group = value; Notify(); } }
        private string search = "";
        public string MainSearch { get => search; set { search = value; Notify(); } }
        public KeyProfile? MainSelectedProfile { get; set; }
        public MainProfileRow? MainSelectedRow
        {
            get => MainRows.FirstOrDefault(row => row.Profile == MainSelectedProfile);
            set { if (value is not null && value.Profile != MainSelectedProfile) { MainSelectedProfile = value.Profile; Notify(); } }
        }
        public KeyProfile? SelectedProfile => MainSelectedProfile;
        public string SelectedProfileLabel => SelectedProfile?.Name ?? "未选择配置";
        public string MainSelectionLabel => SelectedProfileLabel;
        private readonly Dictionary<KeyProfile, MainProfileRow> rows = [];
        public IEnumerable<MainProfileRow> MainRows => MainProfileRow.Filter(Profiles, Groups, MainGroup, MainSearch).Select(p =>
        {
            if (!rows.TryGetValue(p, out var row)) rows[p] = row = new MainProfileRow(p, ProfileParticipation.Includes(p, Groups), "空闲");
            row.Refresh(ProfileParticipation.Includes(p, Groups), IsRunning && p == Profiles[0] ? "执行 / 排队" : "空闲");
            return row;
        }).ToList();
        public IEnumerable<MainProfileRow> MainParticipants => MainRows.Where(p => p.Participates);
        public bool HasParticipatingProfiles => MainParticipants.Any();
        public bool MainHasSelection => SelectedProfile is not null;
        public bool MainAllSelected => MainGroup is null;
        public bool MainHasGroup => MainGroup is not null;
        public string MainScopeTitle => MainGroup?.Name ?? "全部配置";
        public string MainGroupAction => "激活此组";
        public string MainProfileAction => "单独激活";
        public string MainState => IsRunning ? "运行中" : IsMonitoring ? "颜色监听中" : "已停止";
        public string MainActivity => IsRunning ? "正在执行：我的配置 01" : IsMonitoring ? "等待颜色条件满足" : "当前没有方案执行";
        public string MainCounts => $"{MainParticipants.Count()} 个已激活 · {(IsRunning ? 1 : 0)} 个执行或排队";
        public string MainEmptyText => Profiles.Count == 0 ? "还没有配置，点击“新建配置”开始" : "没有匹配的配置，试试其他关键词或分组";
        public string LastMessage => "准备完成，可以编辑配置或开始执行";
        public string HotkeyStatus => "正在监听方案快捷键 · Ctrl+Shift+F10 紧急停止 · 按键继续传递";
        public bool MainHasError => false;
        public bool IsRunning { get; set; }
        public bool IsMonitoring { get; set; }
        public bool IsPinned { get; set; }
        public int StartCount { get; private set; }
        public ICommand ArmCommand { get; }
        public ICommand ShowAllProfilesCommand { get; }
        public ICommand StopCommand { get; } = new RelayCommand(_ => { });
        public ICommand NewGroupCommand { get; } = new RelayCommand(_ => { });
        public ICommand ToggleGroupCommand { get; } = new RelayCommand(_ => { });
        public ICommand EditGroupCommand { get; } = new RelayCommand(_ => { });
        public ICommand DeleteGroupCommand { get; } = new RelayCommand(_ => { });
        public ICommand UseVerifiedPresetCommand { get; } = new RelayCommand(_ => { });
        public ICommand SaveCommand { get; } = new RelayCommand(_ => { });
        public ICommand ImportCommand { get; } = new RelayCommand(_ => { });
        public ICommand ExportCommand { get; } = new RelayCommand(_ => { });
        public ICommand DuplicateProfileCommand { get; } = new RelayCommand(_ => { });
        public ICommand DeleteProfileCommand { get; } = new RelayCommand(_ => { });
        public ICommand OpenEditorCommand { get; } = new RelayCommand(_ => { });
        public ICommand ToggleActivationPoolCommand { get; } = new RelayCommand(_ => { });
        public event PropertyChangedEventHandler? PropertyChanged;
        public void Notify() => PropertyChanged?.Invoke(this, new(null));
    }
}
