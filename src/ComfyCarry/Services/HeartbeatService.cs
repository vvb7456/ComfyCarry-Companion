using System.Net.NetworkInformation;
using System.Reflection;
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
    private readonly RuleStore _ruleStore;
    private readonly SettingsService _settings;
    private Timer? _timer;
    private static readonly string Hostname = Environment.MachineName;
    private static readonly string AppVer =
        typeof(HeartbeatService).Assembly.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.1.0-beta";

    public string LastStatus { get; private set; } = "idle";

    public HeartbeatService(CompanionApiClient api, InstanceStore instances, RuleEngine rules, RuleStore ruleStore, SettingsService settings)
    {
        _api = api;
        _instances = instances;
        _rules = rules;
        _ruleStore = ruleStore;
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
        try { await TickAsync(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Heartbeat] tick: {ex}"); }
    }

    private async Task TickAsync()
    {
        var inst = _instances.Current;
        if (inst is null || string.IsNullOrEmpty(inst.ClientId) || string.IsNullOrEmpty(inst.ApiKey)) return;
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
            RuleSummaries = _ruleStore.All.Select(r => new RuleSummary
            {
                Name = r.Name,
                Source = "",
                LocalPath = r.LocalPath,
                Method = r.Method,
                Trigger = r.Trigger,
                LastResult = r.LastResult,
            }).ToList(),
        };
        await _api.SendHeartbeatAsync(inst, hb);
    }
}
