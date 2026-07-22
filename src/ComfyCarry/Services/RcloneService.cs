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
        var state = ParseState(stdout, stderr, code);
        // 落盘诊断（不记 args，避免泄露密钥）：便于定位 OAuth 状态机走向
        LogLine($"config name={name} type={type} continue={(continueState is { Length: > 0 })} exit={code} " +
                $"stdoutLen={stdout.Length} -> State='{state.State}' Option='{state.Option?.Name}' Error='{state.Error}'");
        return state;
    }

    private void LogLine(string msg)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_paths.LogFile)!);
            File.AppendAllText(_paths.LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [rclone] {msg}{Environment.NewLine}");
        }
        catch { /* 日志失败不影响主流程 */ }
    }

    // rclone OAuth 回调固定端口
    private const int OAuthPort = 53682;

    /// <summary>rclone 起授权本地服务失败（端口被占）的特征识别。</summary>
    private static bool LooksLikePortBindError(string? err)
    {
        if (string.IsNullOrEmpty(err)) return false;
        return err.Contains("53682")
            || err.Contains("auth webserver", StringComparison.OrdinalIgnoreCase)
            || err.Contains("forbidden by its access permissions", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 杀掉占用指定本地端口的进程。rclone OAuth 固定用 53682，卡住的前次授权会永久占用它，
    /// 后续 bind 报 WSAEACCES。用 netstat -ano 精确定位监听该端口的 PID，只杀占端口者，
    /// 不误伤后台 pull 的 rclone。
    /// </summary>
    public void KillPortListener(int port)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netstat",
                Arguments = "-ano",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return;
            var outp = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);

            var suffix = ":" + port;
            var pids = new HashSet<int>();
            foreach (var line in outp.Split('\n'))
            {
                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                // 形如: TCP  127.0.0.1:53682  0.0.0.0:0  LISTENING  7820
                if (parts.Length >= 5
                    && parts[0].Equals("TCP", StringComparison.OrdinalIgnoreCase)
                    && parts[1].EndsWith(suffix, StringComparison.Ordinal)
                    && int.TryParse(parts[^1], out var pid) && pid > 0)
                {
                    pids.Add(pid);
                }
            }
            foreach (var pid in pids)
            {
                try { using var proc = Process.GetProcessById(pid); proc.Kill(true); LogLine($"已杀占用 {port} 的进程 pid={pid}"); }
                catch { /* 进程可能已退出 */ }
            }
        }
        catch { /* 清理失败不阻塞主流程 */ }
    }

    /// <summary>
    /// 检测端口是否落在 Windows 排除范围（Hyper-V/WSL 动态保留），
    /// 若是则提权重启 winnat 释放动态保留后端口即可绑定。
    /// </summary>
    public bool TryReleaseExcludedPort(int port)
    {
        try
        {
            // 1. 检查是否在排除范围
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = "interface ipv4 show excludedportrange protocol=tcp",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return false;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);

            bool inExcludedRange = false;
            foreach (var line in output.Split('\n'))
            {
                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && int.TryParse(parts[0], out var start) && int.TryParse(parts[1], out var end))
                {
                    if (port >= start && port <= end) { inExcludedRange = true; break; }
                }
            }
            if (!inExcludedRange) return false;

            LogLine($"端口 {port} 在 Windows 排除范围内，尝试提权重启 winnat 释放");

            // 2. 提权重启 winnat（释放动态端口保留）
            var elev = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c net stop winnat & timeout /t 2 /nobreak >nul & net start winnat",
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using var ep = Process.Start(elev);
            ep?.WaitForExit(15000);
            LogLine("winnat 重启完成");
            return true;
        }
        catch (Exception ex)
        {
            LogLine($"TryReleaseExcludedPort 失败: {ex.Message}");
            return false;
        }
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
        CancellationToken ct = default,
        IReadOnlyDictionary<string, string>? autoAnswers = null)
    {
        var st = await ConfigCreateAsync(confPath, name, type, options, proxy, null, null, ct);
        return await DriveLoopAsync(confPath, name, type, st, proxy, progress, ct, autoAnswers);
    }

    /// <summary>
    /// 在用户选完一项后继续驱动状态机，直到 done 或下一个 NeedChoice。
    /// </summary>
    public async Task<ConfigDriveResult> ConfigDriveContinueAsync(
        string confPath, string name, string type, ConfigDriveResult last, string result,
        string? proxy = null, IProgress<string>? progress = null, CancellationToken ct = default,
        IReadOnlyDictionary<string, string>? autoAnswers = null)
    {
        if (last.Outcome != ConfigDriveOutcome.NeedChoice)
            return last;
        var st = await ConfigContinueAsync(confPath, name, type, last.State, result, proxy, ct);
        return await DriveLoopAsync(confPath, name, type, st, proxy, progress, ct, autoAnswers);
    }

    private async Task<ConfigDriveResult> DriveLoopAsync(
        string confPath, string name, string type,
        RcloneConfigState st, string? proxy, IProgress<string>? progress, CancellationToken ct,
        IReadOnlyDictionary<string, string>? autoAnswers = null)
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
            LogLine($"step={step} opt.Name={opt.Name} Exclusive={opt.Exclusive} Examples={opt.Examples.Count}");

            // config_is_local：自动继续，result=true。
            // 这一步 rclone 打开浏览器并起本地 127.0.0.1:53682 回调，阻塞到登录完成。
            // 卡住的前次授权会永久占用 53682 → 后续 bind 报 WSAEACCES。先清理占用者；失败再清一次重试。
            if (string.Equals(opt.Name, "config_is_local", StringComparison.Ordinal))
            {
                progress?.Report("browser");
                var localState = st.State;
                KillPortListener(OAuthPort);
                st = await ConfigContinueAsync(confPath, name, type, localState, "true", proxy, ct);
                if (LooksLikePortBindError(st.Error))
                {
                    LogLine("53682 绑定失败，清理占用者 + 检测排除范围后重试");
                    KillPortListener(OAuthPort);
                    TryReleaseExcludedPort(OAuthPort);
                    await Task.Delay(1000, ct);
                    st = await ConfigContinueAsync(confPath, name, type, localState, "true", proxy, ct);
                }
                // 只有确认 rclone 无错误才报告登录完成，否则让循环顶部的错误检查处理
                if (string.IsNullOrEmpty(st.Error))
                    progress?.Report("login_done");
                continue;
            }

            // 自动回答优先：无论 Exclusive 与否，只要 autoAnswers 命中就自动继续
            if (autoAnswers is not null && autoAnswers.TryGetValue(opt.Name, out var autoVal))
            {
                string? answer = ResolveAutoAnswer(autoVal, opt.Examples);
                if (answer is not null)
                {
                    LogLine($"auto-answer {opt.Name} = {answer}");
                    st = await ConfigContinueAsync(confPath, name, type, st.State, answer, proxy, ct);
                    continue;
                }
                // __match 未命中 → 不自动回答，落到下面的 NeedChoice
                LogLine($"auto-answer {opt.Name}: match failed, falling through to user choice. Examples: [{string.Join(" | ", opt.Examples.Select(e => e.Help))}]");
            }

            // 真正要用户选的：有 Examples 列表且非 bool 类型（无论 Exclusive 与否都展示给用户选）
            if (!string.Equals(opt.Type, "bool", StringComparison.OrdinalIgnoreCase) && opt.Examples.Count > 1)
            {
                LogLine($"NeedChoice {opt.Name}: [{string.Join(" | ", opt.Examples.Select(e => $"{e.Value}={e.Help}"))}]");
                return ConfigDriveResult.NeedChoice(st.State, opt.Name, opt.Examples);
            }

            // 其它（bool y/n、Required==false 可选项）：用默认值自动继续
            var result = !string.IsNullOrEmpty(opt.DefaultStr) ? opt.DefaultStr : "";
            st = await ConfigContinueAsync(confPath, name, type, st.State, result, proxy, ct);
        }

        return ConfigDriveResult.Error($"rclone config loop exceeded {DriveMaxSteps} steps.");
    }

    /// <summary>
    /// 解析 AutoAnswers 值：
    ///   "__first__" → 选 Examples[0].Value
    ///   "__exact:text__" → 精确匹配 Help 文本（不区分大小写），返回首个命中的 Value；未命中返回 null
    ///   "__match:keyword__" → 在 Examples 的 Help 中不区分大小写搜索 keyword，返回首个命中的 Value；未命中返回 null
    ///   其它 → 直接作为字面量值返回
    /// </summary>
    private static string? ResolveAutoAnswer(string autoVal, List<RcloneExample> examples)
    {
        if (autoVal == "__first__")
            return examples.Count > 0 ? examples[0].Value : null;

        if (autoVal.StartsWith("__exact:", StringComparison.Ordinal) && autoVal.EndsWith("__", StringComparison.Ordinal))
        {
            var target = autoVal[8..^2];
            var match = examples.FirstOrDefault(e =>
                e.Help.Equals(target, StringComparison.OrdinalIgnoreCase));
            return match?.Value;
        }

        if (autoVal.StartsWith("__match:", StringComparison.Ordinal) && autoVal.EndsWith("__", StringComparison.Ordinal))
        {
            var keyword = autoVal[8..^2];
            var match = examples.FirstOrDefault(e =>
                e.Help.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            return match?.Value;
        }

        return autoVal;
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
        var src = $"{remoteName}:{rule.Source}";
        var dst = rule.LocalPath;
        var args = new List<string>
        {
            rule.Method, src, dst,
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
        args.AddRange(BuildFilterArgs(rule));
        // 远端只读列举（WebDAV），不检查本地 mtime 差异以外的东西
        args.Add("--no-traverse");

        Directory.CreateDirectory(dst);
        return await RunStreamingAsync(args, null, onLog, ct);
    }

    private static List<string> BuildFilterArgs(PullRule rule)
    {
        var args = new List<string>();
        switch (rule.Content)
        {
            case "images":
                args.Add("--include");
                args.Add("*.{png,jpg,jpeg,webp,gif,bmp,tiff,tif}");
                break;
            case "videos":
                args.Add("--include");
                args.Add("*.{mp4,mov,webm,mkv,avi}");
                break;
            // "all": 不加 include
        }
        if (!rule.Subdirs)
        {
            args.Add("--max-depth");
            args.Add("1");
        }
        return args;
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
            "--format", "ps",   // p=path s=size
            "--separator", "|",
            // 不加 --files-only / --dirs-only：同时列文件和目录
        };
        var (code, stdout, _) = await RunAsync(args, null, ct);
        var list = new List<RemoteEntry>();
        if (code != 0) return list;
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('|');
            var name = parts[0].Trim();
            if (string.IsNullOrEmpty(name)) continue;
            // rclone lsf 对目录输出带尾部 '/'，文件无
            bool isDir = name.EndsWith('/');
            list.Add(new RemoteEntry
            {
                Name = isDir ? name.TrimEnd('/') : name,
                IsDir = isDir,
                Size = parts.Length > 1 && long.TryParse(parts[1], out var sz) ? sz : 0,
            });
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
        try
        {
            await p.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { if (!p.HasExited) p.Kill(true); } catch { /* ignore */ }
            throw;
        }
        // 进程退出后，孙进程可能仍持有管道句柄导致 ReadToEnd hang。
        // 加 3 秒超时兜底，超时则取已读到的内容。
        var readAll = Task.WhenAll(stdoutTask, stderrTask);
        var timeout = Task.Delay(3000, CancellationToken.None);
        await Task.WhenAny(readAll, timeout);
        var stdout = stdoutTask.IsCompletedSuccessfully ? stdoutTask.Result : "";
        var stderr = stderrTask.IsCompletedSuccessfully ? stderrTask.Result : "";
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
