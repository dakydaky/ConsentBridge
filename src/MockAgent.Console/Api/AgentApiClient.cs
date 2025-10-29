using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace MockAgent.ConsoleApp.Api;

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
        return doc.RootElement.TryGetProperty("request_id", out var idProp)
            ? idProp.GetString() ?? string.Empty
            : json; // fallback raw
    }

    public async Task<string> SubmitApplicationAsync(string applicationJson, CancellationToken ct = default)
    {
        await EnsureTokenAsync(ct);
        using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/applications")
        {
            Content = new StringContent(applicationJson, Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cachedToken!.access_token);
        var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsStringAsync(ct);
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

    private sealed class OAuthToken
    {
        public string access_token { get; set; } = string.Empty;
        public int expires_in { get; set; }
        public string token_type { get; set; } = "Bearer";
        public DateTimeOffset obtained_at { get; set; }
        public bool IsValid => !string.IsNullOrEmpty(access_token) && obtained_at.AddSeconds(expires_in - 30) > DateTimeOffset.UtcNow;
    }

    public async Task<IReadOnlyList<ConsentView>> GetConsentsAsync(int? take = null, CancellationToken ct = default)
    {
        await EnsureTokenAsync(ct);
        var url = "/v1/consents" + (take.HasValue ? $"?take={take.Value}" : string.Empty);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cachedToken!.access_token);
        var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        var consents = await JsonSerializer.DeserializeAsync<List<ConsentView>>(stream, JsonOpts, ct) ?? new();
        return consents;
    }

    public async Task<bool> RevokeConsentAsync(Guid id, CancellationToken ct = default)
    {
        await EnsureTokenAsync(ct);
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/v1/consents/{id}/revoke");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cachedToken!.access_token);
        var res = await _http.SendAsync(req, ct);
        if (res.IsSuccessStatusCode) return true;
        var body = await res.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException($"Revoke failed: {(int)res.StatusCode} {body}");
    }

    public async Task<RenewResult?> RenewConsentAsync(Guid id, CancellationToken ct = default)
    {
        await EnsureTokenAsync(ct);
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/v1/consents/{id}/renew");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cachedToken!.access_token);
        var res = await _http.SendAsync(req, ct);
        if (res.StatusCode == System.Net.HttpStatusCode.BadRequest) return null;
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<RenewResult>(json, JsonOpts);
    }

    public sealed record RenewResult(string token, Guid token_id, DateTime issued_at, DateTime expires_at, string kid, string alg);

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

    public async Task<IReadOnlyList<ConsentRequestView>> GetConsentRequestsAsync(string? email = null, string? status = null, int? take = null, CancellationToken ct = default)
    {
        await EnsureTokenAsync(ct);
        var qs = new List<string>();
        if (!string.IsNullOrWhiteSpace(email)) qs.Add($"email={Uri.EscapeDataString(email)}");
        if (!string.IsNullOrWhiteSpace(status)) qs.Add($"status={Uri.EscapeDataString(status)}");
        if (take.HasValue) qs.Add($"take={take.Value}");
        var url = "/v1/consent-requests" + (qs.Count > 0 ? ("?" + string.Join('&', qs)) : string.Empty);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cachedToken!.access_token);
        var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        var rows = await JsonSerializer.DeserializeAsync<List<ConsentRequestView>>(stream, JsonOpts, ct) ?? new();
        return rows;
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
}
