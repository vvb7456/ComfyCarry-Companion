using WinRT.Interop;
using Microsoft.UI;
using Microsoft.UI.Windowing;

namespace ComfyCarry;

/// <summary>
/// Window 显示/隐藏的互操作封装。
/// net10 的 Window 已原生支持 Hide/Show/Restore，这里只补 BringToFront。
/// </summary>
internal static class WindowExtensions
{
    public static Microsoft.UI.Windowing.AppWindow GetAppWindow(this Microsoft.UI.Xaml.Window w)
    {
        var hwnd = WindowNative.GetWindowHandle(w);
        var id = Win32Interop.GetWindowIdFromWindow(hwnd);
        return Microsoft.UI.Windowing.AppWindow.GetWindowFromWindowId(id);
    }
}
