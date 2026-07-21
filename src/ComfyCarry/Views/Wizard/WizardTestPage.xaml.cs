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
        NextBtn.IsEnabled = WizardState.TestedOk;
    }

    private void Localize()
    {
        Lbl.Text = L.T("cloud.step.test");
        TestBtn.Content = L.T("common.test");
        BackBtn.Content = L.T("common.back");
        NextBtn.Content = L.T("common.next");
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        TestBtn.IsEnabled = false;
        Busy.IsActive = true;
        Result.Text = L.T("cloud.test.running");
        try
        {
            var (ok, msg) = await App.Hub.Rclone.LsdAsync(
                WizardState.TempConfPath, WizardState.RemoteName, App.Hub.Settings.Data.Proxy);
            WizardState.TestedOk = ok;
            Result.Text = ok ? $"{L.T("cloud.test.ok")}\n{msg}" : $"{L.T("cloud.test.fail")}\n{msg}";
            NextBtn.IsEnabled = ok;
        }
        catch (Exception ex)
        {
            Result.Text = ex.Message;
        }
        finally { Busy.IsActive = false; TestBtn.IsEnabled = true; }
    }

    private void Back_Click(object sender, RoutedEventArgs e) => this.Frame?.GoBack();
    private void Next_Click(object sender, RoutedEventArgs e) => this.Frame?.Navigate(typeof(WizardTreePage));
}
