using System.Diagnostics;

namespace ComfyCarry.Services;

/// <summary>
/// App 本地数据目录布局（%LOCALAPPDATA%\ComfyCarry\）。
/// </summary>
public sealed class AppPaths
{
    public string Root { get; }
    public string DataDir { get; }
    public string RcloneExePath { get; }
    public string CloudRcloneConf { get; }    // Tab1 云存储 remote
    public string PullRcloneConf { get; }     // Tab2 webdav remote
    public string TempConfDir { get; }        // Tab1 向导临时 conf
    public string InstancesFile { get; }
    public string RulesFile { get; }
    public string SettingsFile { get; }
    public string LogFile { get; }
    public string AssetsDir { get; }

    public AppPaths()
    {
        Root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ComfyCarry");
        DataDir = Path.Combine(Root, "data");
        RcloneExePath = Path.Combine(AppContext.BaseDirectory, "rclone.exe");
        CloudRcloneConf = Path.Combine(DataDir, "cloud-rclone.conf");
        PullRcloneConf = Path.Combine(DataDir, "pull-rclone.conf");
        TempConfDir = Path.Combine(DataDir, "wizard");
        InstancesFile = Path.Combine(DataDir, "instances.json");
        RulesFile = Path.Combine(DataDir, "rules.json");
        SettingsFile = Path.Combine(DataDir, "settings.json");
        LogFile = Path.Combine(Root, "comfycarry.log");
        AssetsDir = Path.Combine(AppContext.BaseDirectory, "Assets");
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(TempConfDir);
        Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
    }

    public bool IsRclonePresent() => File.Exists(RcloneExePath);
}
