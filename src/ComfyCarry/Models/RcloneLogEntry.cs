using System.Text.Json.Serialization;

namespace ComfyCarry.Models;

/// <summary>
/// rclone JSON 日志条目（rclone --use-json-log 输出每行一个 JSON）。
/// 字段名对齐 rclone 文档。
/// </summary>
public sealed class RcloneLogEntry
{
    [JsonPropertyName("level")]
    public string Level { get; set; } = "info";    // error/warning/info/debug

    [JsonPropertyName("msg")]
    public string Msg { get; set; } = "";

    [JsonPropertyName("object")]
    public string? Object { get; set; }            // 当前传输的文件

    [JsonPropertyName("stats")]
    public RcloneStats? Stats { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }            // target / transfer / done…

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("size")]
    public long? Size { get; set; }

    [JsonPropertyName("bytes")]
    public long? Bytes { get; set; }

    [JsonPropertyName("percentage")]
    public double? Percentage { get; set; }

    [JsonPropertyName("speed")]
    public double? Speed { get; set; }

    [JsonPropertyName("eta")]
    public string? Eta { get; set; }
}

public sealed class RcloneStats
{
    [JsonPropertyName("bytes")]
    public long Bytes { get; set; }

    [JsonPropertyName("speed")]
    public double Speed { get; set; }

    [JsonPropertyName("checks")]
    public int Checks { get; set; }

    [JsonPropertyName("transfers")]
    public int Transfers { get; set; }

    [JsonPropertyName("elapsedTime")]
    public double ElapsedTime { get; set; }
}

/// <summary>
/// 给 WebDAV 列出的条目（rclone lsf）。
/// </summary>
public sealed class RemoteEntry
{
    public string Name { get; set; } = "";
    public bool IsDir { get; set; }
    public long Size { get; set; }
    public string ModTime { get; set; } = "";
    public string? ThumbnailUrl { get; set; }
}
