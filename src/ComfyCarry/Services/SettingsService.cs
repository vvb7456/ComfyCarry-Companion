using System.Diagnostics;
using System.Text.Json;

namespace ComfyCarry.Services;

public enum AppTheme { System, Light, Dark }

public sealed class AppSettings
{
    public string Language { get; set; } = "zh-CN";   // zh-CN | en-US
    public AppTheme Theme { get; set; } = AppTheme.System;
    public bool StartWithWindows { get; set; } = false;
    public bool MinimizeToTray { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public bool AutoSync { get; set; } = true;
    public int HeartbeatIntervalSec { get; set; } = 20;
    public int PullWatchIntervalSec { get; set; } = 60;
    public int MinAgeSec { get; set; } = 30;
    public string LastTab { get; set; } = "cloud";
    public string Proxy { get; set; } = "";          // rclone 代理地址，如 http://127.0.0.1:7890
}

public sealed class SettingsService
{
    private readonly AppPaths _paths;
    private readonly object _lock = new();
    private AppSettings _data = new();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public event Action? Changed;

    public SettingsService(AppPaths paths) => _paths = paths;

    public AppSettings Data { get { lock (_lock) return _data; } }

    public void Load()
    {
        try
        {
            if (!File.Exists(_paths.SettingsFile)) return;
            var json = File.ReadAllText(_paths.SettingsFile);
            var data = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new AppSettings();
            lock (_lock) _data = data;
        }
        catch (Exception ex) { Debug.WriteLine($"[Settings] Load failed: {ex}"); }
    }

    public void Save()
    {
        try
        {
            string json;
            lock (_lock) json = JsonSerializer.Serialize(_data, JsonOpts);
            File.WriteAllText(_paths.SettingsFile, json);
            Changed?.Invoke();
        }
        catch (Exception ex) { Debug.WriteLine($"[Settings] Save failed: {ex}"); }
    }

    public void Update(Action<AppSettings> mut)
    {
        lock (_lock) mut(_data);
        Save();
    }
}
