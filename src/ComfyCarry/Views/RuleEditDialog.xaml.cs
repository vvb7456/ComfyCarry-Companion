using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using ComfyCarry.Services;

namespace ComfyCarry.Views;

public sealed partial class RuleEditDialog : ContentDialog
{
    private LocalizationService L => App.Hub.Locale;
    public PullRule Rule { get; private set; }
    private readonly bool _isNew;

    public RuleEditDialog(PullRule rule, bool isNew)
    {
        this.InitializeComponent();
        Rule = rule;
        _isNew = isNew;
        Localize();
        Load();
        PrimaryButtonClick += RuleEditDialog_PrimaryButtonClick;
        MethodBox.SelectionChanged += (_, _) =>
            MoveWarn.Visibility = MethodBox.SelectedItem is string m && m == "move" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Localize()
    {
        Title = _isNew ? L.T("pull.rule.new") : L.T("common.edit");
        PrimaryButtonText = L.T("common.save");
        CloseButtonText = L.T("common.cancel");
        BrowseBtn.Content = L.T("common.browse");
        LocalPathBox.Header = L.T("pull.rule.localPath");
        MethodBox.Header = L.T("pull.rule.method");
        FiltersBox.Header = L.T("pull.rule.filters");
        TriggerBox.Header = L.T("pull.rule.trigger");
        IntervalBox.Header = L.T("pull.rule.interval");
    }

    private void Load()
    {
        NameBox.Text = Rule.Name;
        RemotePathBox.Text = Rule.RemotePath;
        LocalPathBox.Text = Rule.LocalPath;
        MethodBox.SelectedItem = Rule.Method;
        FiltersBox.Text = string.Join(" ", Rule.Filters);
        TriggerBox.SelectedItem = Rule.Trigger;
        IntervalBox.Value = Rule.IntervalSec;
        EnabledSwitch.IsOn = Rule.Enabled;
        MoveWarn.Visibility = Rule.Method == "move" ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null) LocalPathBox.Text = folder.Path;
    }

    private void RuleEditDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var name = NameBox.Text.Trim();
        var local = LocalPathBox.Text.Trim();
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(local))
        {
            args.Cancel = true;
            return;
        }
        Rule.Name = name;
        Rule.RemotePath = RemotePathBox.Text.Trim();
        Rule.LocalPath = local;
        Rule.Method = MethodBox.SelectedItem as string ?? "copy";
        Rule.Filters = FiltersBox.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        Rule.Trigger = TriggerBox.SelectedItem as string ?? "watch";
        Rule.IntervalSec = (int)IntervalBox.Value;
        Rule.Enabled = EnabledSwitch.IsOn;
    }
}
