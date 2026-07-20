using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using ComfyCarry.Services;

namespace ComfyCarry.Views;

public sealed partial class ArtifactListView : UserControl
{
    public ObservableCollection<RemoteEntryVM> Items { get; } = new();

    public ArtifactListView()
    {
        this.InitializeComponent();
        Repeater.ItemsSource = Items;
    }

    public async Task LoadAsync(PanelInstance inst, string remotePath)
    {
        Items.Clear();
        Busy.IsActive = true;
        try
        {
            var entries = await App.Hub.Rclone.LsfAsync(inst, remotePath);
            // 缩略图：图片用 WebDAV GET（带 basic auth，简化为占位——rclone lsf 不给缩略图，
            // 完整缩略图需带认证的 HttpClient 拉，二期补）
            foreach (var e in entries)
            {
                var vm = new RemoteEntryVM { Name = e.Name, IsDir = e.IsDir, Size = e.Size };
                Items.Add(vm);
            }
        }
        catch { /* ignore */ }
        finally { Busy.IsActive = false; }
    }
}

public sealed class RemoteEntryVM
{
    public string Name { get; set; } = "";
    public bool IsDir { get; set; }
    public long Size { get; set; }
    public BitmapImage? Thumb { get; set; }
}
