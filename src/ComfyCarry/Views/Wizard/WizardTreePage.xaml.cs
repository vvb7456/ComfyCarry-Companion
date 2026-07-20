using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ComfyCarry.Services;

namespace ComfyCarry.Views.Wizard;

public sealed partial class WizardTreePage : Page
{
    private LocalizationService L => App.Hub.Locale;

    public WizardTreePage()
    {
        this.InitializeComponent();
        Localize();
    }

    private void Localize()
    {
        Lbl.Text = L.T("cloud.step.tree");
        Hint.Text = L.T("cloud.tree.hint");
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (!DoTree.IsOn) { Goto(5); return; }
        RunBtn.IsEnabled = false;
        Busy.IsActive = true;
        Result.Text = "正在创建…";
        try
        {
            int code = await App.Hub.Rclone.CreateStandardTreeAsync(WizardState.TempConfPath, WizardState.RemoteName, WizardState.Proxy);
            Result.Text = code == 0 ? "完成 ✓" : $"部分失败 (exit {code})";
        }
        catch (Exception ex) { Result.Text = "异常：" + ex.Message; }
        finally { Busy.IsActive = false; RunBtn.IsEnabled = true; }
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Goto(3);
    private void Next_Click(object sender, RoutedEventArgs e) => Goto(5);
    private void Goto(int idx)
    {
        if (this.Frame?.Parent is Frame f && f.Parent is NavigationView nv)
        {
            var items = nv.MenuItems.OfType<NavigationViewItem>().ToList();
            nv.SelectedItem = items.ElementAtOrDefault(idx);
        }
    }
}
