using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using H.NotifyIcon;
using Microsoft.UI.Xaml;

namespace ComfyCarry.Services;

/// <summary>
/// 系统托盘常驻（SPEC §3.1/§3.2）。关窗最小化到托盘，后台继续拉取。
/// 菜单：显示主窗口 / 暂停·继续 / 退出。
/// 右键菜单使用 Win32 原生 PopupMenu（不依赖 XAML Island，窗口隐藏时点击仍可响应）。
/// </summary>
public sealed class TrayController : IDisposable
{
    private readonly TaskbarIcon _icon;
    private readonly LocalizationService L;
    private bool _paused;
    private string _statusKey = "tray.status.idle";

    // Win32 菜单命令 ID
    private const uint IDM_SHOW = 40001;
    private const uint IDM_PAUSE = 40002;
    private const uint IDM_EXIT = 40003;

    public bool Paused => _paused;

    public TrayController(LocalizationService locale)
    {
        L = locale;

        _icon = new TaskbarIcon
        {
            ToolTipText = L.T("app.title"),
            NoLeftClickDelay = true,
            LeftClickCommand = new RelayCommand(() => { Log("left-click"); ShowMainWindow(); }),
            RightClickCommand = new RelayCommand(() => { Log("right-click"); ShowNativeMenu(); }),
        };

        // 图标：优先从 exe 提取嵌入图标，兜底用 app.ico
        try
        {
            var exePath = Environment.ProcessPath;
            if (exePath is not null && File.Exists(exePath))
            {
                _icon.Icon = Icon.ExtractAssociatedIcon(exePath) ?? LoadFallbackIcon();
            }
            else
            {
                _icon.Icon = LoadFallbackIcon();
            }
        }
        catch { _icon.Icon = LoadFallbackIcon(); }

        try { _icon.ForceCreate(); Log("icon ForceCreate ok"); }
        catch (Exception ex) { Debug.WriteLine($"[Tray] create: {ex}"); Log($"icon ForceCreate FAILED: {ex.Message}"); }
    }

    private static Icon? LoadFallbackIcon()
    {
        try
        {
            var icoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            return File.Exists(icoPath) ? new Icon(icoPath) : null;
        }
        catch { return null; }
    }

    // ─── Win32 原生右键菜单 ───────────────────────────────────────────

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const uint MF_STRING = 0x0000;
    private const uint MF_SEPARATOR = 0x0800;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_RIGHTALIGN = 0x0008;
    private const uint TPM_BOTTOMALIGN = 0x0020;

    private void ShowNativeMenu()
    {
        var hMenu = CreatePopupMenu();
        if (hMenu == IntPtr.Zero) return;

        try
        {
            AppendMenu(hMenu, MF_STRING, (UIntPtr)IDM_SHOW, L.T("tray.show"));
            var pauseText = _paused ? L.T("tray.resume") : L.T("tray.pause");
            AppendMenu(hMenu, MF_STRING, (UIntPtr)IDM_PAUSE, pauseText);
            AppendMenu(hMenu, MF_SEPARATOR, UIntPtr.Zero, "");
            AppendMenu(hMenu, MF_STRING, (UIntPtr)IDM_EXIT, L.T("tray.exit"));

            // 获取光标位置
            GetCursorPos(out var pt);

            // 必须用本应用窗口句柄 + SetForegroundWindow，否则菜单不弹出
            var hwnd = GetAppHwnd();
            if (hwnd != IntPtr.Zero) SetForegroundWindow(hwnd);

            int cmd = TrackPopupMenu(hMenu, TPM_RETURNCMD | TPM_RIGHTALIGN | TPM_BOTTOMALIGN, pt.X, pt.Y, 0, hwnd, IntPtr.Zero);
            Log($"menu cmd={cmd}");

            switch ((uint)cmd)
            {
                case IDM_SHOW: ShowMainWindow(); break;
                case IDM_PAUSE: TogglePause(); break;
                case IDM_EXIT: ExitApp(); break;
            }
        }
        finally
        {
            DestroyMenu(hMenu);
        }
    }

    private static IntPtr GetAppHwnd()
    {
        try
        {
            if (App.MainWindow is not null)
                return WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        }
        catch { }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    // ─── 通用逻辑 ─────────────────────────────────────────────────────

    private static void Log(string msg)
    {
        try
        {
            var f = App.Hub.Paths.LogFile;
            Directory.CreateDirectory(Path.GetDirectoryName(f)!);
            File.AppendAllText(f, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [tray] {msg}{Environment.NewLine}");
        }
        catch { /* ignore */ }
    }

    private void ExitApp()
    {
        var dq = App.MainWindow?.DispatcherQueue;
        Log($"ExitApp: dq={(dq is null ? "null" : "ok")}");
        if (dq is null)
        {
            Environment.Exit(0);
            return;
        }
        bool ok = dq.TryEnqueue(() =>
        {
            try { App.Hub.Stop(); } catch { /* ignore */ }
            Environment.Exit(0);
        });
        if (!ok) Environment.Exit(0);
    }

    private void TogglePause()
    {
        _paused = !_paused;
        App.Hub.Settings.Update(s => s.AutoSync = !_paused);
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
