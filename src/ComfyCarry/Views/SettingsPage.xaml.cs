using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ComfyCarry.Services;

namespace ComfyCarry.Views;

public sealed partial class SettingsPage : Page
{
    private LocalizationService L => App.Hub.Locale;
    public ObservableCollection<InstanceVM> Instances { get; } = new();

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
        AutoStartSwitch.Header = L.T("settings.autostart");
        CloseToTraySwitch.Header = L.T("tray.show");
        AboutHeader.Text = L.T("settings.about");
        VersionLine.Text = $"{L.T("settings.about.version")}: 1.0.0";
    }

    private void LoadSettings()
    {
        var s = App.Hub.Settings.Data;
        foreach (var rb in LangRadios.Items.OfType<RadioButton>())
            rb.IsChecked = (rb.Tag as string) == s.Language;
        foreach (var rb in ThemeRadios.Items.OfType<RadioButton>())
            rb.IsChecked = (rb.Tag as string) == s.Theme.ToString();
        AutoStartSwitch.IsOn = s.StartWithWindows;
        CloseToTraySwitch.IsOn = s.CloseToTray;
    }

    private void LoadInstances()
    {
        Instances.Clear();
        foreach (var inst in App.Hub.Instances.All)
            Instances.Add(new InstanceVM(inst));
    }

    private void Lang_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LangRadios.SelectedItem is RadioButton rb && rb.Tag is string lang)
            App.Hub.Settings.Update(s => s.Language = lang);
    }

    private void Theme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
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

    private void SetCurrent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string id)
            App.Hub.Instances.SetCurrent(id);
    }

    private void DeleteInstance_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string id)
            App.Hub.Instances.Remove(id);
    }
}

public sealed class InstanceVM
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public InstanceVM(PanelInstance inst)
    {
        Id = inst.Id;
        Label = string.IsNullOrEmpty(inst.Label) ? inst.BaseUrl : inst.Label;
        BaseUrl = inst.BaseUrl;
    }
}
