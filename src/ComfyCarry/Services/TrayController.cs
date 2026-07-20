using System.Diagnostics;
using H.NotifyIcon;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ComfyCarry.Services;

/// <summary>
/// 系统托盘常驻（SPEC §3.1/§3.2）。关窗最小化到托盘，后台继续拉取。
/// 菜单：显示主窗口 / 暂停·继续 / 退出。
/// 使用 H.NotifyIcon.Windowless（WinUI 3 代码创建，无需 XAML）。
/// </summary>
public sealed class TrayController : IDisposable
{
    private readonly TaskbarIcon _icon;
    private readonly LocalizationService L;
    private readonly Microsoft.UI.Xaml.Controls.MenuFlyoutItem _pauseItem;
    private bool _paused;
    private string _statusKey = "tray.status.idle";

    public bool Paused => _paused;

    public TrayController(LocalizationService locale)
    {
        L = locale;
        _pauseItem = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem { Text = L.T("tray.pause") };
        _pauseItem.Click += (s, e) => TogglePause();

        _icon = new TaskbarIcon
        {
            ToolTipText = L.T("app.title"),
            // 占位图标：打包时把 app.ico 放 Assets 目录，这里指向它。
            // 缺失时 H.NotifyIcon 用默认占位，不阻塞功能。
            IconSource = "Assets/app.ico",
            ContextMenuMode = ContextMenuMode.SecondWindow,
            LeftClickCommand = new RelayCommand(ShowMainWindow),
        };
        _icon.ContextFlyout = BuildMenu();
        try { _icon.ForceCreate(); } catch (Exception ex) { Debug.WriteLine($"[Tray] create: {ex}"); }
    }

    private Microsoft.UI.Xaml.Controls.MenuFlyout BuildMenu()
    {
        var menu = new Microsoft.UI.Xaml.Controls.MenuFlyout();
        var miShow = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem { Text = L.T("tray.show") };
        miShow.Click += (s, e) => ShowMainWindow();
        var miExit = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem { Text = L.T("tray.exit") };
        miExit.Click += (s, e) => { Dispose(); Application.Current.Exit(); };
        menu.Items.Add(miShow);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new Microsoft.UI.Xaml.Controls.MenuFlyoutSeparator());
        menu.Items.Add(miExit);
        return menu;
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

