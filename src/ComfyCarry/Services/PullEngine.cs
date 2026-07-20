using System.Diagnostics;
using ComfyCarry.Models;

namespace ComfyCarry.Services;

/// <summary>
/// 后台规则驱动 rclone 拉取引擎（SPEC §3.4）。
/// - watch 规则：按 IntervalSec 周期触发。
/// - manual 规则：由 UI 调用 RunOnceAsync。
/// 每次：建/确保 webdav remote → 创建 Job → rclone <method> → 解析日志回报 → finish。
/// </summary>
public sealed class PullEngine
{
    private readonly RcloneService _rclone;
    private readonly RuleEngine _rules;
    private readonly JobReporter _jobs;
    private readonly InstanceStore _instances;
    private readonly AppPaths _paths;
    private readonly SettingsService _settings;
    private readonly CancellationToken _appToken;
    private Timer? _watchTimer;

    public bool Paused { get; set; }

    public PullEngine(RcloneService rclone, RuleEngine rules, JobReporter jobs,
        InstanceStore instances, AppPaths paths, SettingsService settings, CancellationToken appToken)
    {
        _rclone = rclone;
        _rules = rules;
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
    }

    private async void Tick(object? _)
    {
        if (Paused) return;
        var inst = _instances.Current;
        if (inst is null) return;
        try
        {
            // 拉取最新规则
            await _rules.RefreshAsync(inst, _appToken);
            var watchRules = _rules.Rules.Where(r => r.Enabled && r.Trigger == "watch" && !string.IsNullOrEmpty(r.LocalPath)).ToList();
            foreach (var rule in watchRules)
            {
                if (_appToken.IsCancellationRequested) break;
                await RunOnceAsync(inst, rule, _appToken);
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[PullEngine] tick: {ex}"); }
    }

    /// <summary>UI 手动触发某条规则。</summary>
    public async Task<bool> RunOnceAsync(PanelInstance inst, PullRule rule, CancellationToken ct = default)
    {
        if (!_rclone.IsPresent)
        {
            _rules.MarkError("rclone.exe 缺失");
            return false;
        }
        if (string.IsNullOrEmpty(rule.LocalPath))
        {
            _rules.MarkError("规则未设置本机路径");
            return false;
        }

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
        await _jobs.EventAsync(inst, jobId, new JobEventRequest { Type = "rule_start", Message = rule.Name }, ct);

        try
        {
            int code = await _rclone.PullAsync(inst, rule, async entry =>
            {
                if (entry.Stats is not null || entry.Percentage is not null)
                {
                    int pct = entry.Percentage is { } d ? (int)Math.Round(d) : 0;
                    long speed = entry.Speed is { } s ? (long)s : (entry.Stats?.Speed ?? 0);
                    string file = entry.Object ?? entry.Name ?? "";
                    _rules.ReportProgress(file, pct, speed);
                    await _jobs.EventAsync(inst, jobId, new JobEventRequest
                    {
                        Type = "file_transferred",
                        File = file,
                        Pct = pct,
                        Speed = speed,
                        Message = entry.Msg,
                    }, ct);
                }
                else if (entry.Level == "error")
                {
                    await _jobs.EventAsync(inst, jobId, new JobEventRequest
                    {
                        Type = "log",
                        Level = "error",
                        Message = entry.Msg,
                    }, ct);
                }
            }, ct);

            if (code == 0)
            {
                await _jobs.EventAsync(inst, jobId, new JobEventRequest { Type = "rule_done" }, ct);
                await _jobs.FinishAsync(inst, jobId, "ok", null, ct);
                rule.StatusText = "idle";
                _rules.MarkIdle();
                return true;
            }
            else
            {
                await _jobs.FinishAsync(inst, jobId, "error", $"rclone exit {code}", ct);
                _rules.MarkError($"rclone exit {code}");
                return false;
            }
        }
        catch (OperationCanceledException)
        {
            await _jobs.FinishAsync(inst, jobId, "error", "cancelled", ct);
            _rules.MarkIdle();
            return false;
        }
        catch (Exception ex)
        {
            await _jobs.FinishAsync(inst, jobId, "error", ex.Message, ct);
            _rules.MarkError(ex.Message);
            return false;
        }
    }
}
