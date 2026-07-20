using System.Text.Json.Serialization;

namespace ComfyCarry.Models;

/// <summary>
/// rclone 非交互配置状态机的状态对象（rclone config --all/--continue 返回的 JSON）。
/// 字段按 rclone 实际输出形状命名，不存在的字段保持默认。
/// </summary>
public sealed class RcloneConfigState
{
    [JsonPropertyName("State")]
    public string State { get; set; } = "";       // 空表示完成

    [JsonPropertyName("Result")]
    public string Result { get; set; } = "";

    [JsonPropertyName("Choices")]
    public List<Dictionary<string, JsonElement>> Choices { get; set; } = new();

    [JsonPropertyName("Options")]
    public Dictionary<string, JsonElement> Options { get; set; } = new();

    [JsonPropertyName("Name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("Type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("Str")]
    public string Str { get; set; } = "";

    [JsonPropertyName("Error")]
    public string? Error { get; set; }

    [JsonPropertyName("Config")]
    public Dictionary<string, string>? Config { get; set; }

    /// <summary>状态机是否已完成（State 为空字符串即完成）。</summary>
    public bool IsDone => string.IsNullOrEmpty(State);
}

/// <summary>
/// 给 UI 呈现的结构化选项（OneDrive 选 drive 等）。
/// </summary>
public sealed record ConfigChoice(string Id, string Label, Dictionary<string, string> Attributes);
