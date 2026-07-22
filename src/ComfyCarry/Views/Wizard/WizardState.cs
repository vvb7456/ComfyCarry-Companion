using ComfyCarry.Models;

namespace ComfyCarry.Views.Wizard;

/// <summary>整个向导的共享状态。每次从首页进入时 Reset()。</summary>
public static class WizardState
{
    public static CloudType SelectedCloud { get; set; } = CloudType.OneDrivePersonal;
    public static string RemoteName { get; set; } = "";
    public static Dictionary<string, string> FieldValues { get; } = new();
    public static RcloneConfigState? LastState { get; set; }
    public static string TempConfPath { get; set; } = "";
    public static bool TestedOk { get; set; }
    public static bool CreateTree { get; set; } = true;

    public static void Reset()
    {
        SelectedCloud = CloudType.OneDrivePersonal;
        RemoteName = "";
        FieldValues.Clear();
        LastState = null;
        TempConfPath = "";
        TestedOk = false;
        CreateTree = true;
    }

    /// <summary>准备一个临时 conf 路径（在选类型页完成或命名页进入时调用）。</summary>
    public static void EnsureTempConf()
    {
        if (string.IsNullOrEmpty(TempConfPath) || !File.Exists(TempConfPath))
        {
            Directory.CreateDirectory(App.Hub.Paths.TempConfDir);
            TempConfPath = Path.Combine(App.Hub.Paths.TempConfDir, $"wizard-{Guid.NewGuid():N}.conf");
        }
    }
}
