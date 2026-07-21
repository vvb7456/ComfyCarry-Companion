using System.Diagnostics;
using H.NotifyIcon;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ComfyCarry.Services;

/// <summary>
/// 系统托盘常驻（SPEC §3.1/§3.2）。关窗最小化到托盘，后台继续拉取。
/// 菜单：显示主窗口 / 暂停·继续 / 退出。
/// 使用 H.NotifyIcon.WinUI（参照 PigeonPost 的 InitializeTrayIcon 写法）。
/// </summary>
public sealed class TrayController : IDisposable
{
    private readonly TaskbarIcon _icon;
    private readonly LocalizationService L;
    private readonly MenuFlyoutItem _pauseItem;
    private bool _paused;
    private string _statusKey = "tray.status.idle";

    public bool Paused => _paused;

    public TrayController(LocalizationService locale)
    {
        L = locale;
        _pauseItem = new MenuFlyoutItem { Text = L.T("tray.pause") };

        _icon = new TaskbarIcon
        {
            ToolTipText = L.T("app.title"),
            ContextFlyout = BuildMenu(),
            LeftClickCommand = new RelayCommand(ShowMainWindow),
        };

        // 图标：从 exe 提取（csproj 的 ApplicationIcon 设置后，exe 自带图标）
        try
        {
            var exePath = Environment.ProcessPath;
            if (exePath is not null && File.Exists(exePath))
            {
                _icon.Icon = new System.Drawing.Icon(exePath);
            }
        }
        catch { /* 无图标时 H.NotifyIcon 用占位，不阻塞 */ }

        try { _icon.ForceCreate(); }
        catch (Exception ex) { Debug.WriteLine($"[Tray] create: {ex}"); }
    }

    private MenuFlyout BuildMenu()
    {
        var menu = new MenuFlyout();
        var miShow = new MenuFlyoutItem { Text = L.T("tray.show") };
        miShow.Click += (s, e) => ShowMainWindow();
        _pauseItem.Click += (s, e) => TogglePause();
        var miExit = new MenuFlyoutItem { Text = L.T("tray.exit") };
        miExit.Click += (s, e) => ExitApp();
        menu.Items.Add(miShow);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(miExit);
        return menu;
    }

    /// <summary>
    /// 退出应用。切回 UI 线程执行，规避托盘菜单回调的线程问题；
    /// 退出前不 Dispose 图标——菜单仍打开时 Dispose 会与 UI 线程死锁，
    /// 导致 Environment.Exit 到不了、进程退不掉。进程退出后系统会自动清理托盘图标。
    /// </summary>
    private void ExitApp()
    {
        var dq = App.MainWindow?.DispatcherQueue;
        if (dq is null)
        {
            Environment.Exit(0);
            return;
        }
        dq.TryEnqueue(() =>
        {
            try { App.Hub.Stop(); } catch { /* ignore */ }
            Environment.Exit(0);
        });
    }

    private void TogglePause()
    {
        _paused = !_paused;
        App.Hub.Pull.Paused = _paused;
        _pauseItem.Text = _paused ? L.T("tray.resume") : L.T("tray.pause");
        UpdateStatus(_statusKey);
    }

    public void UpdateStatus(string statusKey)
    {
        _statusKey = statusKey;
        var suffix = _paused ? " (paused)" : "";
        _icon.ToolTipText = $"{L.T("app.title")} — {L.T(statusKey)}{suffix}";
    }

    public void ShowMainWindow()
    {
        App.ShowMainWindow();
    }

    public void Dispose()
    {
        try { _icon.Dispose(); } catch { /* ignore */ }
    }
}
