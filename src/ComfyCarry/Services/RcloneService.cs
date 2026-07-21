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
        var (code, stdout, stderr) = await RunAsync(args, proxy, ct);
        return ParseState(stdout, stderr, code);
    }

    /// <summary>
    /// 继续状态机：把用户的选择（或驱动的自动应答）回灌给 rclone。
    /// 仍以 config create &lt;name&gt; &lt;type&gt; --continue 形式发起（实测缺 type 会报 couldn't find type field）。
    /// </summary>
    public Task<RcloneConfigState> ConfigContinueAsync(
        string confPath, string name, string type, string state, string result, string? proxy = null, CancellationToken ct = default)
        => ConfigCreateAsync(confPath, name, type, Enumerable.Empty<KeyValuePair<string, string>>(),
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

    // ---------- 状态机驱动器：机器类问题自动应答，只把"真正要用户选的"回给 UI ----------

    private const int DriveMaxSteps = 25;

    /// <summary>
    /// 启动 OAuth/参数配置状态机：config create &lt;name&gt; &lt;type&gt; [k=v] --non-interactive。
    /// 自动处理 config_is_local（开浏览器、阻塞到登录完成）、bool y/n、Required==false 可选项；
    /// 遇 Exclusive==true && Type!="bool" && Examples.Count>0 的"真正用户选择"（如 OneDrive 选 drive）则返回 NeedChoice。
    /// progress 用于把"浏览器已打开，请在浏览器完成登录…"之类提示推给 UI。
    /// </summary>
    public async Task<ConfigDriveResult> ConfigDriveAsync(
        string confPath, string name, string type,
        IEnumerable<KeyValuePair<string, string>> options,
        string? proxy = null,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var st = await ConfigCreateAsync(confPath, name, type, options, proxy, null, null, ct);
        return await DriveLoopAsync(confPath, name, type, st, proxy, progress, ct);
    }

    /// <summary>
    /// 在用户选完一项后继续驱动状态机，直到 done 或下一个 NeedChoice。
    /// </summary>
    public async Task<ConfigDriveResult> ConfigDriveContinueAsync(
        string confPath, string name, string type, ConfigDriveResult last, string result,
        string? proxy = null, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (last.Outcome != ConfigDriveOutcome.NeedChoice)
            return last;
        var st = await ConfigContinueAsync(confPath, name, type, last.State, result, proxy, ct);
        return await DriveLoopAsync(confPath, name, type, st, proxy, progress, ct);
    }

    private async Task<ConfigDriveResult> DriveLoopAsync(
        string confPath, string name, string type,
        RcloneConfigState st, string? proxy, IProgress<string>? progress, CancellationToken ct)
    {
        for (int step = 0; step < DriveMaxSteps; step++)
        {
            // 完成
            if (st.IsDone)
                return ConfigDriveResult.Done();

            // 出错
            if (!string.IsNullOrEmpty(st.Error))
                return ConfigDriveResult.Error(st.Error);

            // 无待答问题但未完成：视为异常终止
            if (st.Option is null)
                return ConfigDriveResult.Error("rclone returned no option but is not done.");

            var opt = st.Option;

            // config_is_local：自动继续，result=true。
            // 这一步 rclone 会打开浏览器并起本地 127.0.0.1:53682 回调，阻塞到登录完成。
            if (string.Equals(opt.Name, "config_is_local", StringComparison.Ordinal))
            {
                progress?.Report("browser");
                st = await ConfigContinueAsync(confPath, name, type, st.State, "true", proxy, ct);
                progress?.Report("login_done");
                continue;
            }

            // 真正要用户选的：Exclusive && Type!=bool && Examples.Count>0
            if (opt.Exclusive && !string.Equals(opt.Type, "bool", StringComparison.OrdinalIgnoreCase) && opt.Examples.Count > 0)
            {
                return ConfigDriveResult.NeedChoice(st.State, opt.Name, opt.Examples);
            }

            // 其它（bool y/n、Required==false 可选项）：用默认值自动继续
            var result = !string.IsNullOrEmpty(opt.DefaultStr) ? opt.DefaultStr : "";
            st = await ConfigContinueAsync(confPath, name, type, st.State, result, proxy, ct);
        }

        return ConfigDriveResult.Error($"rclone config loop exceeded {DriveMaxSteps} steps.");
    }

    private static RcloneConfigState ParseState(string stdout, string stderr, int code)
    {
        // rclone --non-interactive 把 JSON 状态对象输出到 stdout，且是【多行美化】JSON（实测 33 行），
        // 日志/NOTICE（含"浏览器 URL""Config file not found"）走 stderr。
        // 因此取 stdout 里首个 '{' 到末个 '}' 的整段解析，不能按单行找。
        var start = stdout.IndexOf('{');
        var end = stdout.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            try
            {
                var json = stdout.Substring(start, end - start + 1);
                var s = JsonSerializer.Deserialize<RcloneConfigState>(json);
                if (s is not null) return s;   // rclone 把真正的错误放在 JSON 的 "Error" 字段
            }
            catch { /* 落到下面的兜底 */ }
        }
        // 无可解析 JSON：仅当进程失败时才把 stderr 当错误（stderr 常态含无害 NOTICE，不能直接当错误）。
        var state = new RcloneConfigState();
        if (code != 0)
            state.Error = !string.IsNullOrWhiteSpace(stderr) ? stderr.Trim() : $"rclone exited {code}";
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
