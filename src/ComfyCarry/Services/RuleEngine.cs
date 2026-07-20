using System.Collections.ObjectModel;
using ComfyCarry.Models;
using Microsoft.UI.Dispatching;

namespace ComfyCarry.Services;

/// <summary>
/// 规则的本地缓存 + 与面板同步（面板为真源，SPEC §2.3）。
/// UI 绑定 Rules 集合；集合修改在 UI 线程进行。
/// </summary>
public sealed class RuleEngine
{
    private readonly CompanionApiClient _api;
    private readonly JobReporter _jobs;
    private readonly object _lock = new();
    private DispatcherQueue? _uiQueue;

    public ObservableCollection<PullRule> Rules { get; } = new();
    public ObservableCollection<PullRule> Templates { get; } = new();

    public PullRule? ActiveRule { get; private set; }

    public string Status { get; private set; } = "idle";
    public int ProgressPct { get; private set; }
    public string ActiveFile { get; private set; } = "";
    public long ActiveSpeed { get; private set; }

    public event Action? StateChanged;

    public RuleEngine(CompanionApiClient api, JobReporter jobs)
    {
        _api = api;
        _jobs = jobs;
    }

    /// <summary>在 UI 线程被激活后注入 DispatcherQueue，确保集合修改在 UI 线程。</summary>
    public void AttachUi(DispatcherQueue queue) => _uiQueue = queue;

    private void Ui(Action a)
    {
        if (_uiQueue is not null && !_uiQueue.HasThreadAccess) _uiQueue.TryEnqueue(() => a());
        else a();
    }

    public async Task RefreshAsync(PanelInstance inst, CancellationToken ct = default)
    {
        var resp = await _api.GetRulesAsync(inst, ct);
        var rules = resp.Rules;
        var templates = resp.Templates;
        Ui(() =>
        {
            lock (_lock)
            {
                Rules.Clear();
                foreach (var r in rules) Rules.Add(r);
                Templates.Clear();
                foreach (var t in templates) Templates.Add(t);
            }
            StateChanged?.Invoke();
        });
    }

    public async Task<bool> SaveRuleAsync(PanelInstance inst, PullRule rule, CancellationToken ct = default)
    {
        rule.ClientId = inst.ClientId;
        rule.Executor = "companion";
        rule.Direction = "pull";
        var saved = await _api.SaveRuleAsync(inst, rule, ct);
        if (saved is not null)
        {
            Ui(() =>
            {
                lock (_lock)
                {
                    var idx = Rules.ToList().FindIndex(r => r.RuleId == saved.RuleId);
                    if (idx >= 0) Rules[idx] = saved;
                    else Rules.Add(saved);
                }
                StateChanged?.Invoke();
            });
            return true;
        }
        return false;
    }

    public async Task<bool> DeleteRuleAsync(PanelInstance inst, string ruleId, CancellationToken ct = default)
    {
        var ok = await _api.DeleteRuleAsync(inst, ruleId, ct);
        if (ok)
        {
            Ui(() =>
            {
                lock (_lock)
                {
                    var toRemove = Rules.ToList().Where(r => r.RuleId == ruleId).ToList();
                    foreach (var r in toRemove) Rules.Remove(r);
                }
                StateChanged?.Invoke();
            });
        }
        return ok;
    }

    public void SetActive(PullRule? rule)
    {
        Ui(() =>
        {
            ActiveRule = rule;
            if (rule is not null) rule.StatusText = "syncing";
            Status = rule is null ? "idle" : "syncing";
            ProgressPct = 0;
            StateChanged?.Invoke();
        });
    }

    public void ReportProgress(string file, int pct, long speed)
    {
        Ui(() =>
        {
            ActiveFile = file;
            ProgressPct = pct;
            ActiveSpeed = speed;
            StateChanged?.Invoke();
        });
    }

    public void MarkIdle()
    {
        Ui(() =>
        {
            Status = "idle";
            ProgressPct = 0;
            if (ActiveRule is not null) ActiveRule.StatusText = "idle";
            ActiveRule = null;
            StateChanged?.Invoke();
        });
    }

    public void MarkError(string? msg)
    {
        Ui(() =>
        {
            Status = "error";
            if (ActiveRule is not null) ActiveRule.StatusText = "error";
            StateChanged?.Invoke();
        });
    }

    /// <summary>默认产物扩展名集（SPEC §4.4）。</summary>
    public static readonly string[] DefaultFilters =
        { "png", "jpg", "jpeg", "webp", "gif", "bmp", "tiff", "tif", "mp4", "mov", "webm", "mkv", "avi" };

    public static PullRule NewFromTemplate(PullRule tmpl, PanelInstance inst)
    {
        return new PullRule
        {
            RuleId = "",
            ClientId = inst.ClientId,
            Executor = "companion",
            Direction = "pull",
            Method = tmpl.Method,
            RemotePath = tmpl.RemotePath,
            LocalPath = "",
            Filters = tmpl.Filters.Count > 0 ? tmpl.Filters.ToList() : DefaultFilters.ToList(),
            Trigger = tmpl.Trigger,
            IntervalSec = tmpl.IntervalSec,
            Enabled = true,
            Name = tmpl.Name + " (副本)",
            TemplateId = tmpl.TemplateId,
        };
    }
}
