using System.Diagnostics;
using System.Threading;

namespace ComfyCarry.Services;

/// <summary>
/// 单实例保证（SPEC §3.1）。第二个实例启动时把已运行实例窗口提到前台并退出。
/// </summary>
public static class SingleInstance
{
    private static Mutex? _mutex;
    private const string MutexName = "Global\\ComfyCarryCompanion_SingleInstance_3F2A";

    public static bool Acquire()
    {
        _mutex = new Mutex(initiallyOwned: true, name: MutexName, out bool createdNew);
        if (!createdNew)
        {
            // 已有实例在跑，提示前台实例显示窗口
            try
            {
                // 简单做法：写一个命名管道/事件通知——这里用文件信号占位。
                // 在 Windows 上更稳妥是 NamedPipeServerThread，本占位仅保证不并跑两个。
            }
            catch (Exception ex) { Debug.WriteLine($"[SingleInstance] notify: {ex}"); }
            return false;
        }
        return true;
    }
}
