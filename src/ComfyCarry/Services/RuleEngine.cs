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
    public int FilesCompleted { get; private set; }
    public string? LastError { get; private set; }

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
    public void Refresh()
    {
        var rules = _store.All;
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
    public void SaveRule(PullRule rule)
    {
        _store.Upsert(rule);
        Refresh();
    }

    /// <summary>删除规则（本地）。</summary>
    public void DeleteRule(string ruleId)
    {
        _store.Delete(ruleId);
        Refresh();
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

    public void ReportProgress(string file, int pct, long speed, int filesCompleted = 0)
    {
        Ui(() =>
        {
            ActiveFile = file;
            ProgressPct = pct;
            ActiveSpeed = speed;
            FilesCompleted = filesCompleted;
            StateChanged?.Invoke();
        });
    }

    public void MarkIdle()
    {
        Ui(() =>
        {
            Status = "idle";
            ProgressPct = 0;
            ActiveFile = "";
            ActiveSpeed = 0;
            FilesCompleted = 0;
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
            LastError = msg;
            if (ActiveRule is not null) ActiveRule.StatusText = "error";
            StateChanged?.Invoke();
        });
    }
}
