using System.Text.Json.Serialization;

namespace ComfyCarry.Models;

/// <summary>
/// 拉取规则（与面板 sync_rules 同源，SPEC §0.2/§2.3/§4.3）。
/// 面板为规则真源；客户端本地缓存用于离线展示。
/// </summary>
public sealed class PullRule
{
    [JsonPropertyName("rule_id")]
    public string RuleId { get; set; } = "";

    [JsonPropertyName("client_id")]
    public string ClientId { get; set; } = "";

    [JsonPropertyName("executor")]
    public string Executor { get; set; } = "companion";   // 固定 companion

    [JsonPropertyName("direction")]
    public string Direction { get; set; } = "pull";

    [JsonPropertyName("method")]
    public string Method { get; set; } = "copy";          // copy|move|sync

    [JsonPropertyName("remote_path")]
    public string RemotePath { get; set; } = "";          // serve content_root 下相对子路径

    [JsonPropertyName("local_path")]
    public string LocalPath { get; set; } = "";           // Windows 本机路径

    [JsonPropertyName("filters")]
    public List<string> Filters { get; set; } = new();

    [JsonPropertyName("trigger")]
    public string Trigger { get; set; } = "watch";        // watch|manual

    [JsonPropertyName("interval_sec")]
    public int IntervalSec { get; set; } = 300;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("template_id")]
    public string? TemplateId { get; set; }

    [JsonIgnore]
    public string StatusText { get; set; } = "idle";

    [JsonIgnore]
    public int ProgressPct { get; set; }
}

public enum RuleMethod { Copy, Move, Sync }
public enum RuleTrigger { Watch, Manual }

public static class RuleMethodExt
{
    public static string ToRclone(this string method) => method switch
    {
        "copy" => "copy",
        "move" => "move",
        "sync" => "sync",
        _ => "copy",
    };
}
