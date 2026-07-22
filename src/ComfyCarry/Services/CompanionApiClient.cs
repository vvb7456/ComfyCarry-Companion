using System.Net;
using System.Text;
using System.Text.Json;
using ComfyCarry.Models;

namespace ComfyCarry.Services;

/// <summary>
/// 面板 JSON API 客户端（SPEC §2.2–§2.5）。
/// 401 时用密码重新 connect 刷新 api_key（SPEC §3.6）。
/// </summary>
public sealed class CompanionApiClient : IDisposable
{
    private readonly InstanceStore _instances;
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public CompanionApiClient(InstanceStore instances)
    {
        _instances = instances;
        _http = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
        })
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    /// <summary>用密码换 api_key + dav_url（SPEC §2.2）。</summary>
    public async Task<ConnectResponse> ConnectAsync(string baseUrl, string password, CancellationToken ct = default)
    {
        var url = NormalizeUrl(baseUrl) + "/api/companion/connect";
        var body = JsonSerializer.Serialize(new { password });
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        using var resp = await _http.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        var cr = JsonSerializer.Deserialize<ConnectResponse>(text, JsonOpts) ?? new ConnectResponse();
        if (!resp.IsSuccessStatusCode && string.IsNullOrEmpty(cr.Error))
            cr.Error = $"HTTP {resp.StatusCode}";
        cr.Ok = resp.IsSuccessStatusCode && cr.Ok;
        return cr;
    }

    public async Task<bool> SendHeartbeatAsync(PanelInstance inst, HeartbeatRequest hb, CancellationToken ct = default)
    {
        var url = $"{NormalizeUrl(inst.BaseUrl)}/api/companion/heartbeat";
        var body = JsonSerializer.Serialize(hb, JsonOpts);
        using var resp = await SendWithAuthAsync(inst, HttpMethod.Post, url, body, ct);
        return resp.IsSuccessStatusCode;
    }

    public async Task<string?> CreateJobAsync(PanelInstance inst, JobCreateRequest req, CancellationToken ct = default)
    {
        var url = $"{NormalizeUrl(inst.BaseUrl)}/api/companion/jobs";
        var body = JsonSerializer.Serialize(req, JsonOpts);
        using var resp = await SendWithAuthAsync(inst, HttpMethod.Post, url, body, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        var jr = JsonSerializer.Deserialize<JobCreateResponse>(text, JsonOpts);
        return jr?.JobId;
    }

    public async Task<bool> AppendJobEventAsync(PanelInstance inst, string jobId, JobEventRequest ev, CancellationToken ct = default)
    {
        var url = $"{NormalizeUrl(inst.BaseUrl)}/api/companion/jobs/{Uri.EscapeDataString(jobId)}/events";
        var body = JsonSerializer.Serialize(ev, JsonOpts);
        using var resp = await SendWithAuthAsync(inst, HttpMethod.Post, url, body, ct);
        return resp.IsSuccessStatusCode;
    }

    public async Task<bool> FinishJobAsync(PanelInstance inst, string jobId, JobFinishRequest fin, CancellationToken ct = default)
    {
        var url = $"{NormalizeUrl(inst.BaseUrl)}/api/companion/jobs/{Uri.EscapeDataString(jobId)}/finish";
        var body = JsonSerializer.Serialize(fin, JsonOpts);
        using var resp = await SendWithAuthAsync(inst, HttpMethod.Post, url, body, ct);
        return resp.IsSuccessStatusCode;
    }

    // ---------- 401 自动重连 ----------

    private async Task<HttpResponseMessage> SendWithAuthAsync(
        PanelInstance inst, HttpMethod method, string url, string? jsonBody, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            using var req = new HttpRequestMessage(method, url);
            if (!string.IsNullOrEmpty(inst.ApiKey))
                req.Headers.TryAddWithoutValidation("X-API-Key", inst.ApiKey);
            if (jsonBody is not null)
                req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            var resp = await _http.SendAsync(req, ct);
            if (resp.StatusCode != HttpStatusCode.Unauthorized) return resp;
            resp.Dispose();
            // 401 → 用密码重新 connect 刷新 api_key
            var cr = await ConnectAsync(inst.BaseUrl, inst.Password, ct);
            if (!cr.Ok || string.IsNullOrEmpty(cr.ApiKey)) return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            inst.ApiKey = cr.ApiKey;
            _instances.Save();
        }
        return new HttpResponseMessage(HttpStatusCode.Unauthorized);
    }

    private static string NormalizeUrl(string url)
    {
        var u = url.Trim().TrimEnd('/');
        if (!u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            u = "https://" + u;
        return u;
    }

    public void Dispose() => _http.Dispose();
}
