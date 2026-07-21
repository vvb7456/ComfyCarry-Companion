using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ComfyCarry.Services;

namespace ComfyCarry.Views.Wizard;

public sealed partial class WizardConfigPage : Page
{
    private LocalizationService L => App.Hub.Locale;
    public ObservableCollection<ConfigChoiceVM> Choices { get; } = new();

    public WizardConfigPage()
    {
        this.InitializeComponent();
        ChoicesList.ItemsSource = Choices;
        Localize();
        ApplyMode();
    }

    private void Localize()
    {
        Lbl.Text = L.T("cloud.step.config");
        BackBtn.Content = L.T("common.back");
        NextBtn.Content = L.T("common.next");
        SaveBtn.Content = L.T("cloud.config.save");
        AuthorizeBtn.Content = L.T("cloud.config.authorize");
        OAuthHint.Text = L.T("cloud.config.oauthHint");
    }

    /// <summary>按当前选中类型切换两块面板（v-if）。</summary>
    private void ApplyMode()
    {
        var def = CloudTypeDefs.Get(WizardState.SelectedCloud);
        if (def.RequiresOAuth)
        {
            OAuthPanel.Visibility = Visibility.Visible;
            ParamsPanel.Visibility = Visibility.Collapsed;
            SaveBtn.Visibility = Visibility.Collapsed;
            AuthorizeBtn.Visibility = Visibility.Visible;
            // OAuth 成功后才启用下一步
            NextBtn.Visibility = WizardState.LastState?.IsDone == true ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            OAuthPanel.Visibility = Visibility.Collapsed;
            ParamsPanel.Visibility = Visibility.Visible;
            SaveBtn.Visibility = Visibility.Visible;
            AuthorizeBtn.Visibility = Visibility.Collapsed;
            BuildFields();
            // 非参数类型若已写入 conf，直接显示下一步
            NextBtn.Visibility = WizardState.LastState?.IsDone == true ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private string ProxyFromSettings() => App.Hub.Settings.Data.Proxy ?? "";

    private void BuildFields()
    {
        FieldsPanel.Children.Clear();
        var def = CloudTypeDefs.Get(WizardState.SelectedCloud);
        foreach (var f in def.Fields)
        {
            var sp = new StackPanel { Spacing = 4 };
            FrameworkElement input;
            if (f.IsSecret)
            {
                var box = new PasswordBox
                {
                    PlaceholderText = f.Placeholder ?? "",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                if (WizardState.FieldValues.TryGetValue(f.Key, out var v)) box.Password = v;
                var key = f.Key;
                box.PasswordChanged += (s, e) => WizardState.FieldValues[key] = box.Password;
                input = box;
            }
            else
            {
                var box = new TextBox
                {
                    PlaceholderText = f.Placeholder ?? "",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                if (WizardState.FieldValues.TryGetValue(f.Key, out var v)) box.Text = v;
                var key = f.Key;
                box.TextChanged += (s, e) => WizardState.FieldValues[key] = box.Text;
                input = box;
            }
            // Header 用统一 Header（TextBox/PasswordBox 都有 Header 属性，分别设置）
            if (input is TextBox tb) tb.Header = f.Label;
            else if (input is PasswordBox pb) pb.Header = f.Label;
            sp.Children.Add(input);
            if (f.Help is { Length: > 0 })
            {
                sp.Children.Add(new TextBlock { Text = f.Help, Opacity = 0.6, TextWrapping = TextWrapping.Wrap });
            }
            FieldsPanel.Children.Add(sp);
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e) => this.Frame?.GoBack();

    private void Next_Click(object sender, RoutedEventArgs e) => this.Frame?.Navigate(typeof(WizardTestPage));

    // ---------- 非参数类型：保存配置 ----------
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveBtn.IsEnabled = false;
            var def = CloudTypeDefs.Get(WizardState.SelectedCloud);
            var opts = BuildOptions(def);
            var st = await App.Hub.Rclone.ConfigCreateAsync(
                WizardState.TempConfPath, WizardState.RemoteName, def.RcloneType, opts, ProxyFromSettings());
            WizardState.LastState = st;
            if (st.IsDone)
            {
                NextBtn.Visibility = Visibility.Visible;
            }
            else
            {
                Status.Text = st.Error is { Length: > 0 } ? st.Error : L.T("cloud.config.failed");
            }
        }
        catch (Exception ex)
        {
            Status.Text = ex.Message;
        }
        finally { SaveBtn.IsEnabled = true; }
    }

    // ---------- OAuth 类型：开始授权 ----------
    private async void Authorize_Click(object sender, RoutedEventArgs e)
    {
        var def = CloudTypeDefs.Get(WizardState.SelectedCloud);
        Busy.IsActive = true;
        AuthorizeBtn.IsEnabled = false;
        Status.Text = L.T("cloud.config.authorizing");

        try
        {
            var opts = BuildOptions(def);
            if (def.Provider is { Length: > 0 }) opts["provider"] = def.Provider;

            var st = await App.Hub.Rclone.ConfigCreateAsync(
                WizardState.TempConfPath, WizardState.RemoteName, def.RcloneType, opts, ProxyFromSettings());
            WizardState.LastState = st;

            if (st.IsDone)
            {
                Status.Text = L.T("cloud.config.authorized");
                NextBtn.Visibility = Visibility.Visible;
            }
            else if (st.Choices.Count > 0)
            {
                Status.Text = L.T("cloud.config.chooseDrive");
                Choices.Clear();
                int idx = 0;
                foreach (var ch in st.Choices)
                {
                    var label = ch.TryGetValue("Name", out var n) ? n.GetString() : $"{L.T("cloud.config.option")} {idx + 1}";
                    Choices.Add(new ConfigChoiceVM { Id = idx.ToString(), Label = label });
                    idx++;
                }
                ChoicesList.Visibility = Visibility.Visible;
            }
            else if (st.Error is { Length: > 0 })
            {
                Status.Text = st.Error;
            }
            else
            {
                Status.Text = L.T("cloud.config.authorized");
                NextBtn.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            Status.Text = ex.Message;
        }
        finally { Busy.IsActive = false; AuthorizeBtn.IsEnabled = true; }
    }

    private async void Choice_Click(object sender, RoutedEventArgs e)
    {
        if (WizardState.LastState is not { } st) return;
        if (sender is not RadioButton rb || rb.Tag is not string idStr) return;
        if (!int.TryParse(idStr, out var idx) || idx >= st.Choices.Count) return;
        var chosen = st.Choices[idx];
        var result = chosen.TryGetValue("Result", out var r) ? r.GetString() : idx.ToString();
        Status.Text = L.T("cloud.config.authorizing");
        Busy.IsActive = true;
        try
        {
            var next = await App.Hub.Rclone.ConfigContinueAsync(
                WizardState.TempConfPath, WizardState.RemoteName, st.State, result, ProxyFromSettings());
            WizardState.LastState = next;
            if (next.IsDone)
            {
                ChoicesList.Visibility = Visibility.Collapsed;
                Status.Text = L.T("cloud.config.authorized");
                NextBtn.Visibility = Visibility.Visible;
            }
            else if (next.Choices.Count > 0)
            {
                Choices.Clear();
                int i = 0;
                foreach (var ch in next.Choices)
                {
                    var label = ch.TryGetValue("Name", out var n) ? n.GetString() : $"{L.T("cloud.config.option")} {i + 1}";
                    Choices.Add(new ConfigChoiceVM { Id = i.ToString(), Label = label });
                    i++;
                }
            }
            else
            {
                Status.Text = L.T("cloud.config.authorized");
                NextBtn.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex) { Status.Text = ex.Message; }
        finally { Busy.IsActive = false; }
    }

    private static Dictionary<string, string> BuildOptions(CloudTypeDef def)
    {
        var opts = new Dictionary<string, string>();
        foreach (var f in def.Fields)
        {
            if (WizardState.FieldValues.TryGetValue(f.Key, out var v) && !string.IsNullOrEmpty(v))
                opts[f.Key] = v;
        }
        return opts;
    }
}

public sealed class ConfigChoiceVM
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
}
