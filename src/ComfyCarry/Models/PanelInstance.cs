using System.Text.Json.Serialization;

namespace ComfyCarry.Models;

/// <summary>
/// 一个连接的面板实例（SPEC §3.5）。
/// 凭据（面板密码、api_key、dav_pass）走 DPAPI 加密存盘。
/// </summary>
public sealed class PanelInstance
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Label { get; set; } = "";          // 用户起名，默认 hostname
    public string BaseUrl { get; set; } = "";         // https://comfy.example.com
    public string ClientId { get; set; } = "";        // 稳定 uuid
    public string DavUrl { get; set; } = "";
    public string DavUser { get; set; } = "comfy";
    public string ComfyuiDir { get; set; } = "/workspace/ComfyUI";
    public string EncPassword { get; set; } = "";     // DPAPI
    public string EncApiKey { get; set; } = "";       // DPAPI
    public bool IsCurrent { get; set; }

    [JsonIgnore]
    public string Password { get; set; } = "";
    [JsonIgnore]
    public string ApiKey { get; set; } = "";
}
