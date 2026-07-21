using System;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinRT;

namespace ComfyCarry;

public static class Program
{
    // 持有到进程结束，保证单实例互斥不被提前释放。
    private static Mutex? _singleInstanceMutex;

    [STAThread]
    static void Main(string[] args)
    {
        _singleInstanceMutex = new Mutex(true, "Global\\ComfyCarryCompanion_Single_3F2A", out bool createdNew);
        if (!createdNew)
        {
            return;
        }

        // InitializeComWrappers 必须在任何 WinRT 调用之前。
        ComWrappersSupport.InitializeComWrappers();

        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(
                DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);

            _ = new App();
        });
    }
}
