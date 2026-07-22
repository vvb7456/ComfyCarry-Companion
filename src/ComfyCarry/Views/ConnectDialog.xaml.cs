using Microsoft.UI.Xaml.Controls;
using ComfyCarry.Services;

namespace ComfyCarry.Views;

public sealed partial class ConnectDialog : ContentDialog
{
    private LocalizationService L => App.Hub.Locale;

    public ConnectDialog()
    {
        this.InitializeComponent();
        Localize();
        PrimaryButtonClick += ConnectDialog_PrimaryButtonClick;
    }

    private void Localize()
    {
        Title = L.T("pull.connect");
        PrimaryButtonText = L.T("pull.connect.btn");
        CloseButtonText = L.T("common.cancel");
        UrlBox.Header = L.T("pull.panelUrl");
        PwdBox.Header = L.T("pull.panelPwd");
        LabelBox.Header = L.T("pull.connect.label");
    }

    private async void ConnectDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var def = args.GetDeferral();
        Busy.IsActive = true;
        Busy.Visibility = Visibility.Visible;
        Status.Visibility = Visibility.Visible;
        Status.Text = L.T("pull.connect.connecting");
        IsPrimaryButtonEnabled = false;
        try
        {
            var url = UrlBox.Text.Trim();
            var pwd = PwdBox.Password;
            var cr = await App.Hub.Api.ConnectAsync(url, pwd);
            if (!cr.Ok || string.IsNullOrEmpty(cr.ApiKey))
            {
                Status.Text = L.T("pull.connect.failed") + (cr.Error ?? L.T("pull.connect.unknownError"));
                args.Cancel = true;
                return;
            }
            // 存实例（去重：同 URL 更新而非新建）
            var normalizedUrl = url.TrimEnd('/');
            var existing = App.Hub.Instances.All.FirstOrDefault(
                i => i.BaseUrl.Trim().TrimEnd('/').Equals(normalizedUrl, StringComparison.OrdinalIgnoreCase));
            var inst = existing ?? new PanelInstance();
            inst.BaseUrl = url;
            inst.Password = pwd;
            inst.ApiKey = cr.ApiKey;
            inst.DavUrl = cr.DavUrl;
            inst.DavUser = cr.DavUser;
            inst.ComfyuiDir = cr.ComfyuiDir;
            inst.InstanceLabel = cr.InstanceLabel;
            inst.Label = string.IsNullOrEmpty(LabelBox.Text.Trim()) ? cr.InstanceLabel : LabelBox.Text.Trim();
            inst.IsCurrent = true;
            App.Hub.Instances.EnsureClientId(inst);
            App.Hub.Instances.Upsert(inst);
            // 确保 webdav remote
            await App.Hub.Rclone.EnsureInstanceWebdavRemoteAsync(inst);
            Status.Text = L.T("pull.connect.success");
        }
        catch (Exception ex)
        {
            Status.Text = L.T("pull.connect.exception") + ex.Message;
            args.Cancel = true;
        }
        finally
        {
            Busy.IsActive = false;
            Busy.Visibility = Visibility.Collapsed;
            IsPrimaryButtonEnabled = true;
            def.Complete();
        }
    }
}
