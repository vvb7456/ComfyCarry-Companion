using System.Threading;
using Microsoft.UI.Xaml;
using ComfyCarry.Services;

namespace ComfyCarry;

public partial class App : Application
{
    private static MainWindow? _mainWindow;
    public static MainWindow MainWindow => _mainWindow!;
    public static ServiceHub Hub { get; } = new();

    private static readonly Mutex SingleMutex = new(false, "Global\\ComfyCarryCompanion_Single_3F2A");

    public App()
    {
        this.InitializeComponent();
        Hub.Start();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // 单实例：已有实例在跑则直接退出（简化版，不做跨进程唤起）
        try
        {
            if (!SingleMutex.WaitOne(0, false))
            {
                // 已有实例，本进程退出
                Environment.Exit(0);
                return;
            }
        }
        catch { /* ignore */ }

        _mainWindow = new MainWindow();
        _mainWindow.Activate();
        _mainWindow.RestoreWindowPlacement();
        _mainWindow.Closed += (s, e) =>
        {
            if (Hub.Settings.Data.CloseToTray && Hub.Tray is not null)
            {
                e.Handled = true; // 阻止关闭，改为隐藏
                try { _mainWindow.GetAppWindow().Hide(); } catch { }
            }
        };
        Hub.Rules.AttachUi(_mainWindow.DispatcherQueue);
        Hub.Tray = new TrayController(Hub.Locale);
        Hub.Rules.StateChanged += () =>
        {
            _mainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                var s = Hub.Rules.Status;
                Hub.Tray.UpdateStatus(s == "syncing" ? "tray.status.syncing" : "tray.status.idle");
            });
        };
    }

    public static void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            _mainWindow = new MainWindow();
            _mainWindow.Activate();
        }
        else
        {
            _mainWindow.RestoreAndActivate();
        }
    }
}
