using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ComfyCarry.Services;

namespace ComfyCarry.Views.Wizard;

public sealed partial class WizardNamePage : Page
{
    private LocalizationService L => App.Hub.Locale;
    private static readonly Regex NameRegex = new("^[a-zA-Z0-9_-]+$", RegexOptions.Compiled);

    public WizardNamePage()
    {
        this.InitializeComponent();
        Localize();
        // 预填充：若用户未手动输入，使用云类型的默认名称
        if (string.IsNullOrEmpty(WizardState.RemoteName))
        {
            var def = Models.CloudTypeDefs.Get(WizardState.SelectedCloud);
            WizardState.RemoteName = def.DefaultRemoteName;
        }
        NameBox.Text = WizardState.RemoteName;
        UpdateValidity();
    }

    private void Localize()
    {
        Lbl.Text = L.T("cloud.step.name");
        NameBox.Header = L.T("cloud.remoteName");
        BackBtn.Content = L.T("common.back");
        NextBtn.Content = L.T("common.next");
    }

    private void Name_TextChanged(object sender, TextChangedEventArgs e)
    {
        WizardState.RemoteName = NameBox.Text.Trim();
        UpdateValidity();
    }

    private void UpdateValidity()
    {
        var name = WizardState.RemoteName;
        bool ok = name.Length > 0 && NameRegex.IsMatch(name);
        string err = "";
        if (!ok)
        {
            err = L.T("cloud.remoteName.invalid");
        }
        else
        {
            // best-effort：与已有 remote 重名校验
            try
            {
                var conf = App.Hub.Paths.AppRcloneConf;
                if (File.Exists(conf))
                {
                    foreach (var ln in File.ReadAllLines(conf))
                    {
                        var t = ln.Trim();
                        if (t.StartsWith("[") && t.EndsWith("]") && t.Length > 2)
                        {
                            var existing = t.Substring(1, t.Length - 2);
                            if (string.Equals(existing, name, StringComparison.Ordinal))
                            {
                                ok = false;
                                err = L.T("cloud.remoteName.duplicate");
                                break;
                            }
                        }
                    }
                }
            }
            catch { /* best-effort，校验失败不阻塞 */ }
        }
        NameErr.Text = err;
        NameErr.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
        NextBtn.IsEnabled = ok;
    }

    private void Back_Click(object sender, RoutedEventArgs e) => this.Frame?.GoBack();

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        WizardState.EnsureTempConf();
        this.Frame?.Navigate(typeof(WizardConfigPage));
    }
}
