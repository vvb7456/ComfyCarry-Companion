using System.Diagnostics;
using System.Text.Json;
using ComfyCarry.Models;

namespace ComfyCarry.Services;

/// <summary>
/// 封装内置 rclone.exe 的调用：非交互配置状态机、lsd/mkdir/copy/move/sync、JSON 日志解析。
/// rclone.exe 视为存在于应用目录（SPEC §3.3/§3.4）。
/// </summary>
public sealed class RcloneService
{
    private readonly AppPaths _paths;
    public string ExePath => _paths.RcloneExePath;

    public RcloneService(AppPaths paths) => _paths = paths;

    public bool IsPresent => File.Exists(ExePath);

    // ---------- 非交互配置状态机（Tab1 OAuth） ----------

    /// <summary>
    /// rclone config create <name> <type> [k=v...] --non-interactive --config <conf> [--continue --state --result]
    /// 返回 rclone stdout（JSON 状态对象）。
    /// </summary>
    public async Task<RcloneConfigState> ConfigCreateAsync(
        string confPath,
        string name,
        string type,
        IEnumerable<KeyValuePair<string, string>> options,
        string? proxy = null,
        string? continueState = null,
        string? continueResult = null,
        CancellationToken ct = default)
    {
        var args = new List<string> { "config", "create", name, type };
        foreach (var kv in options)
        {
            if (!string.IsNullOrEmpty(kv.Value))
                args.Add($"{kv.Key}={kv.Value}");
        }
        args.Add("--non-interactive");
        args.Add("--config"); args.Add(confPath);
        if (continueState is { Length: > 0 })
        {
            args.Add("--continue");
            args.Add("--state"); args.Add(continueState);
            if (continueResult is { Length: > 0 }) { args.Add("--result"); args.Add(continueResult); }
        }
        // 让 rclone 自行处理 OAuth：用 --all 返回结构化状态
        args.Add("--all");
        var (code, stdout, stderr) = await RunAsync(args, proxy, ct);
        var state = ParseState(stdout, stderr);
        if (state.Error is null && code != 0 && state.IsDone)
            state.Error = $"rclone exited {code}";
        return state;
    }

    /// <summary>
    /// rclone config update <name> --continue --state <s> --result <r> --non-interactive --config <conf>
    /// 用于把用户在 GUI 选的 OneDrive drive 回灌给 rclone 状态机。
    /// </summary>
    public Task<RcloneConfigState> ConfigContinueAsync(
        string confPath, string name, string state, string result, string? proxy = null, CancellationToken ct = default)
        => ConfigCreateAsync(confPath, name, "", Enumerable.Empty<KeyValuePair<string, string>>(),
            proxy, continueState: state, continueResult: result, ct: ct);

    /// <summary>
    /// rclone authorize <type> [k=v] —— 让 rclone 自己开浏览器完成 OAuth。
    /// 仅当状态机需要单独 authorize 时使用（多数情况 config create 已自带）。
    /// </summary>
    public async Task<(int code, string stdout, string stderr)> AuthorizeAsync(
        string type, IEnumerable<KeyValuePair<string, string>> options, string? proxy = null, CancellationToken ct = default)
    {
        var args = new List<string> { "authorize", type };
        foreach (var kv in options) if (!string.IsNullOrEmpty(kv.Value)) args.Add($"{kv.Key}={kv.Value}");
        return await RunAsync(args, proxy, ct);
    }

    private static RcloneConfigState ParseState(string stdout, string stderr)
    {
        var state = new RcloneConfigState();
        // rclone --all 输出 JSON 到 stdout（可能混有日志行），找首个 { 开头行
        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        string? jsonLine = null;
        foreach (var ln in lines)
        {
            var t = ln.Trim();
            if (t.StartsWith("{")) { jsonLine = t; break; }
        }
        if (jsonLine is { Length: > 0 })
        {
            try
            {
                state = JsonSerializer.Deserialize<RcloneConfigState>(jsonLine)!;
                if (state is null) state = new RcloneConfigState();
            }
            catch { /* fallthrough */ }
        }
        if (string.IsNullOrEmpty(state.Error) && !string.IsNullOrWhiteSpace(stderr))
            state.Error = stderr.Trim();
        return state;
    }

    // ---------- 连接测试 / 建目录 ----------

    public async Task<(bool ok, string message)> LsdAsync(string confPath, string remote, string? proxy = null, CancellationToken ct = default)
    {
        var args = new[] { "lsd", $"{remote}:", "--config", confPath, "--contimeout", "30s", "--timeout", "60s" };
        var (code, stdout, stderr) = await RunAsync(args, proxy, ct);
        return (code == 0, code == 0 ? stdout : stderr);
    }

    public async Task<int> MkdirAsync(string confPath, string remote, string path, string? proxy = null, CancellationToken ct = default)
    {
        var args = new[] { "mkdir", $"{remote}:{path}", "--config", confPath };
        var (code, _, _) = await RunAsync(args, proxy, ct);
        return code;
    }

    public async Task<int> CreateStandardTreeAsync(string confPath, string remote, string? proxy = null, CancellationToken ct = default)
    {
        var dirs = new[] { "models", "models/checkpoints", "models/loras", "models/vae", "models/embeddings", "output", "input", "workflow", "temp" };
        int last = 0;
        foreach (var d in dirs)
        {
            last = await MkdirAsync(confPath, remote, d, proxy, ct);
        }
        return last;
    }

    // ---------- 拉取执行（Tab2） ----------

    /// <summary>
    /// 读取本机 rclone remote 名（per-instance webdav remote）。
    /// </summary>
    public string InstanceRemoteName(PanelInstance inst) => $"cc-{inst.Id.Substring(0, 8)}";

    /// <summary>
    /// 确保实例的 webdav remote 已写入 app conf。
    /// </summary>
    public async Task EnsureInstanceWebdavRemoteAsync(PanelInstance inst, CancellationToken ct = default)
    {
        var remoteName = InstanceRemoteName(inst);
        var obscuredPass = await ObscureAsync(inst.Password, ct);
        // config create 会覆盖同名
        var opts = new Dictionary<string, string>
        {
            ["url"] = inst.DavUrl,
            ["user"] = inst.DavUser,
            ["pass"] = obscuredPass,
            ["vendor"] = "other",
        };
        await ConfigCreateAsync(_paths.AppRcloneConf, remoteName, "webdav", opts, null, null, null, ct);
    }

    /// <summary>
    /// rclone obscure 一个密码（用于写入 conf 的 pass 字段）。
    /// </summary>
    public async Task<string> ObscureAsync(string plain, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        var (code, stdout, stderr) = await RunAsync(new[] { "obscure", plain }, null, ct);
        return code == 0 ? stdout.Trim() : "";
    }

    /// <summary>
    /// 执行拉取：rclone <method> <remote>:<remote_path> <local_path> --filter ... --multi-thread-cutoff 32M --multi-thread-streams 4 --use-json-log --stats-one-line
    /// 逐行解析 JSON 日志并回调。
    /// </summary>
    public async Task<int> PullAsync(
        PanelInstance inst,
        PullRule rule,
        Func<RcloneLogEntry, Task> onLog,
        CancellationToken ct = default)
    {
        var remoteName = InstanceRemoteName(inst);
        var src = $"{remoteName}:{rule.RemotePath}";
        var dst = rule.LocalPath;
        var args = new List<string>
        {
            rule.Method.ToRclone(), src, dst,
            "--config", _paths.AppRcloneConf,
            "--multi-thread-cutoff", "32M",
            "--multi-thread-streams", "4",
            "--use-json-log",
            "--stats-one-line",
            "--stats", "5s",
            "--transfers", "4",
            "--checkers", "8",
            "--contimeout", "30s",
            "--timeout", "120s",
            "--retries", "3",
            "--low-level-retries", "5",
        };
        foreach (var ext in rule.Filters)
        {
            var e = ext.TrimStart('.');
            args.Add("--include");
            args.Add($"*.{e}");
        }
        // 远端只读列举（WebDAV），不检查本地 mtime 差异以外的东西
        args.Add("--no-traverse");

        Directory.CreateDirectory(dst);
        return await RunStreamingAsync(args, null, onLog, ct);
    }

    /// <summary>
    /// rclone lsf —— 列出远端目录条目（用于 UI 产物列表）。
    /// </summary>
    public async Task<List<RemoteEntry>> LsfAsync(PanelInstance inst, string remotePath, CancellationToken ct = default)
    {
        var remoteName = InstanceRemoteName(inst);
        var args = new List<string>
        {
            "lsf", $"{remoteName}:{remotePath}",
            "--config", _paths.AppRcloneConf,
            "--format", "pspt",   // p=path s=size p=perm t=time
            "--separator", "|",
            "--dirs-only",        // 拆两次：先目录后文件，简化
        };
        // 简化：直接用 lsf 不带 --dirs-only 取文件，再单独取目录
        args = new List<string>
        {
            "lsf", $"{remoteName}:{remotePath}",
            "--config", _paths.AppRcloneConf,
            "--files-only",
        };
        var (code, stdout, _) = await RunAsync(args, null, ct);
        var list = new List<RemoteEntry>();
        if (code != 0) return list;
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            list.Add(new RemoteEntry { Name = line.Trim(), IsDir = false });
        }
        return list;
    }

    /// <summary>
    /// 把远端文件 URL 给 UI 做缩略图（直接 WebDAV GET，basic auth）。
    /// 返回形如 {dav_url}/{remote_path}/{name} 的 URL；认证由 UI 层处理。
    /// </summary>
    public string WebdavFileUrl(PanelInstance inst, string remotePath, string name)
    {
        var base_ = inst.DavUrl.TrimEnd('/');
        var rp = remotePath.Trim('/');
        return string.IsNullOrEmpty(rp) ? $"{base_}/{name}" : $"{base_}/{rp}/{name}";
    }

    // ---------- 进程执行 ----------

    public async Task<(int code, string stdout, string stderr)> RunAsync(
        IReadOnlyList<string> args, string? proxy, CancellationToken ct)
    {
        if (!IsPresent)
            return (127, "", "rclone.exe not found in app directory.");

        var psi = BuildPsi(args, proxy);
        using var p = Process.Start(psi)!;
        var stdoutTask = p.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        return (p.ExitCode, stdout, stderr);
    }

    private async Task<int> RunStreamingAsync(
        IReadOnlyList<string> args, string? proxy,
        Func<RcloneLogEntry, Task> onLog, CancellationToken ct)
    {
        if (!IsPresent) { await onLog(new RcloneLogEntry { Level = "error", Msg = "rclone.exe not found." }); return 127; }

        var psi = BuildPsi(args, proxy);
        psi.RedirectStandardError = false; // JSON 日志走 stdout
        using var p = Process.Start(psi)!;
        // rclone --use-json-log 输出到 stdout，逐行读
        while (!p.StandardOutput.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await p.StandardOutput.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line)) continue;
            var entry = TryParseLog(line);
            if (entry is not null) await onLog(entry);
        }
        await p.WaitForExitAsync(ct);
        return p.ExitCode;
    }

    private ProcessStartInfo BuildPsi(IReadOnlyList<string> args, string? proxy)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ExePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (!string.IsNullOrEmpty(proxy))
        {
            psi.EnvironmentVariables["HTTP_PROXY"] = proxy;
            psi.EnvironmentVariables["HTTPS_PROXY"] = proxy;
        }
        return psi;
    }

    private static RcloneLogEntry? TryParseLog(string line)
    {
        var t = line.Trim();
        if (!t.StartsWith("{")) return new RcloneLogEntry { Level = "info", Msg = t };
        try
        {
            return JsonSerializer.Deserialize<RcloneLogEntry>(t);
        }
        catch
        {
            return new RcloneLogEntry { Level = "info", Msg = t };
        }
    }
}
