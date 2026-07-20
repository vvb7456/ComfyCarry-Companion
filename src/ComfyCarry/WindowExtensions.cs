using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using WinRT.Interop;

namespace ComfyCarry;

/// <summary>
/// Window 显示/隐藏的互操作封装。
/// WinUI 3 的 Window 没有直接 Hide/Show，需通过 HWND 调 ShowWindow。
/// </summary>
internal static class WindowExtensions
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int SW_HIDE = 0;
    private const int SW_SHOWNORMAL = 1;
    private const int SW_RESTORE = 9;

    public static IntPtr GetHwnd(this Microsoft.UI.Xaml.Window w)
        => WindowNative.GetWindowHandle(w);

    public static AppWindow GetAppWindow(this Microsoft.UI.Xaml.Window w)
    {
        var hwnd = w.GetHwnd();
        var id = Win32Interop.GetWindowIdFromWindow(hwnd);
        return AppWindow.GetWindowFromWindowId(id);
    }

    public static void Hide(this Microsoft.UI.Xaml.Window w)
        => ShowWindow(w.GetHwnd(), SW_HIDE);

    public static void ShowNormal(this Microsoft.UI.Xaml.Window w)
        => ShowWindow(w.GetHwnd(), SW_SHOWNORMAL);

    public static void Restore(this Microsoft.UI.Xaml.Window w)
        => ShowWindow(w.GetHwnd(), SW_RESTORE);

    public static void BringToFront(this Microsoft.UI.Xaml.Window w)
        => SetForegroundWindow(w.GetHwnd());
}
