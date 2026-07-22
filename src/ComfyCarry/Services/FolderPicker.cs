using System.Runtime.InteropServices;

namespace ComfyCarry.Services;

/// <summary>
/// Win32 IFileDialog 文件夹选择器。
/// 用 FOS_PICKFOLDERS 替代 Windows.Storage.Pickers.FolderPicker，
/// 后者有已知 bug：初始文件夹"选择"按钮禁用，必须导航离开再回来。
/// </summary>
public static class FolderPicker
{
    private static readonly Guid ClsidFileOpenDialog = new("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");
    private static readonly Guid IidIFileOpenDialog = new("D57C7288-D4AD-4768-BE02-9D969532D960");
    private static readonly Guid IidIShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

    [Flags]
    private enum FOS : uint
    {
        FOS_PICKFOLDERS = 0x00000020,
        FOS_FORCEFILESYSTEM = 0x00000040,
        FOS_NOCHANGEDIR = 0x00000008,
    }

    private const uint SIGDN_FILESYSPATH = 0x80058000;

    [ComImport, Guid("D57C7288-D4AD-4768-BE02-9D969532D960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        [PreserveSig] int Show(IntPtr hwndOwner);
        void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise(IntPtr pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(uint fos);
        void GetOptions(out uint pfos);
        void SetDefaultFolder(IntPtr psi);
        void SetFolder(IntPtr psi);
        void GetFolder(out IntPtr ppsi);
        void GetCurrentSelection(out IntPtr ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IntPtr ppsi);
        void AddPlace(IntPtr psi, int fdap);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszExtension);
        void Close(int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr pFilter);
        void GetResults(out IntPtr ppenum);
        void GetSelectedItems(out IntPtr ppsai);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IntPtr ppsi);
        void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IntPtr psi, uint hint, out int piOrder);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName([MarshalAs(UnmanagedType.LPWStr)] string pszPath, IntPtr pbc, ref Guid riid, out IntPtr ppv);

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(ref Guid clsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid iid, out IntPtr ppv);

    /// <summary>打开文件夹选择对话框。返回选中路径，取消返回 null。</summary>
    public static string? PickFolder(IntPtr hwndOwner, string? title = null, string? startPath = null)
    {
        var clsid = ClsidFileOpenDialog;
        var iid = IidIFileOpenDialog;
        var hr = CoCreateInstance(ref clsid, IntPtr.Zero, 1, ref iid, out var pDialog);
        if (hr != 0) return null;

        var dlg = (IFileOpenDialog)Marshal.GetObjectForIUnknown(pDialog);
        try
        {
            // 设置选项：只选文件夹 + 限于文件系统
            dlg.GetOptions(out var options);
            dlg.SetOptions(options | (uint)(FOS.FOS_PICKFOLDERS | FOS.FOS_FORCEFILESYSTEM | FOS.FOS_NOCHANGEDIR));

            if (title is not null)
                dlg.SetTitle(title);

            // 设置初始文件夹
            if (!string.IsNullOrEmpty(startPath) && System.IO.Directory.Exists(startPath))
            {
                var iidShell = IidIShellItem;
                if (SHCreateItemFromParsingName(startPath, IntPtr.Zero, ref iidShell, out var pShellItem) == 0 && pShellItem != IntPtr.Zero)
                {
                    dlg.SetFolder(pShellItem);
                    Marshal.Release(pShellItem);
                }
            }

            hr = dlg.Show(hwndOwner);
            if (hr != 0) return null; // 用户取消或出错

            dlg.GetResult(out var pResult);
            if (pResult == IntPtr.Zero) return null;

            try
            {
                var item = (IShellItem)Marshal.GetObjectForIUnknown(pResult);
                item.GetDisplayName(SIGDN_FILESYSPATH, out var path);
                return path;
            }
            finally
            {
                Marshal.Release(pResult);
            }
        }
        finally
        {
            Marshal.Release(pDialog);
        }
    }
}
