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
        catch (Exception ex) { AppLog.Info($"[PullEngine] tick: {ex}"); }
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
                if (!await HasChangesAsync(inst, watchRules[i], _appToken))
                {
                    AppLog.Debug($"[PullEngine] 预检无变更: {watchRules[i].Name}");
                    continue;
                }
                await RunOnceAsync(inst, watchRules[i], _appToken);
            }
        }
        catch (Exception ex) { AppLog.Info($"[PullEngine] tick: {ex}"); }
    }

    /// <summary>
    /// watch 预检：用 rclone lsf 列远端匹配文件，与本地比对。
    /// - copy：远端有匹配文件且（本地不存在或大小不同）= 有变更
    /// - move：远端有匹配文件 = 有变更（需执行以清理远端源文件）
    /// 预检失败时保守返回 true（宁可多余跑一次也不漏同步）。
    /// </summary>
    private async Task<bool> HasChangesAsync(PanelInstance inst, PullRule rule, CancellationToken ct)
    {
        try
        {
            var remoteFiles = await _rclone.ListRemoteFilesAsync(inst, rule, ct);
            AppLog.Debug($"[PullEngine] 预检 {rule.Name}: 远端 {remoteFiles.Count} 文件");
            if (remoteFiles.Count == 0) return false;
            if (rule.Method == "move") return true;
            foreach (var (relPath, size) in remoteFiles)
            {
                var localFile = Path.Combine(rule.LocalPath, relPath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(localFile) || new FileInfo(localFile).Length != size)
                    return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            AppLog.Info($"[PullEngine] 预检失败 {rule.Name}: {ex}");
            return true;
        }
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
            AppLog.Info($"[PullEngine] ensure remote: {ex}");
        }

        AppLog.Info($"[PullEngine] 同步开始: rule={rule.Name} method={rule.Method} dst={rule.LocalPath}");
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
                    var st = entry.Stats;
                    long speed = (long)st.Speed;
                    filesSynced = st.Transfers;
                    int pct = st.TotalBytes > 0
                        ? (int)Math.Round((double)st.Bytes / st.TotalBytes * 100)
                        : 0;
                    var active = st.Transferring.Count > 0 ? st.Transferring[0] : null;
                    string file = active?.Name ?? "";
                    _rules.ReportProgress(file, pct, speed, filesSynced);
                    AppLog.Debug($"[rclone] stats bytes={st.Bytes}/{st.TotalBytes} pct={pct}% transfers={st.Transfers} speed={speed}");
                }
                else if (entry.Level == "error")
                {
                    lastRcloneError = entry.Msg;
                    AppLog.Info($"[rclone] error: {entry.Msg}");
                    await _jobs.EventAsync(inst, jobId, "rule_error", rule.RuleId, "error", new() { ["msg"] = entry.Msg }, ct);
                }
                else
                {
                    AppLog.Debug($"[rclone] {entry.Level}: {entry.Msg}");
                }
            }, ct);

            var errorKey = RcloneErrorMapper.Map(code, lastRcloneError);

            if (code == 0 && filesSynced == 0)
            {
                AppLog.Info($"[PullEngine] 同步完成(无变更): rule={rule.Name}");
                await _jobs.FinishAsync(inst, jobId, "success", filesSynced: 0, summary: L("pull.error.nochange"), ct: ct);
                _rules.MarkIdle();
                return (true, null);
            }

            rule.LastResult = code == 0 ? $"{filesSynced} {L("pull.error.success")}" : L(errorKey);
            rule.LastRunAt = DateTime.Now;
            _ruleStore.Upsert(rule);

            if (code == 0)
            {
                AppLog.Info($"[PullEngine] 同步完成: rule={rule.Name} files={filesSynced}");
                await _jobs.FinishAsync(inst, jobId, "success", filesSynced: filesSynced, summary: rule.Name, ct: ct);
                _rules.MarkIdle();
                return (true, null);
            }
            else
            {
                AppLog.Info($"[PullEngine] 同步失败: rule={rule.Name} code={code} error={L(errorKey)}");
                await _jobs.FinishAsync(inst, jobId, "failed", filesSynced: filesSynced, summary: L(errorKey), ct: ct);
                _rules.MarkError(L(errorKey));
                return (false, L(errorKey));
            }
        }
        catch (OperationCanceledException)
        {
            AppLog.Info($"[PullEngine] 同步取消: rule={rule.Name}");
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
            AppLog.Info($"[PullEngine] 同步异常: rule={rule.Name} {ex}");
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
