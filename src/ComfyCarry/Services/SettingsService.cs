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
    public int HeartbeatIntervalSec { get; set; } = 20;
    public int PullWatchIntervalSec { get; set; } = 300;
    public string LastTab { get; set; } = "cloud";
    public string Proxy { get; set; } = "";          // rclone 代理地址，如 http://127.0.0.1:7890
}

public sealed class SettingsService
{
    private readonly AppPaths _paths;
    private AppSettings _data = new();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public event Action? Changed;

    public SettingsService(AppPaths paths) => _paths = paths;

    public AppSettings Data => _data;

    public void Load()
    {
        try
        {
            if (!File.Exists(_paths.SettingsFile)) return;
            var json = File.ReadAllText(_paths.SettingsFile);
            _data = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new AppSettings();
        }
        catch (Exception ex) { Debug.WriteLine($"[Settings] Load failed: {ex}"); }
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_data, JsonOpts);
            File.WriteAllText(_paths.SettingsFile, json);
            Changed?.Invoke();
        }
        catch (Exception ex) { Debug.WriteLine($"[Settings] Save failed: {ex}"); }
    }

    public void Update(Action<AppSettings> mut)
    {
        mut(_data);
        Save();
    }
}
