using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinRT;

namespace ComfyCarry;

public static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ComWrappersSupport.InitializeComWrappers();

        // 单实例：已有实例则退出本进程
        using var mutex = new Mutex(true, "Global\\ComfyCarryCompanion_Single_3F2A", out bool createdNew);
        if (!createdNew)
        {
            return;
        }

        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()!);
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }
}
