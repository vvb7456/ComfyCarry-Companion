using System.Diagnostics;

namespace ComfyCarry.Services;

/// <summary>
/// 统一日志模块，写入 comfycarry.log。
/// Info 级别始终记录；Debug 级别受 EnableDebug 开关控制。
/// 线程安全，单次写入加锁。
/// </summary>
public static class AppLog
{
    private static readonly object _lock = new();
    private static string _file = "";
    private static bool _debug;

    public static void Init(string logFile, bool debug)
    {
        _file = logFile;
        _debug = debug;
    }

    public static void SetDebug(bool debug) => _debug = debug;

    public static void Info(string msg) => Write("INFO", msg);

    public static void Debug(string msg)
    {
        if (_debug) Write("DEBUG", msg);
    }

    private static void Write(string level, string msg)
    {
        if (string.IsNullOrEmpty(_file)) return;
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
                File.AppendAllText(_file,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {msg}{Environment.NewLine}");
            }
        }
        catch { /* 日志失败不影响主流程 */ }
    }
}