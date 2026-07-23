using System.Text.RegularExpressions;

namespace ComfyCarry.Services;

/// <summary>
/// 将 rclone exit code + 日志错误映射为用户可读的提示文本。
/// </summary>
public static class RcloneErrorMapper
{
    /// <summary>exit code → localization key</summary>
    private static readonly Dictionary<int, string> ExitCodeKeys = new()
    {
        [0] = "pull.error.success",
        [1] = "pull.error.generic",
        [2] = "pull.error.syntax",
        [3] = "pull.error.notfound",
        [4] = "pull.error.noremote",
        [5] = "pull.error.toomanyretries",
        [6] = "pull.error.nomounts",
        [7] = "pull.error.fatal",
        [8] = "pull.error.transfer",
        [9] = "pull.error.dirnotfound",
    };

    /// <summary>错误日志关键词 → localization key（优先于 exit code）</summary>
    private static readonly (Regex pattern, string key)[] LogPatterns =
    {
        (new(@"connection refused|actively refused", RegexOptions.IgnoreCase), "pull.error.connection_refused"),
        (new(@"dial tcp.*timeout|connectex.*timeout|i/o timeout", RegexOptions.IgnoreCase), "pull.error.timeout"),
        (new(@"no such host|dns|resolve", RegexOptions.IgnoreCase), "pull.error.dns"),
        (new(@"401|unauthorized|authentication", RegexOptions.IgnoreCase), "pull.error.auth"),
        (new(@"403|forbidden", RegexOptions.IgnoreCase), "pull.error.forbidden"),
        (new(@"500|internal server error", RegexOptions.IgnoreCase), "pull.error.server500"),
        (new(@"404|not found", RegexOptions.IgnoreCase), "pull.error.notfound"),
        (new(@"disk.*full|no space|insufficient", RegexOptions.IgnoreCase), "pull.error.diskfull"),
        (new(@"permission denied|access denied", RegexOptions.IgnoreCase), "pull.error.permission"),
    };

    /// <summary>
    /// 综合退出码和错误日志，返回 localization key。
    /// </summary>
    public static string Map(int exitCode, string? lastErrorMsg)
    {
        if (!string.IsNullOrEmpty(lastErrorMsg))
        {
            foreach (var (pattern, key) in LogPatterns)
            {
                if (pattern.IsMatch(lastErrorMsg)) return key;
            }
        }
        return ExitCodeKeys.TryGetValue(exitCode, out var k) ? k : "pull.error.unknown";
    }
}
