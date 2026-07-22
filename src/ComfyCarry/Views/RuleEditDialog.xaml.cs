using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ComfyCarry.Models;
using ComfyCarry.Services;

namespace ComfyCarry.Views;

public sealed partial class RuleEditDialog : ContentDialog
{
    private LocalizationService L => App.Hub.Locale;
    public PullRule Rule { get; private set; }
    private readonly bool _isNew;
    private string _selectedContent = "images";
    private string _selectedMethod = "copy";
    private string _selectedTrigger = "watch";

    public RuleEditDialog(PullRule rule, bool isNew)
    {
        this.InitializeComponent();
        Rule = rule;
        _isNew = isNew;
        _selectedContent = rule.Content;
        _selectedMethod = rule.Method;
        _selectedTrigger = rule.Trigger;
        Localize();
        Load();
        PrimaryButtonClick += OnPrimaryClick;
    }

    private void Localize()
    {
        Title = _isNew ? L.T("pull.rule.new") : L.T("common.edit");
        PrimaryButtonText = L.T("common.save");
        CloseButtonText = L.T("common.cancel");
        NameLabel.Text = L.T("pull.rule.name") + " *";
        LocalPathLabel.Text = L.T("pull.rule.localPath") + " *";
        BrowseLocalBtn.Content = L.T("pull.rule.select");
        ContentLabel.Text = L.T("pull.rule.content");
        ContentImagesBtn.Content = L.T("pull.rule.content.images");
        ContentVideosBtn.Content = L.T("pull.rule.content.videos");
        ContentAllBtn.Content = L.T("pull.rule.content.all");
        SubdirsCheck.Content = L.T("pull.rule.subdirs");
        MethodLabel.Text = L.T("pull.rule.method");
        MethodCopyBtn.Content = L.T("pull.rule.method.copy");
        MethodMoveBtn.Content = L.T("pull.rule.method.move");
        MoveWarn.Message = L.T("pull.rule.move.confirm");
        TriggerLabel.Text = L.T("pull.rule.trigger");
        TriggerAutoBtn.Content = L.T("pull.rule.trigger.auto");
        TriggerManualBtn.Content = L.T("pull.rule.trigger.manual");
        EnabledCheck.Content = L.T("pull.rule.enabled");
    }

    private void Load()
    {
        NameBox.Text = Rule.Name;
        LocalPathBox.Text = Rule.LocalPath;
        SubdirsCheck.IsChecked = Rule.Subdirs;
        EnabledCheck.IsChecked = Rule.Enabled;
        UpdateSegmentedStyles();
    }

    private void UpdateSegmentedStyles()
    {
        SetAccent(ContentImagesBtn, _selectedContent == "images");
        SetAccent(ContentVideosBtn, _selectedContent == "videos");
        SetAccent(ContentAllBtn, _selectedContent == "all");
        SetAccent(MethodCopyBtn, _selectedMethod == "copy");
        SetAccent(MethodMoveBtn, _selectedMethod == "move");
        MoveWarn.IsOpen = _selectedMethod == "move";
        SetAccent(TriggerAutoBtn, _selectedTrigger == "watch");
        SetAccent(TriggerManualBtn, _selectedTrigger == "manual");
    }

    private static void SetAccent(Button btn, bool active)
    {
        btn.Style = active
            ? (Style)Application.Current.Resources["AccentButtonStyle"]
            : null;
    }

    private void Content_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string tag) { _selectedContent = tag; UpdateSegmentedStyles(); }
    }

    private void Method_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string tag) { _selectedMethod = tag; UpdateSegmentedStyles(); }
    }

    private void Trigger_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string tag) { _selectedTrigger = tag; UpdateSegmentedStyles(); }
    }

    private void BrowseLocal_Click(object sender, RoutedEventArgs e)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        var path = Services.FolderPicker.PickFolder(hwnd, L.T("pull.rule.localPath"));
        if (path is not null) LocalPathBox.Text = path;
    }

    private void OnPrimaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var name = NameBox.Text.Trim();
        var local = LocalPathBox.Text.Trim();
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(local))
        {
            var missing = new List<string>();
            if (string.IsNullOrEmpty(name)) missing.Add(L.T("pull.rule.name"));
            if (string.IsNullOrEmpty(local)) missing.Add(L.T("pull.rule.localPath"));
            var sep = App.Hub.Settings.Data.Language == "en-US" ? ", " : "、";
            ErrorBar.Message = L.T("pull.rule.validate.required") + string.Join(sep, missing);
            ErrorBar.IsOpen = true;
            args.Cancel = true;
            return;
        }
        Rule.Name = name;
        Rule.LocalPath = local;
        Rule.Content = _selectedContent;
        Rule.Subdirs = SubdirsCheck.IsChecked == true;
        Rule.Method = _selectedMethod;
        Rule.Trigger = _selectedTrigger;
        Rule.Enabled = EnabledCheck.IsChecked == true;
    }
}
