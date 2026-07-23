using System.Diagnostics;

namespace ComfyCarry.Services;

/// <summary>
/// 绿色软件数据目录布局，数据保存在 exe 旁边而非 %LOCALAPPDATA%。
/// 发布结构 launcher 在根目录、主程序在 app/ 子目录，
/// 数据放在根目录（launcher 旁边），更新 app/ 不丢数据。
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
    public string PlacementFile { get; }
    public string AssetsDir { get; }

    public AppPaths()
    {
        var baseDir = AppContext.BaseDirectory; // always ends with \
        // 发布模式：主程序在 app/ 子目录，数据放上一级（launcher 旁边）
        var dirName = Path.GetFileName(Path.TrimEndingDirectorySeparator(baseDir));
        Root = string.Equals(dirName, "app", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFullPath(Path.Combine(baseDir, ".."))
            : baseDir;

        DataDir = Path.Combine(Root, "data");
        RcloneExePath = Path.Combine(AppContext.BaseDirectory, "rclone.exe");
        CloudRcloneConf = Path.Combine(DataDir, "cloud-rclone.conf");
        PullRcloneConf = Path.Combine(DataDir, "pull-rclone.conf");
        TempConfDir = Path.Combine(DataDir, "wizard");
        InstancesFile = Path.Combine(DataDir, "instances.json");
        RulesFile = Path.Combine(DataDir, "rules.json");
        SettingsFile = Path.Combine(DataDir, "settings.json");
        LogFile = Path.Combine(Root, "comfycarry.log");
        PlacementFile = Path.Combine(DataDir, "placement.json");
        AssetsDir = Path.Combine(AppContext.BaseDirectory, "Assets");
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(TempConfDir);
    }

    public bool IsRclonePresent() => File.Exists(RcloneExePath);
}
