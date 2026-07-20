using System.Diagnostics;

namespace ComfyCarry.Services;

/// <summary>
/// 统一装配所有服务，供 UI 与后台引擎共享单例。
/// App 启动时构造并 Start，退出时 Stop。
/// </summary>
public sealed class ServiceHub : IDisposable
{
    public AppPaths Paths { get; }
    public SecretStore Secrets { get; }
    public InstanceStore Instances { get; }
    public SettingsService Settings { get; }
    public RcloneService Rclone { get; }
    public CompanionApiClient Api { get; }
    public RuleEngine Rules { get; }
    public HeartbeatService Heartbeat { get; }
    public JobReporter Jobs { get; }
    public PullEngine Pull { get; }
    public TrayController Tray { get; internal set; } = null!;
    public LocalizationService Locale { get; }

    private readonly CancellationTokenSource _appCts = new();

    public ServiceHub()
    {
        Paths = new AppPaths();
        Secrets = new SecretStore();
        Settings = new SettingsService(Paths);
        Locale = new LocalizationService(Settings);
        Instances = new InstanceStore(Paths, Secrets);
        Rclone = new RcloneService(Paths);
        Api = new CompanionApiClient(Instances);
        Jobs = new JobReporter(Api);
        Rules = new RuleEngine(Api, Jobs);
        Heartbeat = new HeartbeatService(Api, Instances, Rules, Settings);
        Pull = new PullEngine(Rclone, Rules, Jobs, Instances, Paths, Settings, _appCts.Token);
    }

    public void Start()
    {
        try
        {
            Paths.EnsureCreated();
            Instances.Load();
            Settings.Load();
            Heartbeat.Start();
            Pull.Start();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ServiceHub] Start failed: {ex}");
        }
    }

    public void Stop()
    {
        try
        {
            Pull.Stop();
            Heartbeat.Stop();
            _appCts.Cancel();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ServiceHub] Stop failed: {ex}");
        }
    }

    public void Dispose()
    {
        Stop();
        Api.Dispose();
    }
}
