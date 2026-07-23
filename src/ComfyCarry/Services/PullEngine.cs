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
    private readonly LocalizationService _locale;
    private readonly CancellationToken _appToken;
    private Timer? _watchTimer;
    private CancellationTokenSource? _currentCts;

    public bool Paused => !_settings.Data.AutoSync;

    public PullEngine(RcloneService rclone, RuleEngine rules, RuleStore ruleStore, JobReporter jobs,
        InstanceStore instances, AppPaths paths, SettingsService settings, LocalizationService locale, CancellationToken appToken)
    {
        _rclone = rclone;
        _rules = rules;
        _ruleStore = ruleStore;
        _jobs = jobs;
        _instances = instances;
        _paths = paths;
        _settings = settings;
        _locale = locale;
        _appToken = appToken;
    }

    public void Start()
    {
        var interval = _settings.Data.PullWatchIntervalSec;
        if (interval < 5) interval = 60;
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
        try { await TickAsync(); }
        catch (Exception ex) { Debug.WriteLine($"[PullEngine] tick: {ex}"); }
    }

    private async Task TickAsync()
    {
        if (Paused) return;
        var inst = _instances.Current;
        if (inst is null) return;
        if (_rules.Status == "syncing") return;
        try
        {
            var watchRules = _ruleStore.All
                .Where(r => r.Enabled && r.Trigger == "watch" && !string.IsNullOrEmpty(r.LocalPath)).ToList();
            for (int i = 0; i < watchRules.Count; i++)
            {
                if (_appToken.IsCancellationRequested) break;
                _rules.SetQueueContext(i + 1, watchRules.Count);
                await RunOnceAsync(inst, watchRules[i], _appToken);
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[PullEngine] tick: {ex}"); }
    }

    private string L(string key) => _locale.T(key);

    /// <summary>UI 手动触发某条规则。返回 false 时 reason 含拒绝原因。</summary>
    public async Task<(bool ok, string? reason)> RunOnceAsync(PanelInstance inst, PullRule rule, CancellationToken externalCt = default)
    {
        if (!_rclone.IsPresent)
        {
            _rules.MarkError(_locale.T("pull.error.rclone_missing"));
            return (false, _locale.T("pull.error.rclone_missing"));
        }
        if (string.IsNullOrEmpty(rule.LocalPath))
        {
            _rules.MarkError(_locale.T("pull.error.no_local_path"));
            return (false, _locale.T("pull.error.no_local_path"));
        }
        // 防止并发覆盖：已有任务在跑则拒绝
        if (_currentCts is not null && !_currentCts.IsCancellationRequested)
            return (false, _locale.T("pull.error.busy"));

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
        string? lastRcloneError = null;
        try
        {
            int code = await _rclone.PullAsync(inst, rule, async entry =>
            {
                if (entry.Stats is not null)
                {
                    // stats 汇总行：更新速度和文件数，不覆盖文件名
                    long speed = (long)entry.Stats.Speed;
                    _rules.ReportStats(speed, filesSynced);
                }
                else if (entry.Percentage is not null)
                {
                    // per-transfer 行：更新文件名和百分比
                    int pct = (int)Math.Round(entry.Percentage.Value);
                    long speed = entry.Speed is { } s ? (long)s : 0;
                    string file = entry.Object ?? entry.Name ?? "";
                    _rules.ReportProgress(file, pct, speed, filesSynced);
                    if (entry.Status == "done")
                    {
                        filesSynced++;
                        await _jobs.EventAsync(inst, jobId, "file_done", rule.RuleId, pars: new() { ["file"] = file, ["pct"] = pct, ["speed"] = speed }, ct: ct);
                    }
                }
                else if (entry.Level == "error")
                {
                    lastRcloneError = entry.Msg;
                    await _jobs.EventAsync(inst, jobId, "rule_error", rule.RuleId, "error", new() { ["msg"] = entry.Msg }, ct);
                }
            }, ct);

            var errorKey = RcloneErrorMapper.Map(code, lastRcloneError);
            rule.LastResult = code == 0 ? $"{filesSynced} {L("pull.error.success")}" : L(errorKey);
            rule.LastRunAt = DateTime.Now;
            _ruleStore.Upsert(rule);

            if (code == 0)
            {
                await _jobs.FinishAsync(inst, jobId, "success", filesSynced: filesSynced, summary: rule.Name, ct: ct);
                _rules.MarkIdle();
                return (true, null);
            }
            else
            {
                await _jobs.FinishAsync(inst, jobId, "failed", filesSynced: filesSynced, summary: L(errorKey), ct: ct);
                _rules.MarkError(L(errorKey));
                return (false, L(errorKey));
            }
        }
        catch (OperationCanceledException)
        {
            rule.LastResult = L("pull.error.cancelled");
            rule.LastRunAt = DateTime.Now;
            _ruleStore.Upsert(rule);
            await _jobs.FinishAsync(inst, jobId, "cancelled", filesSynced: filesSynced, summary: "cancelled", ct: CancellationToken.None);
            _rules.MarkIdle();
            return (false, L("pull.error.cancelled"));
        }
        catch (Exception ex)
        {
            var msg = ex.Message;
            var key = RcloneErrorMapper.Map(-1, msg);
            rule.LastResult = L(key);
            rule.LastRunAt = DateTime.Now;
            _ruleStore.Upsert(rule);
            await _jobs.FinishAsync(inst, jobId, "failed", filesSynced: filesSynced, summary: L(key), ct: CancellationToken.None);
            _rules.MarkError(L(key));
            return (false, L(key));
        }
        finally
        {
            _currentCts?.Dispose();
            _currentCts = null;
        }
    }
}
