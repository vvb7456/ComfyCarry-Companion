using ComfyCarry.Models;

namespace ComfyCarry.Services;

/// <summary>Job 回报面板（失败静默降级）。</summary>
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
                RuleCount = 1,
            }, ct);
        }
        catch { return null; }
    }

    public async Task EventAsync(PanelInstance inst, string? jobId, string key,
        string? ruleId = null, string? level = null, Dictionary<string, object>? pars = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(jobId)) return;
        try
        {
            await _api.AppendJobEventAsync(inst, jobId, new JobEventRequest
            {
                Key = key,
                RuleId = ruleId,
                Level = level,
                Params = pars,
            }, ct);
        }
        catch { }
    }

    public async Task FinishAsync(PanelInstance inst, string? jobId, string status,
        int successCount = 0, int failureCount = 0, int filesSynced = 0, string? summary = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(jobId)) return;
        try
        {
            await _api.FinishJobAsync(inst, jobId, new JobFinishRequest
            {
                Status = status,
                SuccessCount = successCount,
                FailureCount = failureCount,
                FilesSynced = filesSynced,
                Summary = summary,
            }, ct);
        }
        catch { }
    }
}
