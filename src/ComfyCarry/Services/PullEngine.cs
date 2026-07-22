using System.Diagnostics;
using ComfyCarry.Models;

namespace ComfyCarry.Services;

/// <summary>
/// 后台规则驱动 rclone 同步引擎。
/// - watch 规则：按 IntervalSec 周期触发（需 AutoSync 开启）。
/// - manual 规则：由 UI 调用 RunOnceAsync。
/// 支持 CancellationTokenSource 取消当前同步。
/// </summary>
public sealed class PullEngine
{
    private readonly RcloneService _rclone;
    private readonly RuleEngine _rules;
    private readonly RuleStore _ruleStore;
    private readonly JobReporter _jobs;
    private readonly InstanceStore _instances;
    private readonly AppPaths _paths;
    private readonly SettingsService _settings;
    private readonly CancellationToken _appToken;
    private Timer? _watchTimer;
    private CancellationTokenSource? _currentCts;

    public bool Paused => !_settings.Data.AutoSync;

    public PullEngine(RcloneService rclone, RuleEngine rules, RuleStore ruleStore, JobReporter jobs,
        InstanceStore instances, AppPaths paths, SettingsService settings, CancellationToken appToken)
    {
        _rclone = rclone;
        _rules = rules;
        _ruleStore = ruleStore;
        _jobs = jobs;
        _instances = instances;
        _paths = paths;
        _settings = settings;
        _appToken = appToken;
    }

    public void Start()
    {
        var interval = _settings.Data.PullWatchIntervalSec;
        if (interval < 30) interval = 300;
        _watchTimer = new Timer(Tick, null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(interval));
    }

    public void Stop()
    {
        _watchTimer?.Dispose();
        _watchTimer = null;
        _currentCts?.Cancel();
    }

    /// <summary>取消当前正在执行的同步任务。</summary>
    public void CancelCurrent()
    {
        _currentCts?.Cancel();
    }

    private async void Tick(object? _)
    {
        if (Paused) return;
        var inst = _instances.Current;
        if (inst is null) return;
        if (_rules.Status == "syncing") return;
        try
        {
            var watchRules = _ruleStore.All
                .Where(r => r.Enabled && r.Trigger == "watch" && !string.IsNullOrEmpty(r.LocalPath)).ToList();
            foreach (var rule in watchRules)
            {
                if (_appToken.IsCancellationRequested) break;
                await RunOnceAsync(inst, rule, _appToken);
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[PullEngine] tick: {ex}"); }
    }

    /// <summary>UI 手动触发某条规则。</summary>
    public async Task<bool> RunOnceAsync(PanelInstance inst, PullRule rule, CancellationToken externalCt = default)
    {
        if (!_rclone.IsPresent)
        {
            _rules.MarkError("rclone.exe missing");
            return false;
        }
        if (string.IsNullOrEmpty(rule.LocalPath))
        {
            _rules.MarkError("rule has no local path");
            return false;
        }

        _currentCts = CancellationTokenSource.CreateLinkedTokenSource(externalCt, _appToken);
        var ct = _currentCts.Token;

        try
        {
            await _rclone.EnsureInstanceWebdavRemoteAsync(inst, ct);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PullEngine] ensure remote: {ex}");
        }

        _rules.SetActive(rule);
        string? jobId = await _jobs.StartAsync(inst, rule, ct);
        await _jobs.EventAsync(inst, jobId, "rule_start_pull", rule.RuleId, pars: new() { ["name"] = rule.Name }, ct: ct);

        int filesSynced = 0;
        try
        {
            int code = await _rclone.PullAsync(inst, rule, async entry =>
            {
                if (entry.Stats is not null || entry.Percentage is not null)
                {
                    int pct = entry.Percentage is { } d ? (int)Math.Round(d) : 0;
                    long speed = entry.Speed is { } s ? (long)s : (long)(entry.Stats?.Speed ?? 0);
                    string file = entry.Object ?? entry.Name ?? "";
                    _rules.ReportProgress(file, pct, speed, filesSynced);
                    filesSynced++;
                    await _jobs.EventAsync(inst, jobId, "file_done", rule.RuleId, pars: new() { ["file"] = file, ["pct"] = pct, ["speed"] = speed }, ct: ct);
                }
                else if (entry.Level == "error")
                {
                    await _jobs.EventAsync(inst, jobId, "rule_error", rule.RuleId, "error", new() { ["msg"] = entry.Msg }, ct);
                }
            }, ct);

            rule.LastResult = code == 0 ? $"{filesSynced} files ok" : $"failed: rclone exit {code}";
            rule.LastRunAt = DateTime.Now;
            _ruleStore.Upsert(rule);

            if (code == 0)
            {
                await _jobs.FinishAsync(inst, jobId, "success", filesSynced: filesSynced, summary: rule.Name, ct: ct);
                _rules.MarkIdle();
                return true;
            }
            else
            {
                await _jobs.FinishAsync(inst, jobId, "failed", filesSynced: filesSynced, summary: $"rclone exit {code}", ct: ct);
                _rules.MarkError($"rclone exit {code}");
                return false;
            }
        }
        catch (OperationCanceledException)
        {
            rule.LastResult = "cancelled";
            rule.LastRunAt = DateTime.Now;
            _ruleStore.Upsert(rule);
            await _jobs.FinishAsync(inst, jobId, "cancelled", filesSynced: filesSynced, summary: "cancelled", ct: CancellationToken.None);
            _rules.MarkIdle();
            return false;
        }
        catch (Exception ex)
        {
            rule.LastResult = $"failed: {ex.Message}";
            rule.LastRunAt = DateTime.Now;
            _ruleStore.Upsert(rule);
            await _jobs.FinishAsync(inst, jobId, "failed", filesSynced: filesSynced, summary: ex.Message, ct: CancellationToken.None);
            _rules.MarkError(ex.Message);
            return false;
        }
        finally
        {
            _currentCts?.Dispose();
            _currentCts = null;
        }
    }
}
