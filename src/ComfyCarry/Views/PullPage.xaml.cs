using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ComfyCarry.Models;
using ComfyCarry.Services;

namespace ComfyCarry.Views;

public sealed partial class PullPage : Page
{
    private LocalizationService L => App.Hub.Locale;

    public PullPage()
    {
        this.InitializeComponent();
        App.Hub.Rules.AttachUi(DispatcherQueue);
        App.Hub.Rules.StateChanged += () => DispatcherQueue.TryEnqueue(RefreshProgress);
        App.Hub.RuleStore.Changed += () => DispatcherQueue.TryEnqueue(RefreshRules);
        App.Hub.Instances.Changed += () => DispatcherQueue.TryEnqueue(RefreshAll);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Localize();
        RefreshAll();
    }

    private void Localize()
    {
        TitleText.Text = L.T("pull.title");
        SubtitleText.Text = L.T("pull.subtitle");
        EmptyTitle.Text = L.T("pull.empty.title");
        EmptyDesc.Text = L.T("pull.empty.desc");
        EmptyConnectBtn.Content = L.T("pull.empty.btn");
        ReconnectBtn.Content = L.T("pull.connect.reconnect");
        ProgressHeader.Text = L.T("pull.progress.title");
        ProgressIdleText.Text = L.T("pull.progress.idle");
        ProgressIdleDesc.Text = L.T("pull.progress.idle.desc");
        CancelSyncBtn.Content = L.T("pull.progress.cancel");
        RetrySyncBtn.Content = L.T("pull.progress.retry");
        RulesHeader.Text = L.T("pull.rules");
        NewRuleLabel.Text = L.T("pull.rule.new");
        NoRulesText.Text = L.T("pull.noRules");
    }

    private void RefreshAll()
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

        // 实例信息
        InstanceLabel.Text = string.IsNullOrEmpty(inst.Label) ? inst.BaseUrl : inst.Label;
        bool connected = !string.IsNullOrEmpty(inst.ApiKey);
        StatusDot.Fill = connected
            ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.LimeGreen)
            : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);
        DavHostText.Text = connected && !string.IsNullOrEmpty(inst.DavUrl) ? inst.DavUrl : "";

        RefreshProgress();
        RefreshRules();
    }

    private void RefreshProgress()
    {
        // 实例可能未连接
        if (App.Hub.Instances.Current is null) return;

        // 进度面板
        var status = App.Hub.Rules.Status;
        ProgressIdle.Visibility = Visibility.Collapsed;
        ProgressSyncing.Visibility = Visibility.Collapsed;
        ProgressFailed.Visibility = Visibility.Collapsed;

        if (status == "syncing" && App.Hub.Rules.ActiveRule is { } activeRule)
        {
            ProgressSyncing.Visibility = Visibility.Visible;
            SyncRuleName.Text = string.IsNullOrEmpty(activeRule.Name) ? L.T("pull.rule.unnamed") : activeRule.Name;
            SyncFileName.Text = App.Hub.Rules.ActiveFile;
            SyncProgressBar.Value = App.Hub.Rules.ProgressPct;
            SyncPctText.Text = $"{App.Hub.Rules.ProgressPct}%";
            var filesText = string.Format(L.T("pull.progress.files"), App.Hub.Rules.FilesCompleted);
            if (App.Hub.Rules.QueueTotal > 1)
                filesText += " · " + string.Format(L.T("pull.progress.queue"), App.Hub.Rules.QueueIndex, App.Hub.Rules.QueueTotal);
            SyncFilesText.Text = filesText;
            SyncSpeedText.Text = FormatSpeed(App.Hub.Rules.ActiveSpeed);
        }
        else if (status == "error")
        {
            ProgressFailed.Visibility = Visibility.Visible;
            var ruleName = App.Hub.Rules.ActiveRule is { } ar && !string.IsNullOrEmpty(ar.Name)
                ? ar.Name : L.T("pull.rule.unnamed");
            FailedRuleName.Text = $"{ruleName} · {L.T("pull.progress.failed")}";
            FailedErrorText.Text = App.Hub.Rules.LastError ?? "";
        }
        else
        {
            ProgressIdle.Visibility = Visibility.Visible;
        }
    }

    private void RefreshRules()
    {
        App.Hub.Rules.Refresh();
        var vms = App.Hub.Rules.Rules.Select(r => new PullRuleVM(r, L)).ToList();
        RulesList.ItemsSource = vms;
        NoRulesText.Visibility = vms.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string FormatSpeed(long bytesPerSec)
    {
        if (bytesPerSec <= 0) return "";
        if (bytesPerSec < 1024) return $"{bytesPerSec} B/s";
        if (bytesPerSec < 1024 * 1024) return $"{bytesPerSec / 1024.0:F1} KB/s";
        return $"{bytesPerSec / (1024.0 * 1024):F1} MB/s";
    }

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ConnectDialog { XamlRoot = this.XamlRoot };
        _ = dlg.ShowAsync();
    }

    private void CancelSync_Click(object sender, RoutedEventArgs e)
    {
        App.Hub.Pull.CancelCurrent();
    }

    private void RetrySync_Click(object sender, RoutedEventArgs e)
    {
        var inst = App.Hub.Instances.Current;
        if (inst is null) return;
        var rule = App.Hub.Rules.ActiveRule;
        if (rule is null) return;
        _ = App.Hub.Pull.RunOnceAsync(inst, rule);
    }

    private async void NewRule_Click(object sender, RoutedEventArgs e)
    {
        var inst = App.Hub.Instances.Current;
        if (inst is null) return;
        var rule = new PullRule { Name = "", LocalPath = "", Method = "copy", Content = "images", Subdirs = true, Trigger = "watch", Enabled = true };
        var dlg = new RuleEditDialog(rule, true) { XamlRoot = this.XamlRoot };
        var result = await dlg.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            App.Hub.Rules.SaveRule(rule);
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
            App.Hub.Rules.SaveRule(rule);
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
        _ = App.Hub.Pull.RunOnceAsync(inst, rule).ContinueWith(t =>
        {
            if (t.Result is { ok: false, reason: { } msg } && msg == L.T("pull.error.busy"))
            {
                DispatcherQueue.TryEnqueue(async () =>
                {
                    var dlg = new ContentDialog
                    {
                        Title = L.T("pull.progress.title"),
                        Content = msg,
                        CloseButtonText = L.T("common.ok"),
                        XamlRoot = this.XamlRoot,
                    };
                    await dlg.ShowAsync();
                });
            }
        });
    }

    private async void DeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string ruleId) return;
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
        App.Hub.Rules.DeleteRule(ruleId);
    }

    private void RuleEnabled_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch ts || ts.Tag is not string ruleId) return;
        var rule = App.Hub.RuleStore.All.FirstOrDefault(r => r.RuleId == ruleId);
        if (rule is not null && rule.Enabled != ts.IsOn)
        {
            rule.Enabled = ts.IsOn;
            App.Hub.RuleStore.Upsert(rule);
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
    public string DeleteLabel => _l.T("common.delete");

    public string PathSummary
    {
        get
        {
            var content = _r.Content switch { "images" => _l.T("pull.rule.content.images"), "videos" => _l.T("pull.rule.content.videos"), _ => _l.T("pull.rule.content.all") };
            var sub = _r.Subdirs ? " · " + _l.T("pull.rule.subdirs") : "";
            return $"-> {_r.LocalPath} · {content}{sub}";
        }
    }

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
