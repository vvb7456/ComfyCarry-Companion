using System.Text.Json;
using ComfyCarry.Models;

namespace ComfyCarry.Services;

/// <summary>
/// 本地规则持久化。key = instance_label，规则绑定逻辑实例、跨重部署存活。
/// </summary>
public sealed class RuleStore
{
    private readonly string _file;
    private Dictionary<string, List<PullRule>> _data = new();
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
                _data = JsonSerializer.Deserialize<Dictionary<string, List<PullRule>>>(json, JsonOpts) ?? new();
            }
        }
        catch { _data = new(); }
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_data, JsonOpts);
            File.WriteAllText(_file, json);
        }
        catch { }
    }

    public IReadOnlyList<PullRule> RulesFor(string label)
    {
        if (string.IsNullOrEmpty(label)) return Array.Empty<PullRule>();
        return _data.TryGetValue(label, out var list) ? list : Array.Empty<PullRule>();
    }

    public void Upsert(string label, PullRule rule)
    {
        if (string.IsNullOrEmpty(label)) return;
        if (!_data.TryGetValue(label, out var list))
        {
            list = new List<PullRule>();
            _data[label] = list;
        }
        var idx = list.FindIndex(r => r.RuleId == rule.RuleId);
        if (idx >= 0) list[idx] = rule;
        else list.Add(rule);
        Save();
        Changed?.Invoke();
    }

    public void Delete(string label, string ruleId)
    {
        if (string.IsNullOrEmpty(label)) return;
        if (_data.TryGetValue(label, out var list))
        {
            list.RemoveAll(r => r.RuleId == ruleId);
            Save();
            Changed?.Invoke();
        }
    }
}
