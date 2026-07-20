using System.Diagnostics;
using System.Text;
using Microsoft.Win32;
using ComfyCarry.Services;

namespace ComfyCarry;

/// <summary>
/// 开机自启（注册表 HKCU\...\Run）。仅 Windows。
/// </summary>
public static class StartupHelper
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "ComfyCarryCompanion";

    public static void SetAutoStart(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key is null) return;
        if (enable)
        {
            var exe = Environment.ProcessPath!;
            key.SetValue(AppName, $"\"{exe}\"");
        }
        else
        {
            key.DeleteValue(AppName, false);
        }
    }

    public static bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(AppName) is not null;
        }
        catch { return false; }
    }
}
