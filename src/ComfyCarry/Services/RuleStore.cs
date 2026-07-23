using System.Text.Json;
using ComfyCarry.Models;

namespace ComfyCarry.Services;

/// <summary>
/// 本地规则持久化。规则全局共享，与实例解耦。
/// </summary>
public sealed class RuleStore
{
    private readonly string _file;
    private readonly object _lock = new();
    private List<PullRule> _data = new();
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public event Action? Changed;

    public RuleStore(AppPaths paths) => _file = paths.RulesFile;

    public void Load()
    {
        try
        {
            if (File.Exists(_file))
            {
                var json = File.ReadAllText(_file);
                var data = JsonSerializer.Deserialize<List<PullRule>>(json, JsonOpts) ?? new();
                lock (_lock) _data = data;
            }
        }
        catch { lock (_lock) _data = new(); }
    }

    public void Save()
    {
        try
        {
            string json;
            lock (_lock) json = JsonSerializer.Serialize(_data, JsonOpts);
            File.WriteAllText(_file, json);
        }
        catch { }
    }

    public IReadOnlyList<PullRule> All
    {
        get { lock (_lock) return _data.ToList(); }
    }

    public void Upsert(PullRule rule)
    {
        lock (_lock)
        {
            var idx = _data.FindIndex(r => r.RuleId == rule.RuleId);
            if (idx >= 0) _data[idx] = rule;
            else _data.Add(rule);
        }
        Save();
        Changed?.Invoke();
    }

    public void Delete(string ruleId)
    {
        lock (_lock) _data.RemoveAll(r => r.RuleId == ruleId);
        Save();
        Changed?.Invoke();
    }
}
