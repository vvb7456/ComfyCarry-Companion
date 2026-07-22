using System.Net.NetworkInformation;
using ComfyCarry.Models;

namespace ComfyCarry.Services;

/// <summary>
/// 周期心跳上报（SPEC §2.4）。15~30s 上报 client_id/hostname/version/status/active_rule/progress。
/// </summary>
public sealed class HeartbeatService
{
    private readonly CompanionApiClient _api;
    private readonly InstanceStore _instances;
    private readonly RuleEngine _rules;
    private readonly SettingsService _settings;
    private Timer? _timer;
    private static readonly string Hostname = Environment.MachineName;
    private static readonly string AppVer =
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version is { } v
            ? $"{v.Major}.{v.Minor}.{v.Build}" : "1.0.0";

    public string LastStatus { get; private set; } = "idle";

    public HeartbeatService(CompanionApiClient api, InstanceStore instances, RuleEngine rules, SettingsService settings)
    {
        _api = api;
        _instances = instances;
        _rules = rules;
        _settings = settings;
    }

    public void Start()
    {
        var interval = _settings.Data.HeartbeatIntervalSec;
        if (interval < 5) interval = 20;
        _timer = new Timer(Tick, null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(interval));
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private async void Tick(object? _)
    {
        var inst = _instances.Current;
        if (inst is null || string.IsNullOrEmpty(inst.ClientId) || string.IsNullOrEmpty(inst.ApiKey)) return;
        try
        {
            var status = _rules.Status;
            LastStatus = status;
            var hb = new HeartbeatRequest
            {
                ClientId = inst.ClientId,
                Hostname = Hostname,
                AppVersion = AppVer,
                Status = status,
                ActiveRuleId = _rules.ActiveRule?.RuleId,
                Progress = new HeartbeatProgress
                {
                    File = _rules.ActiveFile,
                    Pct = _rules.ProgressPct,
                    Speed = _rules.ActiveSpeed,
                },
            };
            await _api.SendHeartbeatAsync(inst, hb);
        }
        catch { /* 心跳失败静默 */ }
    }
}
