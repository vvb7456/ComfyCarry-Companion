using ComfyCarry.Models;

namespace ComfyCarry.Views.Wizard;

/// <summary>整个向导的共享状态。</summary>
public static class WizardState
{
    public static CloudType SelectedCloud { get; set; } = CloudType.OneDrivePersonal;
    public static string RemoteName { get; set; } = "";
    public static string Proxy { get; set; } = "";
    public static Dictionary<string, string> FieldValues { get; } = new();
    public static RcloneConfigState? LastState { get; set; }
    public static string TempConfPath { get; set; } = "";
    public static bool TestedOk { get; set; }
}
