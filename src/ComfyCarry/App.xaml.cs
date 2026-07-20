using Microsoft.UI.Xaml;
using ComfyCarry.Services;

namespace ComfyCarry;

public partial class App : Application
{
    private static MainWindow? _mainWindow;

    public static MainWindow MainWindow => _mainWindow!;

    public static ServiceHub Hub { get; } = new();

    private static readonly EventWaitHandle ShowSignal =
        new(false, EventResetMode.AutoReset, "Global\\ComfyCarryCompanion_Show_3F2A");

    public App()
    {
        this.InitializeComponent();
        Hub.Start();
        _ = Task.Run(ShowSignalLoop);
    }

    private static async Task ShowSignalLoop()
    {
        while (true)
        {
            ShowSignal.WaitOne();
            var mw = _mainWindow;
            if (mw is not null)
            {
                var tcs = new TaskCompletionSource();
                mw.DispatcherQueue.TryEnqueue(() =>
                {
                    ShowMainWindow();
                    tcs.SetResult();
                });
                await tcs.Task;
            }
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _mainWindow = new MainWindow();
        _mainWindow.Activate();
        _mainWindow.RestoreWindowPlacement();
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

