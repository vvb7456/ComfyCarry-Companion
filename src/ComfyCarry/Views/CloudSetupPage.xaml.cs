using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ComfyCarry.Views.Wizard;
using ComfyCarry.Services;

namespace ComfyCarry.Views;

public sealed partial class CloudSetupPage : Page
{
    private LocalizationService L => App.Hub.Locale;
    public CloudSetupPage()
    {
        this.InitializeComponent();
        Localize();
        if (!App.Hub.Rclone.IsPresent) RcloneMissingBar.IsOpen = true;
        App.Hub.Settings.Changed += () => DispatcherQueue.TryEnqueue(Localize);
    }

    private void Localize()
    {
        TitleText.Text = L.T("cloud.title");
        IntroText.Text = L.T("cloud.intro");
        StartBtnLabel.Text = L.T("cloud.newRemote");
        S1Label.Text = L.T("cloud.step.type");
        S2Label.Text = L.T("cloud.step.params");
        S3Label.Text = L.T("cloud.step.oauth");
        S4Label.Text = L.T("cloud.step.test");
        S5Label.Text = L.T("cloud.step.tree");
        S6Label.Text = L.T("cloud.step.export");
        RcloneMissingBar.Message = L.T("cloud.rcloneMissing");
    }

    private void StartWizard_Click(object sender, RoutedEventArgs e)
    {
        WizardNav.Visibility = Visibility.Visible;
        WizardNav.SelectedItem = WizardNav.MenuItems.OfType<NavigationViewItem>().First();
        WizardFrame.Navigate(typeof(WizardTypePage));
    }

    private void Wizard_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem nvi && nvi.Tag is string tag)
        {
            Type page = tag switch
            {
                "t_type" => typeof(WizardTypePage),
                "t_params" => typeof(WizardParamsPage),
                "t_oauth" => typeof(WizardOAuthPage),
                "t_test" => typeof(WizardTestPage),
                "t_tree" => typeof(WizardTreePage),
                "t_export" => typeof(WizardExportPage),
                _ => typeof(WizardTypePage),
            };
            WizardFrame.Navigate(page);
        }
    }
}

