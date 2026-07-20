using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ComfyCarry.Services;
using ComfyCarry.Models;

namespace ComfyCarry.Views.Wizard;

public sealed partial class WizardOAuthPage : Page
{
    private LocalizationService L => App.Hub.Locale;
    public ObservableCollection<ConfigChoiceVM> Choices { get; } = new();

    public WizardOAuthPage()
    {
        this.InitializeComponent();
        ChoicesList.ItemsSource = Choices;
        Localize();
    }

    private void Localize()
    {
        Lbl.Text = L.T("cloud.step.oauth");
        AuthorizeBtn.Content = L.T("cloud.step.oauth");
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Goto(1);

    private async void Authorize_Click(object sender, RoutedEventArgs e)
    {
        var def = CloudTypeDefs.Get(WizardState.SelectedCloud);
        Busy.IsActive = true;
        AuthorizeBtn.IsEnabled = false;
        Status.Text = "正在调用 rclone，请稍候…";

        try
        {
            if (!def.RequiresOAuth)
            {
                // 非 OAuth：直接 config create 写入临时 conf
                var opts = BuildOptions(def);
                var st = await App.Hub.Rclone.ConfigCreateAsync(
                    WizardState.TempConfPath, WizardState.RemoteName, def.RcloneType, opts, WizardState.Proxy);
                WizardState.LastState = st;
                Status.Text = "配置已写入。";
                NextBtn.Visibility = Visibility.Visible;
                Busy.IsActive = false;
                AuthorizeBtn.IsEnabled = true;
                return;
            }

            // OAuth 类型：先 config create（rclone 会开浏览器完成授权并写 conf）
            var opts2 = BuildOptions(def);
            // provider 作为选项
            if (def.Provider is { Length: > 0 }) opts2["provider"] = def.Provider;

            var st2 = await App.Hub.Rclone.ConfigCreateAsync(
                WizardState.TempConfPath, WizardState.RemoteName, def.RcloneType, opts2, WizardState.Proxy);
            WizardState.LastState = st2;

            if (st2.IsDone)
            {
                Status.Text = "授权完成，配置已写入。";
                NextBtn.Visibility = Visibility.Visible;
            }
            else if (st2.Choices.Count > 0)
            {
                // OneDrive 选 drive：呈现结构化选项
                Status.Text = "请选择一个 Drive（OneDrive 需要选 drive_type / drive_id）：";
                Choices.Clear();
                int idx = 0;
                foreach (var ch in st2.Choices)
                {
                    var label = ch.TryGetValue("Name", out var n) ? n.GetString() : $"选项 {idx + 1}";
                    var id = idx.ToString();
                    Choices.Add(new ConfigChoiceVM { Id = id, Label = label });
                    idx++;
                }
                ChoicesList.Visibility = Visibility.Visible;
            }
            else if (!string.IsNullOrEmpty(st2.State))
            {
                // 需要继续状态机（其它类型）
                Status.Text = $"状态机需继续：state={st2.State}";
            }
            else if (st2.Error is { Length: > 0 })
            {
                Status.Text = "错误：" + st2.Error;
            }
            else
            {
                Status.Text = "完成。";
                NextBtn.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            Status.Text = "异常：" + ex.Message;
        }
        finally
        {
            Busy.IsActive = false;
            AuthorizeBtn.IsEnabled = true;
        }
    }

    private async void Choice_Click(object sender, RoutedEventArgs e)
    {
        // 用户选了某个 drive 选项 → 回灌 rclone 状态机
        if (WizardState.LastState is not { } st) return;
        if (sender is not RadioButton rb || rb.Tag is not string idStr) return;
        if (!int.TryParse(idStr, out var idx) || idx >= st.Choices.Count) return;
        var chosen = st.Choices[idx];
        // result = 选项的 value 或 index
        var result = chosen.TryGetValue("Result", out var r) ? r.GetString() : idx.ToString();
        Status.Text = "正在把选择回灌 rclone…";
        Busy.IsActive = true;
        try
        {
            var next = await App.Hub.Rclone.ConfigContinueAsync(
                WizardState.TempConfPath, WizardState.RemoteName, st.State, result, WizardState.Proxy);
            WizardState.LastState = next;
            if (next.IsDone)
            {
                ChoicesList.Visibility = Visibility.Collapsed;
                Status.Text = "选择完成，配置已写入。";
                NextBtn.Visibility = Visibility.Visible;
            }
            else if (next.Choices.Count > 0)
            {
                // 还有下一级选择
                Choices.Clear();
                int i = 0;
                foreach (var ch in next.Choices)
                {
                    var label = ch.TryGetValue("Name", out var n) ? n.GetString() : $"选项 {i + 1}";
                    Choices.Add(new ConfigChoiceVM { Id = i.ToString(), Label = label });
                    i++;
                }
            }
            else
            {
                Status.Text = "完成。";
                NextBtn.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            Status.Text = "异常：" + ex.Message;
        }
        finally { Busy.IsActive = false; }
    }

    private void Next_Click(object sender, RoutedEventArgs e) => Goto(3);

    private void Goto(int idx)
    {
        if (this.Frame?.Parent is Frame f && f.Parent is NavigationView nv)
        {
            var items = nv.MenuItems.OfType<NavigationViewItem>().ToList();
            nv.SelectedItem = items.ElementAtOrDefault(idx);
        }
    }

    private static List<KeyValuePair<string, string>> BuildOptions(CloudTypeDef def)
    {
        var opts = new List<KeyValuePair<string, string>>();
        foreach (var f in def.Fields)
        {
            if (WizardState.FieldValues.TryGetValue(f.Key, out var v) && !string.IsNullOrEmpty(v))
                opts.Add(new(f.Key, f.IsSecret ? v : v));
        }
        return opts;
    }
}

public sealed class ConfigChoiceVM
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
}
