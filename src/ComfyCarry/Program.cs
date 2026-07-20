using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinRT;
using ComfyCarry.Services;

namespace ComfyCarry;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        ComWrappersSupport.InitializeComWrappers();

        // 单实例（SPEC §3.1）：若已有实例，让它前台显示并退出本进程。
        if (!SingleInstance.Acquire())
        {
            RedirectExistingInstance();
            return 0;
        }

        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()!);
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
        return 0;
    }

    private static void RedirectExistingInstance()
    {
        try
        {
            // 向已运行实例发信号让其窗口前台显示。
            EventWaitHandle.TryOpenExisting("Global\\ComfyCarryCompanion_Show_3F2A", out var ev);
            ev?.Set();
        }
        catch { /* ignore */ }
    }
}
