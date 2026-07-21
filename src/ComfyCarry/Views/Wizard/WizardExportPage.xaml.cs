using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using Windows.Storage;
using ComfyCarry.Services;
using ComfyCarry.Views;

namespace ComfyCarry.Views.Wizard;

public sealed partial class WizardExportPage : Page
{
    private LocalizationService L => App.Hub.Locale;

    public WizardExportPage()
    {
        this.InitializeComponent();
        Localize();
        LoadPreview();
    }

    private void Localize()
    {
        Lbl.Text = L.T("cloud.step.export");
        Hint.Text = L.T("cloud.export.guide");
        ConfPreview.Header = L.T("cloud.export.preview");
        SaveBtn.Content = L.T("common.export");
        CopyBtn.Content = L.T("common.copy");
        BackBtn.Content = L.T("common.back");
        FinishBtn.Content = L.T("common.finish");
    }

    private void LoadPreview()
    {
        try
        {
            if (File.Exists(WizardState.TempConfPath))
                ConfPreview.Text = File.ReadAllText(WizardState.TempConfPath);
            else
                ConfPreview.Text = L.T("cloud.export.noConf");
        }
        catch (Exception ex) { ConfPreview.Text = ex.Message; }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // 合并到 app 主 conf（累积多个 remote），并保存为用户选择的文件
            MergeIntoAppConf();

            var picker = new FileSavePicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            picker.SuggestedStartLocation = PickerLocationId.Downloads;
            picker.SuggestedFileName = "rclone";
            picker.FileTypeChoices.Add("rclone.conf", new[] { ".conf" });
            picker.DefaultFileExtension = ".conf";
            var file = await picker.PickSaveFileAsync();
            if (file is not null)
            {
                var content = File.ReadAllText(WizardState.TempConfPath);
                await FileIO.WriteTextAsync(file, content);
                Status.Text = $"{L.T("cloud.export.saved")}: {file.Path}";
            }
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }

    /// <summary>把临时 conf 里的 remote 段合并进 app 主 conf（简单文本合并，避免重复 [name] 段）。</summary>
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
        catch { /* 合并失败不阻塞导出 */ }
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

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var content = File.Exists(WizardState.TempConfPath) ? File.ReadAllText(WizardState.TempConfPath) : ConfPreview.Text;
            var dp = new DataPackage();
            dp.SetText(content);
            Clipboard.SetContent(dp);
            Status.Text = L.T("cloud.export.copied");
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }

    private void Back_Click(object sender, RoutedEventArgs e) => this.Frame?.GoBack();

    private void Finish_Click(object sender, RoutedEventArgs e)
    {
        // 完成回首页，清空 back stack
        this.Frame?.Navigate(typeof(CloudHomePage));
    }
}
