using System.Text.Json.Serialization;

namespace ComfyCarry.Models;

/// <summary>
/// 拉取规则（客户端本地所有，绑定 instance_label）。
/// </summary>
public sealed class PullRule
{
    [JsonPropertyName("rule_id")]
    public string RuleId { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("local_path")]
    public string LocalPath { get; set; } = "";     // Windows 本机路径

    [JsonPropertyName("method")]
    public string Method { get; set; } = "copy";    // copy|move

    [JsonPropertyName("content")]
    public string Content { get; set; } = "images"; // images|videos|all

    [JsonPropertyName("subdirs")]
    public bool Subdirs { get; set; } = true;

    [JsonPropertyName("trigger")]
    public string Trigger { get; set; } = "watch";  // watch|manual

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("last_result")]
    public string LastResult { get; set; } = "";

    [JsonPropertyName("last_run_at")]
    public DateTime? LastRunAt { get; set; }

    [JsonIgnore]
    public string StatusText { get; set; } = "idle";

    [JsonIgnore]
    public int ProgressPct { get; set; }
}
