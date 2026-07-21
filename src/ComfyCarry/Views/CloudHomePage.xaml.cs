using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ComfyCarry.Services;
using ComfyCarry.Views.Wizard;

namespace ComfyCarry.Views;

public sealed partial class CloudHomePage : Page
{
    private LocalizationService L => App.Hub.Locale;

    public CloudHomePage()
    {
        this.InitializeComponent();
        Localize();
        if (!App.Hub.Rclone.IsPresent)
        {
            RcloneMissingBar.IsOpen = true;
            RcloneMissingBar.Title = L.T("cloud.rcloneMissing.title");
            RcloneMissingBar.Message = L.T("cloud.rcloneMissing");
        }
        LoadRemotes();
        App.Hub.Settings.Changed += () => DispatcherQueue.TryEnqueue(Localize);
    }

    private void Localize()
    {
        TitleText.Text = L.T("cloud.title");
        IntroText.Text = L.T("cloud.home.intro");
        RemotesHeader.Text = L.T("cloud.home.remotes");
        RemotesEmpty.Text = L.T("cloud.home.noRemotes");
        NewRemoteLabel.Text = L.T("cloud.newRemote");
    }

    /// <summary>best-effort 解析 app 主 conf 里的 [section] 名，只读展示。</summary>
    private void LoadRemotes()
    {
        var names = new List<string>();
        try
        {
            var conf = App.Hub.Paths.AppRcloneConf;
            if (File.Exists(conf))
            {
                foreach (var ln in File.ReadAllLines(conf))
                {
                    var t = ln.Trim();
                    if (t.StartsWith("[") && t.EndsWith("]") && t.Length > 2)
                        names.Add(t.Substring(1, t.Length - 2));
                }
            }
        }
        catch { /* best-effort */ }

        if (names.Count > 0)
        {
            RemotesList.ItemsSource = names;
            RemotesEmpty.Visibility = Visibility.Collapsed;
        }
        else
        {
            RemotesList.ItemsSource = Array.Empty<string>();
            RemotesEmpty.Visibility = Visibility.Visible;
        }
    }

    private void NewRemote_Click(object sender, RoutedEventArgs e)
    {
        // 重置向导共享状态，进入线性向导首页（选类型）
        WizardState.Reset();
        this.Frame.Navigate(typeof(WizardTypePage));
    }
}
