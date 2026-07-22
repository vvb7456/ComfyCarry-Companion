using System.Collections.ObjectModel;
using ComfyCarry.Models;
using Microsoft.UI.Dispatching;

namespace ComfyCarry.Services;

/// <summary>
/// 规则的本地状态管理（规则归客户端所有，本地持久化）。
/// </summary>
public sealed class RuleEngine
{
    private readonly RuleStore _store;
    private readonly InstanceStore _instances;
    private readonly JobReporter _jobs;
    private readonly object _lock = new();
    private DispatcherQueue? _uiQueue;

    public ObservableCollection<PullRule> Rules { get; } = new();

    public PullRule? ActiveRule { get; private set; }
    public string Status { get; private set; } = "idle";
    public int ProgressPct { get; private set; }
    public string ActiveFile { get; private set; } = "";
    public long ActiveSpeed { get; private set; }

    public event Action? StateChanged;

    public RuleEngine(RuleStore store, InstanceStore instances, JobReporter jobs)
    {
        _store = store;
        _instances = instances;
        _jobs = jobs;
    }

    public void AttachUi(DispatcherQueue queue) => _uiQueue = queue;

    private void Ui(Action a)
    {
        if (_uiQueue is not null && !_uiQueue.HasThreadAccess) _uiQueue.TryEnqueue(() => a());
        else a();
    }

    /// <summary>从本地 RuleStore 刷新规则列表（无网络）。</summary>
    /// <remarks>不触发 StateChanged：Refresh 是读操作，调用方（RefreshUI/SaveRule/DeleteRule）
    /// 本身已由 RuleStore.Changed 或显式 SetActive/MarkIdle 等事件驱动 UI 刷新，
    /// 在此处 fire StateChanged 会形成 RefreshUI -> Refresh -> StateChanged -> RefreshUI 无限循环。</remarks>
    public void Refresh(PanelInstance inst)
    {
        var label = inst.InstanceLabel;
        var rules = _store.RulesFor(label);
        Ui(() =>
        {
            lock (_lock)
            {
                Rules.Clear();
                foreach (var r in rules) Rules.Add(r);
            }
        });
    }

    /// <summary>保存规则到本地（无网络）。</summary>
    public void SaveRule(PanelInstance inst, PullRule rule)
    {
        _store.Upsert(inst.InstanceLabel, rule);
        Refresh(inst);
    }

    /// <summary>删除规则（本地）。</summary>
    public void DeleteRule(PanelInstance inst, string ruleId)
    {
        _store.Delete(inst.InstanceLabel, ruleId);
        Refresh(inst);
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
}
