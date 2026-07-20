using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;
using ComfyCarry.Views;

namespace ComfyCarry;

public sealed partial class MainWindow : Window
{
    public Frame Frame => ContentFrame;
    public LocalizationService L => App.Hub.Locale;

    public MainWindow()
    {
        this.InitializeComponent();
        EnsureSystemSession();
        SetMinSize(960, 640);
        ApplyTheme();
        ApplyLanguage();
        Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First();
        ContentFrame.Navigate(typeof(CloudSetupPage));
        App.Hub.Settings.Changed += () => DispatcherQueue.TryEnqueue(() => { ApplyTheme(); ApplyLanguage(); });
        App.Hub.Instances.Changed += () => DispatcherQueue.TryEnqueue(() => { /* 更新顶部实例提示等 */ });
    }

    private void EnsureSystemSession()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var id = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetWindowFromWindowId(id);
            appWindow.Closing += OnWindowClosing;
        }
        catch { /* 非 WindowsAppSDK 完整环境时忽略 */ }
    }

    private void SetMinSize(int w, int h)
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var id = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetWindowFromWindowId(id);
            appWindow.Resize(new Windows.Graphics.SizeInt32(w, h));
            // Note: AppWindow 没有直接的 MinSize，但 Presenter 可设置
            if (appWindow.Presenter is OverlappedPresenter op)
            {
                op.SetMinSize(w, h);
            }
        }
        catch { /* ignore */ }
    }

    private void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        // 关窗最小化到托盘（SPEC §3.1/§3.2）
        if (App.Hub.Settings.Data.CloseToTray && App.Hub.Tray is not null)
        {
            args.Cancel = true;
            this.Hide();
            App.Hub.Tray.UpdateStatus("tray.status.idle");
        }
    }

    public void RestoreAndActivate()
    {
        this.ShowNormal();
        this.Restore();
        this.BringToFront();
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
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                    var id = Win32Interop.GetWindowIdFromWindow(hwnd);
                    var aw = AppWindow.GetWindowFromWindowId(id);
                    aw.MoveAndResize(new Windows.Graphics.RectInt32(p.X, p.Y, p.W, p.H));
                }
            }
        }
        catch { /* ignore */ }
    }

    public void SaveWindowPlacement()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var id = Win32Interop.GetWindowIdFromWindow(hwnd);
            var aw = AppWindow.GetWindowFromWindowId(id);
            var r = aw.Position;
            var s = aw.Size;
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
            if (!ContentFrame.Navigate(page))
                ContentFrame.Navigate(page);
            App.Hub.Settings.Update(s => s.LastTab = tag);
        }
    }

    public void ApplyTheme()
    {
        var t = App.Hub.Settings.Data.Theme;
        try
        {
            this.SystemBackdrop = new Microsoft.UI.Composition.SystemBackdrops.MicaBackdrop
            {
                Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base
            };
        }
        catch { /* 老版本 Windows 无 Mica 时忽略 */ }
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
