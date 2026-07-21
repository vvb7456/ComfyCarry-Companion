using System.Text.Json.Serialization;

namespace ComfyCarry.Models;

/// <summary>
/// rclone 非交互配置状态机的状态对象。
/// 形状严格对齐 rclone v1.74+ config --non-interactive 返回的 JSON：
/// { "State":"...", "Option":{...}|null, "Error":"", "Result":"" }
/// State 为空字符串表示完成；同时 Option==null 也表示无待答问题。
/// </summary>
public sealed class RcloneConfigState
{
    [JsonPropertyName("State")]
    public string State { get; set; } = "";

    [JsonPropertyName("Error")]
    public string? Error { get; set; }

    [JsonPropertyName("Result")]
    public string Result { get; set; } = "";

    [JsonPropertyName("Option")]
    public RcloneOption? Option { get; set; }

    /// <summary>状态机是否已完成（State 为空）。</summary>
    public bool IsDone => string.IsNullOrEmpty(State);

    /// <summary>是否有待答问题。</summary>
    public bool HasOption => !IsDone && Option is not null;
}

/// <summary>
/// rclone 返回的单个待答问题（单数对象，不是顶层 Choices）。
/// </summary>
public sealed class RcloneOption
{
    [JsonPropertyName("Name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("Help")]
    public string Help { get; set; } = "";

    [JsonPropertyName("Type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("Required")]
    public bool Required { get; set; }

    [JsonPropertyName("Exclusive")]
    public bool Exclusive { get; set; }

    [JsonPropertyName("DefaultStr")]
    public string? DefaultStr { get; set; }

    [JsonPropertyName("ValueStr")]
    public string? ValueStr { get; set; }

    [JsonPropertyName("Examples")]
    public List<RcloneExample> Examples { get; set; } = new();
}

/// <summary>枚举型选项的一个候选项（OneDrive 选 drive 等）。</summary>
public sealed class RcloneExample
{
    [JsonPropertyName("Value")]
    public string Value { get; set; } = "";

    [JsonPropertyName("Help")]
    public string Help { get; set; } = "";
}

/// <summary>
/// 状态机驱动结果：UI 据此判断下一步动作。
/// - Done: 流程完成，conf 已写入。
/// - Error: 出错，message 给出原因。
/// - NeedChoice: 需要用户在 Examples 中选一项，把所选 Value 通过 ContinueAsync 回灌。
/// </summary>
public enum ConfigDriveOutcome { Done, Error, NeedChoice }

public sealed class ConfigDriveResult
{
    public ConfigDriveOutcome Outcome { get; init; }

    /// <summary>需要用户选择时的候选项（Outcome==NeedChoice 时有效）。</summary>
    public IReadOnlyList<RcloneExample> Examples { get; init; } = Array.Empty<RcloneExample>();

    /// <summary>出错时的消息（Outcome==Error 时有效）。</summary>
    public string Message { get; init; } = "";

    /// <summary>最后一次的 State（用于 UI 继续时回传；驱动器内部已自行维护）。</summary>
    internal string State { get; init; } = "";

    /// <summary>当前候选项对应的 Option.Name，便于 UI 判别场景。</summary>
    public string OptionName { get; init; } = "";

    public static ConfigDriveResult Done() => new() { Outcome = ConfigDriveOutcome.Done };
    public static ConfigDriveResult Error(string msg) => new() { Outcome = ConfigDriveOutcome.Error, Message = msg };
    public static ConfigDriveResult NeedChoice(string state, string optionName, IReadOnlyList<RcloneExample> examples)
        => new() { Outcome = ConfigDriveOutcome.NeedChoice, State = state, OptionName = optionName, Examples = examples };
}
