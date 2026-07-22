using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ComfyCarry.Views.Wizard;
using ComfyCarry.Services;

namespace ComfyCarry.Views;

public sealed partial class CloudSetupPage : Page
{
    private LocalizationService L => App.Hub.Locale;

    // 步骤序号→（本地化 key）。0 表示首页（隐藏 StepBar）。
    private static readonly (Type Page, string Key)[] Steps =
    {
        (typeof(WizardTypePage),   "cloud.step.type"),
        (typeof(WizardNamePage),   "cloud.step.name"),
        (typeof(WizardConfigPage), "cloud.step.config"),
    };

    public CloudSetupPage()
    {
        this.InitializeComponent();
        HostFrame.Navigated += HostFrame_Navigated;
        HostFrame.Navigate(typeof(CloudHomePage));
        App.Hub.Settings.Changed += () => DispatcherQueue.TryEnqueue(RefreshStepBar);
    }

    private void HostFrame_Navigated(object sender, NavigationEventArgs e)
    {
        var t = e.SourcePageType;
        int idx = Array.FindIndex(Steps, s => s.Page == t);
        if (idx < 0)
        {
            // 首页或其它页：隐藏步骤条
            StepBar.Visibility = Visibility.Collapsed;
        }
        else
        {
            StepBar.Visibility = Visibility.Visible;
            SetStep(idx);
        }
    }

    private void SetStep(int idx)
    {
        StepBarPanel.Children.Clear();
        for (int i = 0; i < Steps.Length; i++)
        {
            var isCur = i == idx;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            var num = new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12),
                VerticalAlignment = VerticalAlignment.Center,
                Background = isCur ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
                                  : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ControlFillColorDefaultBrush"],
                Child = new TextBlock
                {
                    Text = (i + 1).ToString(),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = isCur ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"]
                                       : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                },
            };
            var label = new TextBlock
            {
                Text = L.T(Steps[i].Key),
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = isCur ? 1.0 : 0.6,
                FontWeight = isCur ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            };
            row.Children.Add(num);
            row.Children.Add(label);
            StepBarPanel.Children.Add(row);
        }
    }

    private void RefreshStepBar()
    {
        if (StepBar.Visibility == Visibility.Visible && HostFrame.Content is Page p)
        {
            int idx = Array.FindIndex(Steps, s => s.Page == p.GetType());
            if (idx >= 0) SetStep(idx);
        }
    }
}
