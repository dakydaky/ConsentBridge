using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Candidate.PortalApp.Api;

public class AgentApiClient
{
    private readonly HttpClient _http;
    private readonly GatewayOptions _opts;
    private OAuthToken? _cachedToken;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public AgentApiClient(IHttpClientFactory httpFactory, IOptions<GatewayOptions> opts)
    {
        _http = httpFactory.CreateClient();
        _opts = opts.Value;
        _http.BaseAddress = new Uri(_opts.BaseUrl);
    }

    public async Task<string> CreateConsentRequestAsync(string candidateEmail, string? boardTenantId = null, string?[]? scopes = null, CancellationToken ct = default)
    {
        await EnsureTokenAsync(ct);
        var payload = new
        {
            CandidateEmail = candidateEmail,
            AgentTenantId = _opts.AgentTenantId,
            BoardTenantId = boardTenantId ?? _opts.BoardTenantId,
            Scopes = scopes is { Length: > 0 } ? scopes : new[] { _opts.OAuth.Scope }
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/consent-requests")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOpts), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cachedToken!.access_token);
        var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("request_id").GetString() ?? string.Empty;
    }

    public async Task<IReadOnlyList<ConsentRequestView>> GetConsentRequestsAsync(string email, string? status = null, CancellationToken ct = default)
    {
        await EnsureTokenAsync(ct);
        var qs = $"email={Uri.EscapeDataString(email)}&take=50" + (string.IsNullOrWhiteSpace(status) ? string.Empty : $"&status={Uri.EscapeDataString(status)}");
        using var req = new HttpRequestMessage(HttpMethod.Get, $"/v1/consent-requests?{qs}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cachedToken!.access_token);
        var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        var rows = await JsonSerializer.DeserializeAsync<List<ConsentRequestView>>(stream, JsonOpts, ct) ?? new();
        return rows;
    }

    public async Task<IReadOnlyList<ConsentView>> GetConsentsAsync(CancellationToken ct = default)
    {
        await EnsureTokenAsync(ct);
        using var req = new HttpRequestMessage(HttpMethod.Get, "/v1/consents?take=50");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cachedToken!.access_token);
        var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        var rows = await JsonSerializer.DeserializeAsync<List<ConsentView>>(stream, JsonOpts, ct) ?? new();
        return rows;
    }

    public async Task<IReadOnlyList<ApplicationRecord>> GetApplicationsAsync(string email, CancellationToken ct = default)
    {
        await EnsureTokenAsync(ct);
        using var req = new HttpRequestMessage(HttpMethod.Get, $"/v1/applications?email={Uri.EscapeDataString(email)}&take=50");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cachedToken!.access_token);
        var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        var rows = await JsonSerializer.DeserializeAsync<List<ApplicationRecord>>(stream, JsonOpts, ct) ?? new();
        return rows;
    }

    private async Task EnsureTokenAsync(CancellationToken ct)
    {
        if (_cachedToken is { IsValid: true }) return;
        var body = JsonSerializer.Serialize(new
        {
            grantType = "client_credentials",
            clientId = _opts.OAuth.ClientId,
            clientSecret = _opts.OAuth.ClientSecret,
            scope = _opts.OAuth.Scope
        }, JsonOpts);
        using var req = new HttpRequestMessage(HttpMethod.Post, _opts.OAuth.TokenEndpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync(ct);
        var token = JsonSerializer.Deserialize<OAuthToken>(json, JsonOpts)!;
        token.obtained_at = DateTimeOffset.UtcNow;
        _cachedToken = token;
    }

    public sealed record ConsentRequestView(
        Guid Id,
        string CandidateEmail,
        string Status,
        DateTime CreatedAt,
        DateTime ExpiresAt,
        DateTime? DecisionAt,
        DateTime? VerifiedAt,
        Guid? consent_id,
        string link
    );

    public sealed record ConsentView(
        Guid Id,
        string AgentTenantId,
        string BoardTenantId,
        string? ApprovedByEmail,
        IReadOnlyList<string> Scopes,
        string Status,
        DateTime IssuedAt,
        DateTime ExpiresAt,
        DateTime TokenIssuedAt,
        DateTime TokenExpiresAt,
        Guid TokenId,
        string? TokenKeyId,
        string? TokenAlgorithm,
        DateTime? RevokedAt
    );

    public sealed record ApplicationRecord(
        Guid Id,
        Guid ConsentId,
        string AgentTenantId,
        string BoardTenantId,
        string Status,
        DateTime SubmittedAt,
        string PayloadHash,
        string? SubmissionSignature,
        string? SubmissionKeyId,
        string? SubmissionAlgorithm,
        string? Receipt,
        string? ReceiptSignature,
        string? ReceiptHash
    );

    public async Task<bool> RevokeConsentAsync(Guid id, CancellationToken ct = default)
    {
        await EnsureTokenAsync(ct);
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/v1/consents/{id}/revoke");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cachedToken!.access_token);
        var res = await _http.SendAsync(req, ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> RenewConsentAsync(Guid id, CancellationToken ct = default)
    {
        await EnsureTokenAsync(ct);
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/v1/consents/{id}/renew");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cachedToken!.access_token);
        var res = await _http.SendAsync(req, ct);
        return res.IsSuccessStatusCode;
    }

    private sealed class OAuthToken
    {
        public string access_token { get; set; } = string.Empty;
        public int expires_in { get; set; }
        public string token_type { get; set; } = "Bearer";
        public DateTimeOffset obtained_at { get; set; }
        public bool IsValid => !string.IsNullOrEmpty(access_token) && obtained_at.AddSeconds(expires_in - 30) > DateTimeOffset.UtcNow;
    }
}
