using System.Diagnostics;
using System.Text.Json;
using ComfyCarry.Models;

namespace ComfyCarry.Services;

/// <summary>
/// 封装内置 rclone.exe 的调用：实例 webdav remote 写入、lsf/copy/move/sync、JSON 日志解析。
/// rclone.exe 视为存在于应用目录（SPEC §3.4）。
/// </summary>
public sealed class RcloneService
{
    private readonly AppPaths _paths;
    private readonly SettingsService _settings;
    public string ExePath => _paths.RcloneExePath;

    public RcloneService(AppPaths paths, SettingsService settings)
    {
        _paths = paths;
        _settings = settings;
    }

    public bool IsPresent => File.Exists(ExePath);

    // ---------- 拉取执行 ----------

    /// <summary>
    /// 读取本机 rclone remote 名（per-instance webdav remote）。
    /// </summary>
    public string InstanceRemoteName(PanelInstance inst) => $"cc-{inst.Id.Substring(0, 8)}";

    /// <summary>
    /// 确保实例的 webdav remote 已写入 app conf（config create 会覆盖同名）。
    /// </summary>
    public async Task EnsureInstanceWebdavRemoteAsync(PanelInstance inst, CancellationToken ct = default)
    {
        var remoteName = InstanceRemoteName(inst);
        var obscuredPass = await ObscureAsync(inst.Password, ct);
        var args = new List<string>
        {
            "config", "create", remoteName, "webdav",
            $"url={inst.DavUrl}",
            $"user={inst.DavUser}",
            $"pass={obscuredPass}",
            "vendor=other",
            "--non-interactive",
            "--config", _paths.PullRcloneConf,
        };
        // 不记 args，避免泄露凭据
        var (code, _, stderr) = await RunAsync(args, null, ct);
        AppLog.Info($"[rclone] config create name={remoteName} type=webdav exit={code}");
        if (code != 0)
            throw new Exception(stderr.Length > 0 ? stderr.Trim() : $"rclone config create exited {code}");
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
    /// 列出远端被规则白名单匹配的文件（带 filter），用于 watch 预检。
    /// 返回 (相对路径, 大小) 列表。
    /// </summary>
    public async Task<List<(string Path, long Size)>> ListRemoteFilesAsync(
        PanelInstance inst, PullRule rule, CancellationToken ct = default)
    {
        var remoteName = InstanceRemoteName(inst);
        var args = new List<string>
        {
            "lsf", $"{remoteName}:",
            "--config", _paths.PullRcloneConf,
            "--format", "ps",
            "--separator", "|",
            "--files-only",
            "--recursive",
        };
        args.AddRange(BuildFilterArgs(rule));
        var (code, stdout, stderr) = await RunAsync(args, _settings.Data.Proxy, ct);
        if (code != 0)
            throw new Exception(stderr.Length > 0 ? stderr.Trim() : $"rclone lsf exited {code}");
        var list = new List<(string, long)>();
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('|');
            var name = parts[0].Trim();
            if (string.IsNullOrEmpty(name)) continue;
            long size = parts.Length > 1 && long.TryParse(parts[1], out var sz) ? sz : 0;
            list.Add((name, size));
        }
        return list;
    }

    /// <summary>
    /// 执行拉取：rclone &lt;method&gt; &lt;remote&gt;: &lt;local_path&gt; --filter ... --multi-thread-cutoff 32M --multi-thread-streams 4 --use-json-log --stats-one-line
    /// 逐行解析 JSON 日志并回调。
    /// </summary>
    public async Task<int> PullAsync(
        PanelInstance inst,
        PullRule rule,
        Func<RcloneLogEntry, Task> onLog,
        CancellationToken ct = default)
    {
        var remoteName = InstanceRemoteName(inst);
        var src = $"{remoteName}:";
        var dst = rule.LocalPath;
        var args = new List<string>
        {
            rule.Method, src, dst,
            "--config", _paths.PullRcloneConf,
            "--multi-thread-cutoff", "32M",
            "--multi-thread-streams", "4",
            "-v",
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
        // 文件稳定延迟：跳过最近修改的文件，防止拉取写入中的不完整文件
        var minAge = _settings.Data.MinAgeSec;
        if (minAge > 0)
        {
            args.Add("--min-age");
            args.Add($"{minAge}s");
        }

        Directory.CreateDirectory(dst);
        return await RunStreamingAsync(args, _settings.Data.Proxy, onLog, ct);
    }

    private static List<string> BuildFilterArgs(PullRule rule)
    {
        var args = new List<string>
        {
            "--filter", "- .*/**",
            "--filter", "- _output_images_will_be_put_here",
        };
        if (rule.Subdirs)
        {
            switch (rule.Content)
            {
                case "images":
                    args.Add("--filter"); args.Add("+ *.{png,jpg,jpeg,webp,gif,bmp,tiff,tif}");
                    args.Add("--filter"); args.Add("+ **/*.{png,jpg,jpeg,webp,gif,bmp,tiff,tif}");
                    break;
                case "videos":
                    args.Add("--filter"); args.Add("+ *.{mp4,mov,webm,mkv,avi}");
                    args.Add("--filter"); args.Add("+ **/*.{mp4,mov,webm,mkv,avi}");
                    break;
            }
        }
        else
        {
            switch (rule.Content)
            {
                case "images":
                    args.Add("--filter"); args.Add("+ *.{png,jpg,jpeg,webp,gif,bmp,tiff,tif}");
                    break;
                case "videos":
                    args.Add("--filter"); args.Add("+ *.{mp4,mov,webm,mkv,avi}");
                    break;
            }
            args.Add("--max-depth");
            args.Add("1");
        }
        args.Add("--filter"); args.Add("- *");
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
            "--config", _paths.PullRcloneConf,
            "--format", "ps",   // p=path s=size
            "--separator", "|",
            // 不加 --files-only / --dirs-only：同时列文件和目录
        };
        var (code, stdout, stderr) = await RunAsync(args, null, ct);
        if (code != 0)
            throw new Exception(stderr.Length > 0 ? stderr.Trim() : $"rclone lsf exited {code}");
        var list = new List<RemoteEntry>();
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
        using var p = Process.Start(psi)!;
        // 注册取消：杀掉 rclone 进程树，否则进程会继续跑
        await using var _ = ct.Register(() =>
        {
            try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { /* ignore */ }
        });
        // rclone --use-json-log 的 JSON 行走 stderr（非 stdout），逐行读
        // 用 Task.WhenAny 防止孙进程持有管道导致 ReadLineAsync 永久阻塞
        var readTask = ReadAllLinesAsync(p, onLog, ct);
        var exitTask = p.WaitForExitAsync(ct);
        await Task.WhenAny(readTask, exitTask);
        // 如果进程先退出但读未完成，再等一会
        if (p.HasExited && !readTask.IsCompleted)
        {
            await Task.WhenAny(readTask, Task.Delay(3000, CancellationToken.None));
        }
        ct.ThrowIfCancellationRequested();
        if (!p.HasExited)
        {
            try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
            await p.WaitForExitAsync(CancellationToken.None);
        }
        AppLog.Info($"[rclone] 进程退出 code={p.ExitCode}");
        return p.ExitCode;
    }

    private async Task ReadAllLinesAsync(Process p, Func<RcloneLogEntry, Task> onLog, CancellationToken ct)
    {
        while (!p.StandardError.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await p.StandardError.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line)) continue;
            var entry = TryParseLog(line);
            if (entry is not null) await onLog(entry);
        }
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
