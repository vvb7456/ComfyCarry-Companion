using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ComfyCarry.Models;
using ComfyCarry.Services;

namespace ComfyCarry.Views.Wizard;

public sealed partial class WizardConfigPage : Page
{
    private LocalizationService L => App.Hub.Locale;
    public ObservableCollection<ConfigChoiceVM> Choices { get; } = new();

    // 当前驱动结果（用于 Choice_Click 续跑）
    private ConfigDriveResult? _drive;

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
            Busy.IsActive = true;
            var def = CloudTypeDefs.Get(WizardState.SelectedCloud);
            var opts = BuildOptions(def);
            Status.Text = L.T("cloud.config.authorizing");
            var res = await App.Hub.Rclone.ConfigDriveAsync(
                WizardState.TempConfPath, WizardState.RemoteName, def.RcloneType, opts, ProxyFromSettings());
            _drive = res;
            ApplyDriveResult(res);
        }
        catch (Exception ex)
        {
            Status.Text = ex.Message;
        }
        finally { SaveBtn.IsEnabled = true; Busy.IsActive = false; }
    }

    // ---------- OAuth 类型：开始授权 ----------
    private async void Authorize_Click(object sender, RoutedEventArgs e)
    {
        var def = CloudTypeDefs.Get(WizardState.SelectedCloud);
        Busy.IsActive = true;
        AuthorizeBtn.IsEnabled = false;
        Status.Text = L.T("cloud.config.browserOpening");

        try
        {
            var opts = BuildOptions(def);
            if (def.Provider is { Length: > 0 }) opts["provider"] = def.Provider;

            var progress = new Progress<string>(OnDriveProgress);
            var res = await App.Hub.Rclone.ConfigDriveAsync(
                WizardState.TempConfPath, WizardState.RemoteName, def.RcloneType, opts,
                ProxyFromSettings(), progress);
            _drive = res;
            ApplyDriveResult(res);
        }
        catch (Exception ex)
        {
            Status.Text = ex.Message;
        }
        finally { Busy.IsActive = false; AuthorizeBtn.IsEnabled = true; }
    }

    /// <summary>驱动器进度回调：区分"开浏览器"与"登录完成"两段文案。</summary>
    private void OnDriveProgress(string tag)
    {
        if (tag == "browser")
            Status.Text = L.T("cloud.config.waitingLogin");
        else if (tag == "login_done")
            Status.Text = L.T("cloud.config.authorized");
    }

    /// <summary>把驱动结果落到 UI：done 显 Next、NeedChoice 填列表、Error 显文案。</summary>
    private void ApplyDriveResult(ConfigDriveResult res)
    {
        // 用 LastState 携带 IsDone 标志供 ApplyMode 复用
        WizardState.LastState = res.Outcome == ConfigDriveOutcome.Done
            ? new RcloneConfigState()   // State="" 即 IsDone
            : new RcloneConfigState { State = res.State };

        switch (res.Outcome)
        {
            case ConfigDriveOutcome.Done:
                ChoicesList.Visibility = Visibility.Collapsed;
                Status.Text = L.T("cloud.config.authorized");
                NextBtn.Visibility = Visibility.Visible;
                break;
            case ConfigDriveOutcome.NeedChoice:
                Status.Text = L.T("cloud.config.chooseDrive");
                FillChoices(res.Examples);
                ChoicesList.Visibility = Visibility.Visible;
                break;
            default: // Error
                ChoicesList.Visibility = Visibility.Collapsed;
                Status.Text = !string.IsNullOrEmpty(res.Message) ? res.Message : L.T("cloud.config.failed");
                break;
        }
    }

    private void FillChoices(IReadOnlyList<RcloneExample> examples)
    {
        Choices.Clear();
        for (int i = 0; i < examples.Count; i++)
        {
            var ex = examples[i];
            var label = !string.IsNullOrEmpty(ex.Help) ? ex.Help : $"{L.T("cloud.config.option")} {i + 1}";
            Choices.Add(new ConfigChoiceVM { Id = ex.Value, Label = label });
        }
    }

    private async void Choice_Click(object sender, RoutedEventArgs e)
    {
        if (_drive is not { Outcome: ConfigDriveOutcome.NeedChoice } drive) return;
        if (sender is not RadioButton rb || rb.Tag is not string id) return;
        // id 即 Example.Value
        var result = id;
        Status.Text = L.T("cloud.config.authorizing");
        Busy.IsActive = true;
        try
        {
            var def = CloudTypeDefs.Get(WizardState.SelectedCloud);
            var progress = new Progress<string>(OnDriveProgress);
            var next = await App.Hub.Rclone.ConfigDriveContinueAsync(
                WizardState.TempConfPath, WizardState.RemoteName, def.RcloneType, drive, result,
                ProxyFromSettings(), progress);
            _drive = next;
            ApplyDriveResult(next);
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
