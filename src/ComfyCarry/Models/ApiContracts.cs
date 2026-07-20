using System.Text.Json.Serialization;

namespace ComfyCarry.Models;

/// <summary>
/// 面板返回的 connect 响应（SPEC §2.2）。
/// </summary>
public sealed class ConnectResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("api_key")]
    public string ApiKey { get; set; } = "";

    [JsonPropertyName("dav_url")]
    public string DavUrl { get; set; } = "";

    [JsonPropertyName("dav_user")]
    public string DavUser { get; set; } = "comfy";

    [JsonPropertyName("comfyui_dir")]
    public string ComfyuiDir { get; set; } = "/workspace/ComfyUI";

    [JsonPropertyName("instance_label")]
    public string InstanceLabel { get; set; } = "";

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>
/// 规则 CRUD 接口的列表/模板响应。
/// </summary>
public sealed class RulesResponse
{
    [JsonPropertyName("rules")]
    public List<PullRule> Rules { get; set; } = new();

    [JsonPropertyName("templates")]
    public List<PullRule> Templates { get; set; } = new();

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>
/// 心跳请求（SPEC §2.4）。
/// </summary>
public sealed class HeartbeatRequest
{
    [JsonPropertyName("client_id")]
    public string ClientId { get; set; } = "";

    [JsonPropertyName("hostname")]
    public string Hostname { get; set; } = "";

    [JsonPropertyName("app_version")]
    public string AppVersion { get; set; } = "1.0.0";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "idle";

    [JsonPropertyName("active_rule_id")]
    public string? ActiveRuleId { get; set; }

    [JsonPropertyName("progress")]
    public HeartbeatProgress? Progress { get; set; }
}

public sealed class HeartbeatProgress
{
    [JsonPropertyName("file")]
    public string? File { get; set; }

    [JsonPropertyName("pct")]
    public int Pct { get; set; }

    [JsonPropertyName("speed")]
    public long Speed { get; set; }
}

/// <summary>
/// Job 创建/事件/收尾请求（SPEC §2.5）。
/// </summary>
public sealed class JobCreateRequest
{
    [JsonPropertyName("rule_id")]
    public string RuleId { get; set; } = "";

    [JsonPropertyName("client_id")]
    public string ClientId { get; set; } = "";

    [JsonPropertyName("trigger_type")]
    public string TriggerType { get; set; } = "companion";
}

public sealed class JobCreateResponse
{
    [JsonPropertyName("job_id")]
    public string JobId { get; set; } = "";

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public sealed class JobEventRequest
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";   // rule_start/file_transferred/rule_done

    [JsonPropertyName("level")]
    public string? Level { get; set; }       // info/warning/error（log 类事件）

    [JsonPropertyName("file")]
    public string? File { get; set; }

    [JsonPropertyName("pct")]
    public int? Pct { get; set; }

    [JsonPropertyName("speed")]
    public long? Speed { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

public sealed class JobFinishRequest
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "ok";   // ok|error|partial

    [JsonPropertyName("stats")]
    public Dictionary<string, object> Stats { get; set; } = new();

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
