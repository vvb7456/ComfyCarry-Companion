using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ComfyCarry.Models;
using ComfyCarry.Services;

namespace ComfyCarry.Views;

public sealed partial class PullPage : Page
{
    private LocalizationService L => App.Hub.Locale;
    private bool _suppressSelection;
    private bool _loading;

    public PullPage()
    {
        this.InitializeComponent();
        App.Hub.Rules.AttachUi(DispatcherQueue);
        App.Hub.Rules.StateChanged += () => DispatcherQueue.TryEnqueue(RefreshUI);
        App.Hub.RuleStore.Changed += () => DispatcherQueue.TryEnqueue(RefreshUI);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Localize();
        RefreshUI();
    }

    private void Localize()
    {
        TitleText.Text = L.T("pull.title");
        SubtitleText.Text = L.T("pull.subtitle");
        EmptyTitle.Text = L.T("pull.empty.title");
        EmptyDesc.Text = L.T("pull.empty.desc");
        EmptyConnectBtn.Content = L.T("pull.connect");
        AddInstanceBtn.Content = L.T("pull.addInstance");
        AutoPullSwitch.Header = L.T("pull.autoPull");
        AutoPullDesc.Text = L.T("pull.autoPull.desc");
        RulesHeader.Text = L.T("pull.rules");
        NewRuleLabel.Text = L.T("pull.rule.new");
        NoRulesText.Text = L.T("pull.noRules");
    }

    private void RefreshUI()
    {
        var inst = App.Hub.Instances.Current;
        if (inst is null)
        {
            EmptyState.Visibility = Visibility.Visible;
            MainContent.Visibility = Visibility.Collapsed;
            return;
        }
        EmptyState.Visibility = Visibility.Collapsed;
        MainContent.Visibility = Visibility.Visible;

        // 实例下拉
        _suppressSelection = true;
        var names = App.Hub.Instances.All.Select(i => i.BaseUrl).ToList();
        InstanceBox.ItemsSource = names;
        InstanceBox.SelectedItem = inst.BaseUrl;
        _suppressSelection = false;

        // 连接状态
        bool connected = !string.IsNullOrEmpty(inst.ApiKey);
        StatusDot.Fill = connected
            ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.LimeGreen)
            : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);
        StatusText.Text = connected ? L.T("pull.connect.status.ok") : L.T("pull.connect.status.no");
        DavHostText.Text = connected && !string.IsNullOrEmpty(inst.DavUrl) ? inst.DavUrl : "";

        // 自动取回
        _loading = true;
        AutoPullSwitch.IsOn = !App.Hub.Pull.Paused;
        _loading = false;
        var status = App.Hub.Rules.Status;
        PullStatusText.Text = status == "syncing"
            ? $"{L.T("pull.autoPull.status.syncing")} · {App.Hub.Rules.ActiveFile}"
            : L.T("pull.autoPull.status.idle");

        // 规则列表
        App.Hub.Rules.Refresh(inst);
        var vms = App.Hub.Rules.Rules.Select(r => new PullRuleVM(r, L)).ToList();
        RulesList.ItemsSource = vms;
        NoRulesText.Visibility = vms.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Instance_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection) return;
        if (InstanceBox.SelectedItem is string url)
        {
            var inst = App.Hub.Instances.All.FirstOrDefault(i => i.BaseUrl == url);
            if (inst is not null) App.Hub.Instances.SetCurrent(inst.Id);
            RefreshUI();
        }
    }

    private void AutoPull_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Hub.Pull.Paused = !AutoPullSwitch.IsOn;
    }

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ConnectDialog { XamlRoot = this.XamlRoot };
        _ = dlg.ShowAsync();
    }

    private async void NewRule_Click(object sender, RoutedEventArgs e)
    {
        var inst = App.Hub.Instances.Current;
        if (inst is null) return;
        var rule = new PullRule { Name = "", Source = "", LocalPath = "", Method = "copy", Content = "images", Subdirs = true, Trigger = "watch", Enabled = true };
        var dlg = new RuleEditDialog(rule, true) { XamlRoot = this.XamlRoot };
        var result = await dlg.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            App.Hub.Rules.SaveRule(inst, rule);
        }
    }

    private async void EditRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string ruleId) return;
        var inst = App.Hub.Instances.Current;
        if (inst is null) return;
        var rule = App.Hub.Rules.Rules.FirstOrDefault(r => r.RuleId == ruleId);
        if (rule is null) return;
        var dlg = new RuleEditDialog(rule, false) { XamlRoot = this.XamlRoot };
        var result = await dlg.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            App.Hub.Rules.SaveRule(inst, rule);
        }
    }

    private async void RunOnce_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string ruleId) return;
        var inst = App.Hub.Instances.Current;
        if (inst is null) return;
        var rule = App.Hub.Rules.Rules.FirstOrDefault(r => r.RuleId == ruleId);
        if (rule is null) return;
        if (rule.Method == "move")
        {
            var confirm = new ContentDialog
            {
                Title = L.T("pull.rule.method.move"),
                Content = L.T("pull.rule.move.confirm"),
                PrimaryButtonText = L.T("common.ok"),
                CloseButtonText = L.T("common.cancel"),
                XamlRoot = this.XamlRoot,
            };
            var r = await confirm.ShowAsync();
            if (r != ContentDialogResult.Primary) return;
        }
        _ = App.Hub.Pull.RunOnceAsync(inst, rule);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string path) return;
        if (!string.IsNullOrEmpty(path) && System.IO.Directory.Exists(path))
            _ = Windows.System.Launcher.LaunchFolderPathAsync(path);
    }

    private async void DeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string ruleId) return;
        var inst = App.Hub.Instances.Current;
        if (inst is null) return;
        var confirm = new ContentDialog
        {
            Title = L.T("common.delete"),
            Content = L.T("pull.rule.deleteConfirm"),
            PrimaryButtonText = L.T("common.delete"),
            CloseButtonText = L.T("common.cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        App.Hub.Rules.DeleteRule(inst, ruleId);
    }

    private void RuleEnabled_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox cb || cb.Tag is not string ruleId) return;
        var inst = App.Hub.Instances.Current;
        if (inst is null) return;
        var rule = App.Hub.RuleStore.RulesFor(inst.InstanceLabel).FirstOrDefault(r => r.RuleId == ruleId);
        if (rule is not null)
        {
            rule.Enabled = cb.IsChecked == true;
            App.Hub.RuleStore.Upsert(inst.InstanceLabel, rule);
        }
    }
}

public sealed class PullRuleVM
{
    private readonly PullRule _r;
    private readonly LocalizationService _l;

    public PullRuleVM(PullRule r, LocalizationService l) { _r = r; _l = l; }

    public string RuleId => _r.RuleId;
    public string Name => string.IsNullOrEmpty(_r.Name) ? _l.T("pull.rule.unnamed") : _r.Name;
    public string LocalPath => _r.LocalPath;
    public bool Enabled { get => _r.Enabled; set => _r.Enabled = value; }

    public string MethodLabel => _r.Method == "move" ? _l.T("pull.rule.method.move") : _l.T("pull.rule.method.copy");
    public string TriggerLabel => _r.Trigger == "manual" ? _l.T("pull.rule.trigger.manual") : _l.T("pull.rule.trigger.auto");

    public string RunLabel => _l.T("pull.rule.run");
    public string EditLabel => _l.T("common.edit");
    public string OpenLabel => _l.T("pull.rule.open");
    public string DeleteLabel => _l.T("common.delete");

    public string PathSummary
    {
        get
        {
            var src = string.IsNullOrEmpty(_r.Source) ? "output" : $"output/{_r.Source}";
            var content = _r.Content switch { "images" => _l.T("pull.rule.content.images"), "videos" => _l.T("pull.rule.content.videos"), _ => _l.T("pull.rule.content.all") };
            var sub = _r.Subdirs ? " · " + _l.T("pull.rule.subdirs") : "";
            return $"{src} → {_r.LocalPath} · {content}{sub}";
        }
    }

    public Visibility ProgressVisible => _r.StatusText == "syncing" ? Visibility.Visible : Visibility.Collapsed;
    public int Progress => _r.ProgressPct;
    public string ProgressLabel => $"{_r.ProgressPct}%";

    public string LastRunText
    {
        get
        {
            if (!_r.Enabled) return _l.T("pull.rule.disabled");
            if (_r.LastRunAt is null) return _l.T("pull.rule.neverRun");
            return $"{_l.T("pull.rule.lastRun")} {_r.LastRunAt:HH:mm} · {_r.LastResult}";
        }
    }
}
