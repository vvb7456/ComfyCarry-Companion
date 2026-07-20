using ComfyCarry.Models;

namespace ComfyCarry.Services;

/// <summary>
/// 把 rclone 拉取过程回报面板 Job（SPEC §2.5）。
/// 失败静默降级（不阻塞拉取）。
/// </summary>
public sealed class JobReporter
{
    private readonly CompanionApiClient _api;

    public JobReporter(CompanionApiClient api) => _api = api;

    public async Task<string?> StartAsync(PanelInstance inst, PullRule rule, CancellationToken ct = default)
    {
        try
        {
            return await _api.CreateJobAsync(inst, new JobCreateRequest
            {
                RuleId = rule.RuleId,
                ClientId = inst.ClientId,
                TriggerType = "companion",
            }, ct);
        }
        catch { return null; }
    }

    public async Task EventAsync(PanelInstance inst, string? jobId, JobEventRequest ev, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(jobId)) return;
        try { await _api.AppendJobEventAsync(inst, jobId, ev, ct); }
        catch { /* 降级 */ }
    }

    public async Task FinishAsync(PanelInstance inst, string? jobId, string status, string? msg, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(jobId)) return;
        try
        {
            await _api.FinishJobAsync(inst, jobId, new JobFinishRequest
            {
                Status = status,
                Message = msg,
            }, ct);
        }
        catch { /* 降级 */ }
    }
}
