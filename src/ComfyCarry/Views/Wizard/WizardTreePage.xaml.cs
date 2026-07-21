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
        DoTree.Header = L.T("cloud.tree.toggle");
        RunBtn.Content = L.T("cloud.tree.run");
        BackBtn.Content = L.T("common.back");
        NextBtn.Content = L.T("common.next");
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (!DoTree.IsOn)
        {
            this.Frame?.Navigate(typeof(WizardExportPage));
            return;
        }
        RunBtn.IsEnabled = false;
        Busy.IsActive = true;
        Result.Text = L.T("cloud.tree.running");
        try
        {
            int code = await App.Hub.Rclone.CreateStandardTreeAsync(
                WizardState.TempConfPath, WizardState.RemoteName, App.Hub.Settings.Data.Proxy);
            Result.Text = code == 0 ? L.T("cloud.tree.ok") : $"{L.T("cloud.tree.partial")} (exit {code})";
        }
        catch (Exception ex) { Result.Text = ex.Message; }
        finally { Busy.IsActive = false; RunBtn.IsEnabled = true; }
    }

    private void Back_Click(object sender, RoutedEventArgs e) => this.Frame?.GoBack();
    private void Next_Click(object sender, RoutedEventArgs e) => this.Frame?.Navigate(typeof(WizardExportPage));
}
