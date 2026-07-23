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
    public int QueueIndex { get; private set; }
    public int QueueTotal { get; private set; }

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

    /// <summary>从本地 RuleStore 刷新规则列表（差量更新，避免全量重建导致 UI 闪烁）。</summary>
    public void Refresh()
    {
        var rules = _store.All;
        Ui(() =>
        {
            lock (_lock)
            {
                // 移除已删除的
                for (int i = Rules.Count - 1; i >= 0; i--)
                {
                    if (!rules.Any(r => r.RuleId == Rules[i].RuleId))
                        Rules.RemoveAt(i);
                }
                // 新增或更新
                foreach (var r in rules)
                {
                    var existing = Rules.FirstOrDefault(x => x.RuleId == r.RuleId);
                    if (existing is null)
                    {
                        Rules.Add(r);
                    }
                    else
                    {
                        // 就地更新字段，不替换对象引用
                        existing.Name = r.Name;
                        existing.LocalPath = r.LocalPath;
                        existing.Method = r.Method;
                        existing.Content = r.Content;
                        existing.Subdirs = r.Subdirs;
                        existing.Trigger = r.Trigger;
                        existing.Enabled = r.Enabled;
                        existing.LastResult = r.LastResult;
                        existing.LastRunAt = r.LastRunAt;
                    }
                }
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

    public void SetQueueContext(int index, int total)
    {
        Ui(() =>
        {
            QueueIndex = index;
            QueueTotal = total;
            StateChanged?.Invoke();
        });
    }

    /// <summary>更新进度（transfer 行）。</summary>
    public void ReportProgress(string file, int pct, long speed, int filesCompleted = 0)
    {
        Ui(() =>
        {
            if (!string.IsNullOrEmpty(file)) ActiveFile = file;
            ProgressPct = pct;
            ActiveSpeed = speed;
            FilesCompleted = filesCompleted;
            StateChanged?.Invoke();
        });
    }

    /// <summary>更新 stats 汇总行（不覆盖文件名）。</summary>
    public void ReportStats(long speed, int filesCompleted)
    {
        Ui(() =>
        {
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
            QueueIndex = 0;
            QueueTotal = 0;
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
