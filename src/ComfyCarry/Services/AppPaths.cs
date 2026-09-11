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
    public string PullRcloneConf { get; }     // 同步用 webdav remote
    public string InstancesFile { get; }
    public string RulesFile { get; }
    public string SettingsFile { get; }
    public string LogFile { get; }
    public string PlacementFile { get; }

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
        PullRcloneConf = Path.Combine(DataDir, "pull-rclone.conf");
        InstancesFile = Path.Combine(DataDir, "instances.json");
        RulesFile = Path.Combine(DataDir, "rules.json");
        SettingsFile = Path.Combine(DataDir, "settings.json");
        LogFile = Path.Combine(Root, "comfycarry.log");
        PlacementFile = Path.Combine(DataDir, "placement.json");
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(DataDir);
    }

    public bool IsRclonePresent() => File.Exists(RcloneExePath);
}
