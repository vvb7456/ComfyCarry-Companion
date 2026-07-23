using System.Diagnostics;
using System.Text.Json;

namespace ComfyCarry.Services;

/// <summary>
/// 多实例列表存储（SPEC §3.5）。凭据字段 DPAPI 加密落盘。
/// </summary>
public sealed class InstanceStore
{
    private readonly AppPaths _paths;
    private readonly SecretStore _secrets;
    private readonly object _lock = new();
    private List<PanelInstance> _list = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public event Action? Changed;

    public InstanceStore(AppPaths paths, SecretStore secrets)
    {
        _paths = paths;
        _secrets = secrets;
    }

    public IReadOnlyList<PanelInstance> All
    {
        get { lock (_lock) return _list.ToList(); }
    }

    public PanelInstance? Current
    {
        get { lock (_lock) return _list.FirstOrDefault(i => i.IsCurrent) ?? _list.FirstOrDefault(); }
    }

    /// <summary>在锁内修改当前实例字段，避免与 HeartbeatService 读竞态。</summary>
    public void UpdateCurrent(Action<PanelInstance> mut)
    {
        lock (_lock)
        {
            var inst = _list.FirstOrDefault(i => i.IsCurrent) ?? _list.FirstOrDefault();
            if (inst is not null) mut(inst);
        }
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(_paths.InstancesFile)) return;
            var json = File.ReadAllText(_paths.InstancesFile);
            var list = JsonSerializer.Deserialize<List<PanelInstance>>(json, JsonOpts) ?? new();
            // 解密凭据到内存字段
            foreach (var inst in list)
            {
                inst.Password = _secrets.Unprotect(inst.EncPassword);
                inst.ApiKey = _secrets.Unprotect(inst.EncApiKey);
            }
            lock (_lock) _list = list;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InstanceStore] Load failed: {ex}");
        }
    }

    public void Save()
    {
        try
        {
            List<PanelInstance> snap;
            lock (_lock)
            {
                // 同步内存凭据到加密字段
                foreach (var inst in _list)
                {
                    if (inst.Password.Length > 0) inst.EncPassword = _secrets.Protect(inst.Password);
                    if (inst.ApiKey.Length > 0) inst.EncApiKey = _secrets.Protect(inst.ApiKey);
                }
                snap = _list.ToList();
            }
            var json = JsonSerializer.Serialize(snap, JsonOpts);
            File.WriteAllText(_paths.InstancesFile, json);
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InstanceStore] Save failed: {ex}");
        }
    }

    public PanelInstance Upsert(PanelInstance inst)
    {
        lock (_lock)
        {
            var idx = _list.FindIndex(i => i.Id == inst.Id);
            if (idx >= 0) _list[idx] = inst;
            else _list.Add(inst);
            // 只能有一个 current
            if (inst.IsCurrent)
            {
                foreach (var other in _list.Where(i => i.Id != inst.Id)) other.IsCurrent = false;
            }
            else if (!_list.Any(i => i.IsCurrent) && _list.Count > 0)
            {
                _list[0].IsCurrent = true;
            }
        }
        Save();
        return inst;
    }

    public void Remove(string id)
    {
        lock (_lock) _list.RemoveAll(i => i.Id == id);
        Save();
    }

    public void SetCurrent(string id)
    {
        lock (_lock)
        {
            foreach (var i in _list) i.IsCurrent = (i.Id == id);
        }
        Save();
    }

    /// <summary>确保当前实例有 ClientId（首次连接生成稳定 uuid）。</summary>
    public void EnsureClientId(PanelInstance inst)
    {
        if (string.IsNullOrEmpty(inst.ClientId))
        {
            inst.ClientId = Guid.NewGuid().ToString();
            Save();
        }
    }
}
