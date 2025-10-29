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
}

