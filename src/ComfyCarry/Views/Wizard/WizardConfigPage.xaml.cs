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
        NextBtn.Content = L.T("common.finish");
        SaveBtn.Content = L.T("cloud.config.save");
        AuthorizeBtn.Content = L.T("cloud.config.authorize");
        OAuthHint.Text = L.T("cloud.config.oauthHint");
        CreateTreeCheck.Content = L.T("cloud.config.createTree");
        ChoiceHint.Text = L.T("cloud.config.chooseDrive");
    }

    private void ApplyMode()
    {
        var def = CloudTypeDefs.Get(WizardState.SelectedCloud);
        Status.Text = "";
        Status.Visibility = Visibility.Collapsed;
        Busy.Visibility = Visibility.Collapsed;
        ChoicePanel.Visibility = Visibility.Collapsed;
        Choices.Clear();
        NextBtn.IsEnabled = false;
        CreateTreeCheck.IsChecked = WizardState.CreateTree;
        CreateTreeCheck.Visibility = Visibility.Visible;

        if (def.RequiresOAuth)
        {
            OAuthPanel.Visibility = Visibility.Visible;
            ParamsPanel.Visibility = Visibility.Collapsed;
            OAuthHint.Visibility = Visibility.Visible;
            AuthorizeBtn.Visibility = Visibility.Visible;
            AuthorizeBtn.IsEnabled = true;
            AuthorizeBtn.Content = L.T("cloud.config.authorize");
        }
        else
        {
            OAuthPanel.Visibility = Visibility.Collapsed;
            ParamsPanel.Visibility = Visibility.Visible;
            SaveBtn.Visibility = Visibility.Visible;
            SaveBtn.IsEnabled = true;
            BuildFields();
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
                var box = new PasswordBox { PlaceholderText = f.Placeholder ?? "", HorizontalAlignment = HorizontalAlignment.Stretch };
                if (WizardState.FieldValues.TryGetValue(f.Key, out var v)) box.Password = v;
                var key = f.Key;
                box.PasswordChanged += (s, e) => WizardState.FieldValues[key] = box.Password;
                input = box;
            }
            else
            {
                var box = new TextBox { PlaceholderText = f.Placeholder ?? "", HorizontalAlignment = HorizontalAlignment.Stretch };
                if (WizardState.FieldValues.TryGetValue(f.Key, out var v)) box.Text = v;
                var key = f.Key;
                box.TextChanged += (s, e) => WizardState.FieldValues[key] = box.Text;
                input = box;
            }
            if (input is TextBox tb) tb.Header = f.Label;
            else if (input is PasswordBox pb) pb.Header = f.Label;
            sp.Children.Add(input);
            if (f.Help is { Length: > 0 })
                sp.Children.Add(new TextBlock { Text = f.Help, Opacity = 0.6, TextWrapping = TextWrapping.Wrap });
            FieldsPanel.Children.Add(sp);
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        WizardState.LastState = null;
        _drive = null;
        try { if (File.Exists(WizardState.TempConfPath)) File.Delete(WizardState.TempConfPath); } catch { }
        WizardState.TempConfPath = "";
        this.Frame?.GoBack();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        this.Frame?.Navigate(typeof(CloudHomePage));
    }

    // ---------- 非 OAuth: 保存配置 ----------
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        SaveBtn.IsEnabled = false;
        SetBusy(true, L.T("cloud.config.authenticating"));
        try
        {
            var def = CloudTypeDefs.Get(WizardState.SelectedCloud);
            var opts = BuildOptions(def);
            var res = await App.Hub.Rclone.ConfigDriveAsync(
                WizardState.TempConfPath, WizardState.RemoteName, def.RcloneType, opts, ProxyFromSettings(),
                autoAnswers: def.AutoAnswers);
            _drive = res;
            await HandleDriveResult(res);
        }
        catch (Exception ex) { SetError(ex.Message); }
        finally { SetBusy(false); SaveBtn.IsEnabled = true; }
    }

    // ---------- OAuth: 开始授权 ----------
    private async void Authorize_Click(object sender, RoutedEventArgs e)
    {
        AuthorizeBtn.IsEnabled = false;
        OAuthHint.Visibility = Visibility.Collapsed;
        SetBusy(true, L.T("cloud.config.browserOpening"));
        try
        {
            var def = CloudTypeDefs.Get(WizardState.SelectedCloud);
            var opts = BuildOptions(def);
            if (def.Provider is { Length: > 0 }) opts["provider"] = def.Provider;
            var progress = new Progress<string>(tag =>
            {
                if (tag == "browser") SetStatus(L.T("cloud.config.waitingLogin"));
            });
            var res = await App.Hub.Rclone.ConfigDriveAsync(
                WizardState.TempConfPath, WizardState.RemoteName, def.RcloneType, opts,
                ProxyFromSettings(), progress, autoAnswers: def.AutoAnswers);
            _drive = res;
            await HandleDriveResult(res);
        }
        catch (Exception ex) { SetError(ex.Message); }
        finally { SetBusy(false); }
    }

    // ---------- 选择列表点击 ----------
    private async void Choice_Click(object sender, RoutedEventArgs e)
    {
        if (_drive is not { Outcome: ConfigDriveOutcome.NeedChoice } drive) return;
        if (sender is not RadioButton rb || rb.Tag is not string id) return;
        SetBusy(true, L.T("cloud.config.authenticating"));
        try
        {
            var def = CloudTypeDefs.Get(WizardState.SelectedCloud);
            var next = await App.Hub.Rclone.ConfigDriveContinueAsync(
                WizardState.TempConfPath, WizardState.RemoteName, def.RcloneType, drive, id,
                ProxyFromSettings(), autoAnswers: def.AutoAnswers);
            _drive = next;
            await HandleDriveResult(next);
        }
        catch (Exception ex) { SetError(ex.Message); }
        finally { SetBusy(false); }
    }

    // ---------- 统一结果处理 ----------
    private async Task HandleDriveResult(ConfigDriveResult res)
    {
        WizardState.LastState = res.Outcome == ConfigDriveOutcome.Done
            ? new RcloneConfigState() : new RcloneConfigState { State = res.State };

        switch (res.Outcome)
        {
            case ConfigDriveOutcome.Done:
                ChoicePanel.Visibility = Visibility.Collapsed;
                CreateTreeCheck.Visibility = Visibility.Collapsed;
                // 自动验证 + 建目录 + 合并
                await PostConfigFlow();
                break;
            case ConfigDriveOutcome.NeedChoice:
                OAuthHint.Visibility = Visibility.Collapsed;
                AuthorizeBtn.Visibility = Visibility.Collapsed;
                SaveBtn.Visibility = Visibility.Collapsed;
                ChoiceHint.Text = L.T("cloud.config.chooseDrive");
                FillChoices(res.Examples);
                ChoicePanel.Visibility = Visibility.Visible;
                SetBusy(false);
                break;
            default:
                SetError(res.Message);
                // 显示重试按钮
                if (CloudTypeDefs.Get(WizardState.SelectedCloud).RequiresOAuth)
                {
                    AuthorizeBtn.Visibility = Visibility.Visible;
                    AuthorizeBtn.Content = L.T("cloud.config.retry");
                    AuthorizeBtn.IsEnabled = true;
                }
                else
                {
                    SaveBtn.Visibility = Visibility.Visible;
                    SaveBtn.IsEnabled = true;
                }
                break;
        }
    }

    /// <summary>配置完成后的自动流程：验证连接 → 建目录 → 合并 conf</summary>
    private async Task PostConfigFlow()
    {
        WizardState.CreateTree = CreateTreeCheck.IsChecked == true;
        SetBusy(true, L.T("cloud.config.verifying"));

        bool testOk = false;
        string testMsg = "";
        try
        {
            var (ok, msg) = await App.Hub.Rclone.LsdAsync(
                WizardState.TempConfPath, WizardState.RemoteName, ProxyFromSettings());
            testOk = ok;
            testMsg = msg;
        }
        catch (Exception ex) { testMsg = ex.Message; }

        bool treeOk = false;
        if (testOk && WizardState.CreateTree)
        {
            try
            {
                int code = await App.Hub.Rclone.CreateStandardTreeAsync(
                    WizardState.TempConfPath, WizardState.RemoteName, ProxyFromSettings());
                treeOk = code == 0;
            }
            catch { }
        }

        // 合并到 app conf
        MergeIntoAppConf();

        SetBusy(false);

        if (testOk)
        {
            var msg = L.T("cloud.config.verifyOk");
            if (treeOk) msg += "\n" + L.T("cloud.config.treeOk");
            SetStatus(msg);
        }
        else
        {
            SetStatus(L.T("cloud.config.verifyWarn") + "\n" + testMsg + "\n" + L.T("cloud.config.verifyWarnHint"));
        }
        NextBtn.IsEnabled = true;
    }

    private void MergeIntoAppConf()
    {
        try
        {
            if (!File.Exists(WizardState.TempConfPath)) return;
            var tempText = File.ReadAllText(WizardState.TempConfPath);
            var appConf = App.Hub.Paths.AppRcloneConf;
            var appText = File.Exists(appConf) ? File.ReadAllText(appConf) : "";
            var tempRemotes = ParseRemotes(tempText);
            var appRemotes = ParseRemotes(appText);
            foreach (var kv in tempRemotes) appRemotes[kv.Key] = kv.Value;
            var sb = new System.Text.StringBuilder();
            foreach (var kv in appRemotes)
            {
                sb.AppendLine($"[{kv.Key}]");
                sb.AppendLine(kv.Value.TrimEnd('\r', '\n'));
                sb.AppendLine();
            }
            File.WriteAllText(appConf, sb.ToString());
        }
        catch { }
    }

    private static Dictionary<string, string> ParseRemotes(string conf)
    {
        var result = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(conf)) return result;
        var lines = conf.Split('\n');
        string? curName = null;
        var curBody = new System.Text.StringBuilder();
        foreach (var ln in lines)
        {
            var t = ln.Trim();
            if (t.StartsWith("[") && t.EndsWith("]"))
            {
                if (curName is not null) result[curName] = curBody.ToString();
                curName = t.Substring(1, t.Length - 2);
                curBody.Clear();
            }
            else if (curName is not null) curBody.AppendLine(ln);
        }
        if (curName is not null) result[curName] = curBody.ToString();
        return result;
    }

    // ---------- UI 辅助 ----------
    private void SetBusy(bool active, string? text = null)
    {
        Busy.IsActive = active;
        Busy.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        if (text is not null) SetStatus(text);
    }

    private void SetStatus(string text)
    {
        Status.Text = text;
        Status.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetError(string text)
    {
        SetBusy(false);
        ChoicePanel.Visibility = Visibility.Collapsed;
        SetStatus(text);
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
