using System.Runtime.InteropServices;
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
        SetupTitleBar();
        ApplyTheme();
        ApplyLanguage();
        RestoreLastTab();
        App.Hub.Settings.Changed += () => DispatcherQueue.TryEnqueue(() => { ApplyTheme(); ApplyLanguage(); });
        App.Hub.Instances.Changed += () => DispatcherQueue.TryEnqueue(() => { });

        // 关窗到托盘（SPEC §3.1/§3.2）
        try
        {
            if (AppWindow is not null)
            {
                AppWindow.Closing += OnWindowClosing;
                AppWindow.Changed += OnWindowChanged;
            }
        }
        catch { /* ignore */ }
    }

    private void SetupTitleBar()
    {
        try
        {
            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(AppTitleBar);
            if (AppWindow?.TitleBar is { } tb)
            {
                tb.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
                tb.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
            }
        }
        catch { /* ignore */ }
    }

    /// <summary>恢复上次选中的 Tab（SPEC §3.2 设置中 LastTab）。</summary>
    private void RestoreLastTab()
    {
        var tag = App.Hub.Settings.Data.LastTab;
        var items = Nav.MenuItems.OfType<NavigationViewItem>().ToList();
        var target = items.FirstOrDefault(i => i.Tag is string t && t == tag) ?? items.FirstOrDefault();
        if (target is not null)
        {
            Nav.SelectedItem = target;
            if (target.Tag is string t2)
            {
                Type page = t2 switch
                {
                    "cloud" => typeof(CloudSetupPage),
                    "pull" => typeof(PullPage),
                    "settings" => typeof(SettingsPage),
                    _ => typeof(CloudSetupPage),
                };
                ContentFrame.Navigate(page);
            }
        }
        else
        {
            Nav.SelectedItem = items.FirstOrDefault();
            ContentFrame.Navigate(typeof(CloudSetupPage));
        }
    }

    private void OnWindowClosing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (App.Hub.Settings.Data.CloseToTray && App.Hub.Tray is not null)
        {
            args.Cancel = true;
            sender.Hide();
        }
    }

    /// <summary>最小化到托盘（SPEC §3.2）。</summary>
    private void OnWindowChanged(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowChangedEventArgs args)
    {
        if (args.DidPresenterChange && App.Hub.Settings.Data.MinimizeToTray && App.Hub.Tray is not null)
        {
            if (sender.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized })
            {
                sender.Hide();
            }
        }
    }

    public void RestoreAndActivate()
    {
        try
        {
            AppWindow?.Show();
            if (AppWindow?.Presenter is Microsoft.UI.Windowing.OverlappedPresenter op
                && op.State == Microsoft.UI.Windowing.OverlappedPresenterState.Minimized)
            {
                op.Restore();
            }
        }
        catch { }
        this.Activate();
    }

    private static readonly string PlacementFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ComfyCarry", "placement.json");

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    public void RestoreWindowPlacement()
    {
        try
        {
            // 目标逻辑尺寸 1060x700 DIP，最小 920x620 DIP，按窗口 DPI 换算物理像素
            double scale = 1.0;
            try
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                var dpi = GetDpiForWindow(hwnd);
                if (dpi > 0) scale = dpi / 96.0;
            }
            catch { /* ignore */ }
            if (scale <= 0) scale = 1.0;

            int defW = (int)(1060 * scale);
            int defH = (int)(700 * scale);
            int minW = (int)(920 * scale);
            int minH = (int)(620 * scale);

            if (AppWindow?.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            {
                presenter.PreferredMinimumWidth = minW;
                presenter.PreferredMinimumHeight = minH;
            }

            WindowPlacement? p = null;
            if (File.Exists(PlacementFile))
                p = System.Text.Json.JsonSerializer.Deserialize<WindowPlacement>(File.ReadAllText(PlacementFile));

            if (p is not null && p.W >= 600 && p.H >= 600)
            {
                AppWindow?.MoveAndResize(new Windows.Graphics.RectInt32(p.X, p.Y, p.W, p.H));
            }
            else if (AppWindow is not null)
            {
                var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
                var wa = area.WorkArea;
                int x = wa.X + System.Math.Max(0, (wa.Width - defW) / 2);
                int y = wa.Y + System.Math.Max(0, (wa.Height - defH) / 2);
                AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, defW, defH));
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
            ContentFrame.Navigate(page);
            App.Hub.Settings.Update(s => s.LastTab = tag);
        }
    }

    public void ApplyTheme()
    {
        var t = App.Hub.Settings.Data.Theme;
        var theme = t switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        if (this.Content is FrameworkElement root) root.RequestedTheme = theme;
        Nav.RequestedTheme = theme;
        ApplyTitleBarColors(t);
    }

    /// <summary>根据主题设置标题栏按钮前景色，确保浅色/深色模式下三键可见。</summary>
    private void ApplyTitleBarColors(AppTheme theme)
    {
        try
        {
            if (AppWindow?.TitleBar is not { } tb) return;
            tb.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            tb.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;

            bool isDark = theme switch
            {
                AppTheme.Dark => true,
                AppTheme.Light => false,
                _ => IsSystemDark(),
            };

            var fg = isDark
                ? Windows.UI.Color.FromArgb(255, 255, 255, 255)
                : Windows.UI.Color.FromArgb(255, 0, 0, 0);
            tb.ButtonForegroundColor = fg;
            tb.ButtonInactiveForegroundColor = fg;
            tb.ButtonHoverForegroundColor = fg;
            tb.ButtonPressedForegroundColor = fg;
        }
        catch { /* ignore */ }
    }

    private static bool IsSystemDark()
    {
        try
        {
            var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var val = key?.GetValue("AppsUseLightTheme");
            return val is int i && i == 0;
        }
        catch { return true; }
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
        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var vs = ver is null ? "" : $" v{ver.Major}.{ver.Minor}.{ver.Build}";
        AppTitleText.Text = L.T("app.title") + vs;
        Title = L.T("app.title") + vs;
    }
}

internal sealed class WindowPlacement
{
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }
}
