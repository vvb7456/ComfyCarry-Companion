using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ComfyCarry.Services;

namespace ComfyCarry.Views;

public sealed partial class SettingsPage : Page
{
    private LocalizationService L => App.Hub.Locale;
    public ObservableCollection<InstanceVM> Instances { get; } = new();
    private bool _loading;   // 防止回填时触发 Update

    public SettingsPage()
    {
        this.InitializeComponent();
        InstanceList.ItemsSource = Instances;
        Localize();
        LoadSettings();
        LoadInstances();
        App.Hub.Instances.Changed += () => DispatcherQueue.TryEnqueue(LoadInstances);
        App.Hub.Settings.Changed += () => DispatcherQueue.TryEnqueue(() => { Localize(); LoadSettings(); });
    }

    private void Localize()
    {
        Title.Text = L.T("settings.title");
        InstancesHeader.Text = L.T("settings.instances");
        LangHeader.Text = L.T("settings.language");
        ThemeHeader.Text = L.T("settings.theme");
        ThemeSystem.Content = L.T("settings.theme.system");
        ThemeLight.Content = L.T("settings.theme.light");
        ThemeDark.Content = L.T("settings.theme.dark");
        AutoSyncSwitch.Header = L.T("settings.autoSync");
        AutoStartSwitch.Header = L.T("settings.autostart");
        CloseToTraySwitch.Header = L.T("settings.closeToTray");
        MinimizeToTraySwitch.Header = L.T("settings.minimizeToTray");
        SyncParamsHeader.Text = L.T("settings.syncParams");
        WatchIntervalLabel.Text = L.T("settings.watchInterval");
        WatchIntervalHint.Text = L.T("settings.watchInterval.hint");
        MinAgeLabel.Text = L.T("settings.minAge");
        MinAgeHint.Text = L.T("settings.minAge.hint");
        ProxyHeader.Text = L.T("settings.proxy");
        ProxyHint.Text = L.T("settings.proxy.hint");
        AboutHeader.Text = L.T("settings.about");
        AboutDesc.Text = L.T("settings.about.desc");
        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        VersionLine.Text = $"{L.T("settings.about.version")}: {ver?.Major ?? 1}.{ver?.Minor ?? 0}.{ver?.Build ?? 0}";
    }

    private void LoadSettings()
    {
        _loading = true;
        try
        {
            var s = App.Hub.Settings.Data;
            foreach (var rb in LangRadios.Items.OfType<RadioButton>())
                rb.IsChecked = (rb.Tag as string) == s.Language;
            foreach (var rb in ThemeRadios.Items.OfType<RadioButton>())
                rb.IsChecked = (rb.Tag as string) == s.Theme.ToString();
            AutoSyncSwitch.IsOn = s.AutoSync;
            AutoStartSwitch.IsOn = s.StartWithWindows;
            CloseToTraySwitch.IsOn = s.CloseToTray;
            MinimizeToTraySwitch.IsOn = s.MinimizeToTray;
            WatchIntervalBox.Value = s.PullWatchIntervalSec;
            MinAgeBox.Value = s.MinAgeSec;
            ProxyBox.Text = s.Proxy ?? "";
        }
        finally { _loading = false; }
    }

    private void LoadInstances()
    {
        Instances.Clear();
        foreach (var inst in App.Hub.Instances.All)
            Instances.Add(new InstanceVM(inst));
    }

    private void Lang_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (LangRadios.SelectedItem is RadioButton rb && rb.Tag is string lang)
            App.Hub.Settings.Update(s => s.Language = lang);
    }

    private void Theme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (ThemeRadios.SelectedItem is RadioButton rb && rb.Tag is string t)
        {
            App.Hub.Settings.Update(s => s.Theme = t switch
            {
                "Light" => AppTheme.Light,
                "Dark" => AppTheme.Dark,
                _ => AppTheme.System,
            });
            App.MainWindow.ApplyTheme();
        }
    }

    private void AutoSync_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Hub.Settings.Update(s => s.AutoSync = AutoSyncSwitch.IsOn);
    }

    private void WatchInterval_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs e)
    {
        if (_loading) return;
        var v = (int)Math.Round(sender.Value);
        if (v < 5) v = 5;
        App.Hub.Settings.Update(s => s.PullWatchIntervalSec = v);
    }

    private void MinAge_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs e)
    {
        if (_loading) return;
        var v = (int)Math.Max(0, Math.Round(sender.Value));
        App.Hub.Settings.Update(s => s.MinAgeSec = v);
    }

    private void AutoStart_Toggled(object sender, RoutedEventArgs e)
    {
        var on = AutoStartSwitch.IsOn;
        App.Hub.Settings.Update(s => s.StartWithWindows = on);
        try { StartupHelper.SetAutoStart(on); } catch { /* 占位 */ }
    }

    private void CloseToTray_Toggled(object sender, RoutedEventArgs e)
    {
        App.Hub.Settings.Update(s => s.CloseToTray = CloseToTraySwitch.IsOn);
    }

    private void MinimizeToTray_Toggled(object sender, RoutedEventArgs e)
    {
        App.Hub.Settings.Update(s => s.MinimizeToTray = MinimizeToTraySwitch.IsOn);
    }

    private void Proxy_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        App.Hub.Settings.Update(s => s.Proxy = ProxyBox.Text.Trim());
    }

    private void SetCurrent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string id)
            App.Hub.Instances.SetCurrent(id);
    }

    private async void DeleteInstance_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string id) return;
        var confirm = new ContentDialog
        {
            Title = L.T("common.delete"),
            Content = L.T("cloud.home.deleteConfirm"),
            PrimaryButtonText = L.T("common.delete"),
            CloseButtonText = L.T("common.cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        App.Hub.Instances.Remove(id);
    }
}

public sealed class InstanceVM
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string SetCurrentLabel => App.Hub.Locale.T("settings.instance.setCurrent");
    public string DeleteLabel => App.Hub.Locale.T("common.delete");
    public InstanceVM(PanelInstance inst)
    {
        Id = inst.Id;
        Label = string.IsNullOrEmpty(inst.Label) ? inst.BaseUrl : inst.Label;
        BaseUrl = inst.BaseUrl;
    }
}
