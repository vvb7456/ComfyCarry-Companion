using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ComfyCarry.Services;

namespace ComfyCarry.Views;

public sealed partial class PullPage : Page
{
    private LocalizationService L => App.Hub.Locale;
    public ObservableCollection<PullRuleVM> Rules { get; } = new();

    public PullPage()
    {
        this.InitializeComponent();
        RulesList.ItemsSource = Rules;
        Localize();
        LoadInstances();
        App.Hub.Instances.Changed += () => DispatcherQueue.TryEnqueue(LoadInstances);
        App.Hub.Rules.StateChanged += () => DispatcherQueue.TryEnqueue(RefreshRulesUI);
        App.Hub.Settings.Changed += () => DispatcherQueue.TryEnqueue(Localize);
        Loaded += async (_, _) => await RefreshAll();
    }

    private void Localize()
    {
        IntroText.Text = L.T("pull.intro");
        ConnectBtn.Content = L.T("pull.connect");
        RefreshBtn.Content = L.T("common.refresh");
        NewRuleBtn.Content = L.T("pull.rule.new");
        NoInstanceBar.Message = L.T("pull.noInstance");
    }

    private void LoadInstances()
    {
        InstanceBox.Items.Clear();
        foreach (var inst in App.Hub.Instances.All)
        {
            var label = string.IsNullOrEmpty(inst.Label) ? inst.BaseUrl : inst.Label;
            var item = new ComboBoxItem { Content = label, Tag = inst };
            if (inst.IsCurrent) InstanceBox.SelectedItem = item;
            InstanceBox.Items.Add(item);
        }
        NoInstanceBar.IsOpen = App.Hub.Instances.All.Count == 0;
    }

    private void Instance_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (InstanceBox.SelectedItem is ComboBoxItem ci && ci.Tag is PanelInstance inst)
        {
            App.Hub.Instances.SetCurrent(inst.Id);
            _ = RefreshAll();
        }
    }

    private async Task RefreshAll()
    {
        var inst = App.Hub.Instances.Current;
        if (inst is null || string.IsNullOrEmpty(inst.ApiKey))
        {
            NoInstanceBar.IsOpen = true;
            return;
        }
        NoInstanceBar.IsOpen = false;
        await App.Hub.Rules.RefreshAsync(inst);
        RefreshRulesUI();
        await Artifacts.LoadAsync(inst, "");
    }

    private void RefreshRulesUI()
    {
        Rules.Clear();
        foreach (var r in App.Hub.Rules.Rules)
        {
            var vm = new PullRuleVM(r);
            if (App.Hub.Rules.ActiveRule?.RuleId == r.RuleId)
            {
                vm.Progress = App.Hub.Rules.ProgressPct;
                vm.StatusText = App.Hub.Rules.Status;
            }
            Rules.Add(vm);
        }
        var active = App.Hub.Rules.ActiveRule;
        ProgressRow.Visibility = active is null ? Visibility.Collapsed : Visibility.Visible;
        TotalProgress.Value = App.Hub.Rules.ProgressPct;
        ProgressText.Text = App.Hub.Rules.ActiveFile;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAll();

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ConnectDialog();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(dlg, hwnd);
        await dlg.ShowAsync();
        await RefreshAll();
    }

    private async void NewRule_Click(object sender, RoutedEventArgs e)
    {
        var inst = App.Hub.Instances.Current;
        if (inst is null) return;
        var dlg = new RuleEditDialog(new PullRule
        {
            Name = "新规则",
            Method = "copy",
            RemotePath = "",
            LocalPath = "",
            Filters = RuleEngine.DefaultFilters.ToList(),
            Trigger = "watch",
            IntervalSec = 300,
            Enabled = true,
            ClientId = inst.ClientId,
        }, isNew: true);
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(dlg, hwnd);
        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
        {
            await App.Hub.Rules.SaveRuleAsync(inst, dlg.Rule);
            await RefreshAll();
        }
    }

    private async void EditRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string id) return;
        var rule = App.Hub.Rules.Rules.FirstOrDefault(r => r.RuleId == id);
        if (rule is null) return;
        var dlg = new RuleEditDialog(rule, isNew: false);
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(dlg, hwnd);
        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
        {
            var inst = App.Hub.Instances.Current!;
            await App.Hub.Rules.SaveRuleAsync(inst, dlg.Rule);
            await RefreshAll();
        }
    }

    private async void RunOnce_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string id) return;
        var inst = App.Hub.Instances.Current;
        var rule = App.Hub.Rules.Rules.FirstOrDefault(r => r.RuleId == id);
        if (inst is null || rule is null) return;
        if (rule.Method == "move")
        {
            var cd = new ContentDialog
            {
                Title = "确认 move",
                Content = L.T("pull.rule.move.confirm"),
                PrimaryButtonText = L.T("common.ok"),
                CloseButtonText = L.T("common.cancel"),
                XamlRoot = this.XamlRoot,
            };
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(cd, hwnd);
            if (await cd.ShowAsync() != ContentDialogResult.Primary) return;
        }
        _ = App.Hub.Pull.RunOnceAsync(inst, rule);
    }

    private async void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string path || string.IsNullOrEmpty(path)) return;
        try
        {
            if (!System.IO.Directory.Exists(path)) System.IO.Directory.CreateDirectory(path);
            await Windows.System.Launcher.LaunchFolderPathAsync(path);
        }
        catch { /* ignore */ }
    }

    private void Rule_Click(object sender, ItemClickEventArgs e)
    {
        // 占位：点击规则项展开详情（二期）
    }
}

public sealed class PullRuleVM
{
    public string RuleId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Summary { get; set; } = "";
    public string StatusText { get; set; } = "idle";
    public int Progress { get; set; }
    public string LocalPath { get; set; } = "";

    public PullRuleVM(PullRule r)
    {
        RuleId = r.RuleId;
        Name = string.IsNullOrEmpty(r.Name) ? "(未命名)" : r.Name;
        var filters = r.Filters.Count > 0 ? string.Join(" ", r.Filters) : "*";
        Summary = $"{r.Method} · {r.RemotePath} → {r.LocalPath} · [{filters}] · {r.Trigger}/{r.IntervalSec}s";
        StatusText = r.StatusText;
        Progress = r.ProgressPct;
        LocalPath = r.LocalPath;
    }
}
