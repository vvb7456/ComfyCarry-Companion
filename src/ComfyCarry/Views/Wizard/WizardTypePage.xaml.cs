using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ComfyCarry.Services;

namespace ComfyCarry.Views.Wizard;

public sealed partial class WizardTypePage : Page
{
    private LocalizationService L => App.Hub.Locale;
    private static readonly Regex NameRegex = new("^[a-zA-Z0-9_-]+$", RegexOptions.Compiled);

    public WizardTypePage()
    {
        this.InitializeComponent();
        LoadRadios();
        Localize();
        NameBox.Text = WizardState.RemoteName;
        ProxyBox.Text = WizardState.Proxy;
    }

    private void LoadRadios()
    {
        TypeRadios.Items.Clear();
        foreach (var def in CloudTypeDefs.All)
        {
            var rb = new RadioButton { Content = def.DisplayName, Tag = def, IsChecked = def.Type == WizardState.SelectedCloud };
            TypeRadios.Items.Add(rb);
        }
    }

    private void Localize()
    {
        Lbl.Text = L.T("cloud.step.type");
        NameLabel.Text = L.T("cloud.remoteName");
        ProxyLabel.Text = L.T("cloud.proxy");
        ProxyHint.Text = L.T("cloud.proxy.hint");
        NextBtn.Content = L.T("common.ok");
    }

    private void Type_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TypeRadios.SelectedItem is RadioButton rb && rb.Tag is CloudTypeDef def)
        {
            WizardState.SelectedCloud = def.Type;
        }
    }

    private void Name_TextChanged(object sender, TextChangedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        WizardState.RemoteName = name;
        bool ok = name.Length > 0 && NameRegex.IsMatch(name);
        NameErr.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
        NameErr.Text = ok ? "" : "仅允许 a-z A-Z 0-9 _ -";
        NextBtn.IsEnabled = ok;
    }

    private void Proxy_TextChanged(object sender, TextChangedEventArgs e)
    {
        WizardState.Proxy = ProxyBox.Text.Trim();
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        // 准备临时 conf 路径
        var tempConf = Path.Combine(App.Hub.Paths.TempConfDir, $"wizard-{Guid.NewGuid():N}.conf");
        WizardState.TempConfPath = tempConf;
        // 跳到下一步（导航栏）
        if (this.Frame?.Parent is Frame f && f.Parent is NavigationView nv)
        {
            var items = nv.MenuItems.OfType<NavigationViewItem>().ToList();
            nv.SelectedItem = items.ElementAtOrDefault(1); // t_params
        }
    }
}
