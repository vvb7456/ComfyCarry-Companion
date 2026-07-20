using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ComfyCarry.Services;

namespace ComfyCarry.Views.Wizard;

public sealed partial class WizardTestPage : Page
{
    private LocalizationService L => App.Hub.Locale;

    public WizardTestPage()
    {
        this.InitializeComponent();
        Localize();
    }

    private void Localize()
    {
        Lbl.Text = L.T("cloud.step.test");
        TestBtn.Content = L.T("common.test");
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        TestBtn.IsEnabled = false;
        Busy.IsActive = true;
        Result.Text = L.T("cloud.test.running");
        try
        {
            var (ok, msg) = await App.Hub.Rclone.LsdAsync(WizardState.TempConfPath, WizardState.RemoteName, WizardState.Proxy);
            WizardState.TestedOk = ok;
            Result.Text = ok ? "成功 ✓\n" + msg : "失败 ✗\n" + msg;
            NextBtn.IsEnabled = true;
        }
        catch (Exception ex)
        {
            Result.Text = "异常：" + ex.Message;
        }
        finally { Busy.IsActive = false; TestBtn.IsEnabled = true; }
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Goto(2);
    private void Next_Click(object sender, RoutedEventArgs e) => Goto(4);
    private void Goto(int idx)
    {
        if (this.Frame?.Parent is Frame f && f.Parent is NavigationView nv)
        {
            var items = nv.MenuItems.OfType<NavigationViewItem>().ToList();
            nv.SelectedItem = items.ElementAtOrDefault(idx);
        }
    }
}
