using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ComfyCarry.Views;

namespace ComfyCarry;

public sealed partial class MainWindow : Window
{
    public Frame Frame => ContentFrame;
    public LocalizationService L => App.Hub.Locale;

    public MainWindow()
    {
        this.InitializeComponent();
        ApplyTheme();
        ApplyLanguage();
        Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First();
        ContentFrame.Navigate(typeof(CloudSetupPage));
        App.Hub.Settings.Changed += () => DispatcherQueue.TryEnqueue(() => { ApplyTheme(); ApplyLanguage(); });
        App.Hub.Instances.Changed += () => DispatcherQueue.TryEnqueue(() => { });

        // 关窗到托盘（SPEC §3.1/§3.2）
        try
        {
            if (AppWindow is not null)
                AppWindow.Closing += OnWindowClosing;
        }
        catch { /* ignore */ }
    }

    private void OnWindowClosing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (App.Hub.Settings.Data.CloseToTray && App.Hub.Tray is not null)
        {
            args.Cancel = true;
            sender.Hide();
        }
    }

    public void RestoreAndActivate()
    {
        try { AppWindow?.Show(); } catch { }
        this.Activate();
    }

    private static readonly string PlacementFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ComfyCarry", "placement.json");

    public void RestoreWindowPlacement()
    {
        try
        {
            if (File.Exists(PlacementFile))
            {
                var json = File.ReadAllText(PlacementFile);
                var p = System.Text.Json.JsonSerializer.Deserialize<WindowPlacement>(json);
                if (p is not null && p.W > 600 && p.H > 400)
                {
                    AppWindow?.MoveAndResize(new Windows.Graphics.RectInt32(p.X, p.Y, p.W, p.H));
                }
            }
        }
        catch { /* ignore */ }
    }

    public void SaveWindowPlacement()
    {
        try
        {
            if (AppWindow is null) return;
            var r = AppWindow.Position;
            var s = AppWindow.Size;
            var p = new WindowPlacement { X = r.X, Y = r.Y, W = s.Width, H = s.Height };
            Directory.CreateDirectory(Path.GetDirectoryName(PlacementFile)!);
            File.WriteAllText(PlacementFile, System.Text.Json.JsonSerializer.Serialize(p));
        }
        catch { /* ignore */ }
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem nvi && nvi.Tag is string tag)
        {
            Type page = tag switch
            {
                "cloud" => typeof(CloudSetupPage),
                "pull" => typeof(PullPage),
                "settings" => typeof(SettingsPage),
                _ => typeof(CloudSetupPage),
            };
            HeaderTitle.Text = tag switch { "cloud" => L.T("nav.cloud"), "pull" => L.T("nav.pull"), _ => L.T("nav.settings") };
            ContentFrame.Navigate(page);
            App.Hub.Settings.Update(s => s.LastTab = tag);
        }
    }

    public void ApplyTheme()
    {
        var t = App.Hub.Settings.Data.Theme;
        RootTheme = t switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
    }

    public ElementTheme RootTheme
    {
        get { if (this.Content is FrameworkElement root) return root.RequestedTheme; return ElementTheme.Default; }
        set { if (this.Content is FrameworkElement root) root.RequestedTheme = value; }
    }

    public void ApplyLanguage()
    {
        NavCloudLabel.Text = L.T("nav.cloud");
        NavPullLabel.Text = L.T("nav.pull");
        NavSettingsLabel.Text = L.T("nav.settings");
        Title = L.T("app.title");
    }
}

internal sealed class WindowPlacement
{
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }
}
