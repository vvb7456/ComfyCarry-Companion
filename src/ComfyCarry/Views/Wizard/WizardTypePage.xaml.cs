using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using ComfyCarry.Services;
using ComfyCarry.Views;

namespace ComfyCarry.Views.Wizard;

public sealed partial class WizardTypePage : Page
{
    private LocalizationService L => App.Hub.Locale;

    public WizardTypePage()
    {
        this.InitializeComponent();
        LoadRadios();
        Localize();
    }

    private void LoadRadios()
    {
        TypeRadios.Items.Clear();
        int defaultIndex = 0;
        for (int i = 0; i < CloudTypeDefs.All.Count; i++)
        {
            var def = CloudTypeDefs.All[i];
            if (def.Type == WizardState.SelectedCloud) defaultIndex = i;

            var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            // Logo
            if (!string.IsNullOrEmpty(def.LogoPath))
            {
                var logoPath = Path.Combine(AppContext.BaseDirectory, def.LogoPath);
                if (File.Exists(logoPath))
                {
                    var img = new Image
                    {
                        Source = new BitmapImage(new Uri(logoPath)),
                        Width = 20,
                        Height = 20,
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    sp.Children.Add(img);
                }
            }
            sp.Children.Add(new TextBlock { Text = def.DisplayName, VerticalAlignment = VerticalAlignment.Center });

            var rb = new RadioButton { Content = sp, Tag = def };
            TypeRadios.Items.Add(rb);
        }
        // 手动 new 的 RadioButton 不会自动同步 RadioButtons.SelectedItem；
        // 这里显式设 SelectedIndex，触发 SelectionChanged → 同步 SelectedItem → 启用 Next。
        TypeRadios.SelectedIndex = defaultIndex;
        // 兜底（SelectedIndex 在某些极端时机可能未触发回调）
        UpdateNextEnabled();
    }

    private void Localize()
    {
        Lbl.Text = L.T("cloud.step.type");
        CancelBtn.Content = L.T("common.cancel");
        NextBtn.Content = L.T("common.next");
    }

    private void Type_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TypeRadios.SelectedItem is RadioButton rb && rb.Tag is CloudTypeDef def)
        {
            WizardState.SelectedCloud = def.Type;
        }
        UpdateNextEnabled();
    }

    private void UpdateNextEnabled()
    {
        NextBtn.IsEnabled = TypeRadios.SelectedItem is RadioButton rb && rb.Tag is CloudTypeDef;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        this.Frame?.Navigate(typeof(CloudHomePage));
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        // 命名/配置页会用到临时 conf，这里先准备好
        WizardState.EnsureTempConf();
        this.Frame?.Navigate(typeof(WizardNamePage));
    }
}
