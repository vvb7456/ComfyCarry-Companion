using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ComfyCarry.Services;

namespace ComfyCarry.Views.Wizard;

public sealed partial class WizardParamsPage : Page
{
    private LocalizationService L => App.Hub.Locale;

    public WizardParamsPage()
    {
        this.InitializeComponent();
        Localize();
        BuildFields();
    }

    private void Localize()
    {
        Lbl.Text = L.T("cloud.step.params");
        BackBtn.Content = "←";
        NextBtn.Content = L.T("common.ok");
    }

    private void BuildFields()
    {
        FieldsPanel.Children.Clear();
        var def = CloudTypeDefs.Get(WizardState.SelectedCloud);
        OAuthInfo.IsOpen = def.RequiresOAuth;
        foreach (var f in def.Fields)
        {
            var sp = new StackPanel { Spacing = 4 };
            var lbl = new TextBlock { Text = f.Label };
            sp.Children.Add(lbl);
            var box = new PasswordBox
            {
                PlaceholderText = f.Placeholder ?? "",
                Width = 420,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            if (WizardState.FieldValues.TryGetValue(f.Key, out var v)) box.Password = v;
            var key = f.Key;
            box.PasswordChanged += (s, e) => WizardState.FieldValues[key] = box.Password;
            sp.Children.Add(box);
            if (f.Help is { Length: > 0 })
            {
                sp.Children.Add(new TextBlock { Text = f.Help, Opacity = 0.6, TextWrapping = TextWrapping.Wrap, MaxWidth = 480 });
            }
            FieldsPanel.Children.Add(sp);
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Goto(0);
    private void Next_Click(object sender, RoutedEventArgs e)
    {
        var def = CloudTypeDefs.Get(WizardState.SelectedCloud);
        if (def.RequiresOAuth) Goto(2);
        else Goto(2); // OAuth 页对非 OAuth 类型会直接 create 并跳测试
    }

    private void Goto(int idx)
    {
        if (this.Frame?.Parent is Frame f && f.Parent is NavigationView nv)
        {
            var items = nv.MenuItems.OfType<NavigationViewItem>().ToList();
            nv.SelectedItem = items.ElementAtOrDefault(idx);
        }
    }
}
