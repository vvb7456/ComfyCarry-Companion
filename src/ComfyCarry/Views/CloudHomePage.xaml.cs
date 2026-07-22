using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using Windows.Storage;
using ComfyCarry.Services;
using ComfyCarry.Views.Wizard;

namespace ComfyCarry.Views;

public sealed partial class CloudHomePage : Page
{
    private LocalizationService L => App.Hub.Locale;

    public CloudHomePage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Localize();
        LoadRemotes();
    }

    private void Localize()
    {
        TitleText.Text = L.T("cloud.title");
        EmptyTitle.Text = L.T("cloud.home.noRemotes");
        EmptyAddLabel.Text = L.T("cloud.newRemote");
        NewRemoteLabel.Text = L.T("cloud.newRemote");
        ExportLabel.Text = L.T("cloud.home.export");
        if (!App.Hub.Rclone.IsPresent)
        {
            RcloneMissingBar.IsOpen = true;
            RcloneMissingBar.Title = L.T("cloud.rcloneMissing.title");
            RcloneMissingBar.Message = L.T("cloud.rcloneMissing");
        }
    }

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
        catch { }

        if (names.Count > 0)
        {
            EmptyState.Visibility = Visibility.Collapsed;
            DataState.Visibility = Visibility.Visible;
            var vms = names.Select(n => new RemoteVM
            {
                Name = n,
                LogoUri = GetLogoForRemote(n),
            }).ToList();
            RemotesList.ItemsSource = vms;
            ExportBtn.IsEnabled = true;
        }
        else
        {
            EmptyState.Visibility = Visibility.Visible;
            DataState.Visibility = Visibility.Collapsed;
            ExportBtn.IsEnabled = false;
        }
    }

    private static Microsoft.UI.Xaml.Media.Imaging.BitmapImage? GetLogoForRemote(string name)
    {
        try
        {
            var conf = App.Hub.Paths.AppRcloneConf;
            if (!File.Exists(conf)) return null;
            var lines = File.ReadAllLines(conf);
            string? type = null;
            bool inSection = false;
            foreach (var ln in lines)
            {
                var t = ln.Trim();
                if (t.StartsWith("[") && t.EndsWith("]"))
                {
                    inSection = t == $"[{name}]";
                }
                else if (inSection && t.StartsWith("type"))
                {
                    var parts = t.Split('=', 2);
                    if (parts.Length == 2) type = parts[1].Trim();
                    break;
                }
            }
            if (type is null) return null;
            var def = CloudTypeDefs.All.FirstOrDefault(d => d.RcloneType == type);
            if (def is null || string.IsNullOrEmpty(def.LogoPath)) return null;
            var path = Path.Combine(AppContext.BaseDirectory, def.LogoPath);
            if (!File.Exists(path)) return null;
            var uri = new Uri(path);
            return new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(uri);
        }
        catch { return null; }
    }

    private void NewRemote_Click(object sender, RoutedEventArgs e)
    {
        WizardState.Reset();
        this.Frame.Navigate(typeof(WizardTypePage));
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string name) return;
        try
        {
            var conf = App.Hub.Paths.AppRcloneConf;
            if (!File.Exists(conf)) return;
            var lines = File.ReadAllLines(conf).ToList();
            var result = new List<string>();
            bool skip = false;
            foreach (var ln in lines)
            {
                var t = ln.Trim();
                if (t.StartsWith("[") && t.EndsWith("]"))
                {
                    skip = t == $"[{name}]";
                }
                if (!skip) result.Add(ln);
            }
            File.WriteAllLines(conf, result);
            LoadRemotes();
        }
        catch { }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var conf = App.Hub.Paths.AppRcloneConf;
            if (!File.Exists(conf)) return;
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
                var content = File.ReadAllText(conf);
                await FileIO.WriteTextAsync(file, content);
            }
        }
        catch { }
    }
}

public sealed class RemoteVM
{
    public string Name { get; set; } = "";
    public Microsoft.UI.Xaml.Media.Imaging.BitmapImage? LogoUri { get; set; }
}
