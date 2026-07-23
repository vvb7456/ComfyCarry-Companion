using System.Text.Json.Serialization;

namespace ComfyCarry.Models;

/// <summary>面板 connect 响应。</summary>
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

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>心跳请求。</summary>
public sealed class HeartbeatRequest
{
    [JsonPropertyName("client_id")]
    public string ClientId { get; set; } = "";

    [JsonPropertyName("hostname")]
    public string Hostname { get; set; } = "";

    [JsonPropertyName("app_version")]
    public string AppVersion { get; set; } = "0.1.0-beta";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "idle";

    [JsonPropertyName("active_rule_id")]
    public string? ActiveRuleId { get; set; }

    [JsonPropertyName("progress")]
    public HeartbeatProgress? Progress { get; set; }

    [JsonPropertyName("rule_summaries")]
    public List<RuleSummary> RuleSummaries { get; set; } = new();
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

/// <summary>心跳中的规则摘要（只读，面板 Clients tab 展示用）。</summary>
public sealed class RuleSummary
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    [JsonPropertyName("local_path")]
    public string LocalPath { get; set; } = "";

    [JsonPropertyName("method")]
    public string Method { get; set; } = "";

    [JsonPropertyName("trigger")]
    public string Trigger { get; set; } = "";

    [JsonPropertyName("last_result")]
    public string LastResult { get; set; } = "";
}

/// <summary>Job 创建请求。</summary>
public sealed class JobCreateRequest
{
    [JsonPropertyName("rule_id")]
    public string RuleId { get; set; } = "";

    [JsonPropertyName("client_id")]
    public string ClientId { get; set; } = "";

    [JsonPropertyName("rule_count")]
    public int RuleCount { get; set; } = 1;
}

public sealed class JobCreateResponse
{
    [JsonPropertyName("job_id")]
    public string JobId { get; set; } = "";

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>Job 事件请求。</summary>
public sealed class JobEventRequest
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("rule_id")]
    public string? RuleId { get; set; }

    [JsonPropertyName("level")]
    public string? Level { get; set; }

    [JsonPropertyName("params")]
    public Dictionary<string, object>? Params { get; set; }
}

/// <summary>Job 结束请求。</summary>
public sealed class JobFinishRequest
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "success";   // success|failed|partial|cancelled

    [JsonPropertyName("success_count")]
    public int SuccessCount { get; set; }

    [JsonPropertyName("failure_count")]
    public int FailureCount { get; set; }

    [JsonPropertyName("files_synced")]
    public int FilesSynced { get; set; }

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }
}
