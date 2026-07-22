namespace ComfyCarry.Models;

/// <summary>
/// Tab1 支持的云类型（SPEC §4.2）。
/// 与面板 REMOTE_TYPE_DEFS 对齐。
/// </summary>
public enum CloudType
{
    OneDrivePersonal,
    OneDriveBusiness,
    GoogleDrive,
    Dropbox,
    WebDAV,
    CloudflareR2,
    AwsS3
}

/// <summary>
/// 云类型元信息：rclone type、关键字段、是否需 OAuth。
/// AutoAnswers: 状态机中遇到指定 question name 时自动回答的值（不弹给用户选）。
///   值为 "__first__" 表示自动选 Examples 列表的第一项。
///   值为 "__match:keyword__" 表示自动选 Help 文本包含 keyword 的项（找不到则弹给用户）。
/// </summary>
public sealed record CloudTypeDef(
    CloudType Type,
    string DisplayName,
    string RcloneType,
    string? Provider,                // rclone 的 provider=xxx，无则 null
    bool RequiresOAuth,              // 是否走 OAuth 浏览器授权
    IReadOnlyList<CloudFieldDef> Fields,
    IReadOnlyDictionary<string, string>? AutoAnswers = null,
    string DefaultRemoteName = "",
    string LogoPath = "");

/// <summary>
/// 非敏感字段定义（敏感字段走 OAuth 或单独 obscure 存）。
/// </summary>
public sealed record CloudFieldDef(
    string Key,
    string Label,
    string? Placeholder,
    bool IsSecret,
    bool IsOptional,
    string? Help);

public static class CloudTypeDefs
{
    public static readonly IReadOnlyList<CloudTypeDef> All = new[]
    {
        new CloudTypeDef(
            CloudType.OneDrivePersonal, "OneDrive (个人)", "onedrive", "personal", true,
            new[]
            {
                new CloudFieldDef("drive_id", "Drive ID（可选，留空走状态机选择）", null, false, true, "不填则由 rclone 状态机返回可选项让你选。"),
            },
            new Dictionary<string, string> { ["config_type"] = "__first__", ["config_driveid"] = "__exact:OneDrive (personal)__", ["config_drive_ok"] = "__first__" },
            "onedrive",
            "Assets/logos/onedrive.png"),
        new CloudTypeDef(
            CloudType.OneDriveBusiness, "OneDrive (商业/学校)", "onedrive", "business", true,
            new[]
            {
                new CloudFieldDef("drive_id", "Drive ID（可选，留空走状态机选择）", null, false, true, "不填则由 rclone 状态机返回可选项让你选。"),
            },
            new Dictionary<string, string> { ["config_type"] = "__first__", ["config_driveid"] = "__exact:OneDrive (business)__", ["config_drive_ok"] = "__first__" },
            "onedrive-biz",
            "Assets/logos/onedrive.png"),
        new CloudTypeDef(
            CloudType.GoogleDrive, "Google Drive", "drive", null, true,
            Array.Empty<CloudFieldDef>(),
            null,
            "gdrive",
            "Assets/logos/googledrive.png"),
        new CloudTypeDef(
            CloudType.Dropbox, "Dropbox", "dropbox", null, true,
            Array.Empty<CloudFieldDef>(),
            null,
            "dropbox",
            "Assets/logos/dropbox.png"),
        new CloudTypeDef(
            CloudType.WebDAV, "WebDAV（坚果云·群晖·AList 等）", "webdav", null, false,
            new[]
            {
                new CloudFieldDef("url", "服务地址", "https://dav.jianguoyun.com/dav/", false, false, "必须以 / 结尾。"),
                new CloudFieldDef("vendor", "WebDAV 实现", "nextcloud", false, true, "nextcloud/other/sharepoint…"),
                new CloudFieldDef("user", "账号", null, false, false, null),
                new CloudFieldDef("pass", "密码/应用密码", null, true, false, "坚果云用应用密码。"),
            },
            null,
            "webdav",
            "Assets/logos/webdav.png"),
        new CloudTypeDef(
            CloudType.CloudflareR2, "Cloudflare R2", "s3", "Cloudflare", false,
            new[]
            {
                new CloudFieldDef("access_key_id", "Access Key ID", null, true, false, null),
                new CloudFieldDef("secret_access_key", "Secret Access Key", null, true, false, null),
                new CloudFieldDef("endpoint", "S3 Endpoint", "https://<account>.r2.cloudflarestorage.com", false, false, null),
                new CloudFieldDef("acl", "ACL", "private", false, true, null),
            },
            null,
            "r2",
            "Assets/logos/cloudflare.png"),
        new CloudTypeDef(
            CloudType.AwsS3, "AWS S3", "s3", "AWS", false,
            new[]
            {
                new CloudFieldDef("access_key_id", "Access Key ID", null, true, false, null),
                new CloudFieldDef("secret_access_key", "Secret Access Key", null, true, false, null),
                new CloudFieldDef("region", "Region", "us-east-1", false, false, null),
                new CloudFieldDef("endpoint", "Endpoint（可选）", null, false, true, null),
            },
            null,
            "s3",
            "Assets/logos/awss3.png"),
    };

    public static CloudTypeDef Get(CloudType t) => All.First(c => c.Type == t);
}
