namespace MockAgent.ConsoleApp;

public sealed class GatewayOptions
{
    public string BaseUrl { get; set; } = "";
    public string? PublicBaseUrl { get; set; }
    public OAuthOptions OAuth { get; set; } = new();
    public string AgentTenantId { get; set; } = "agent_acme";
    public string BoardTenantId { get; set; } = "mockboard_eu";
}

public sealed class OAuthOptions
{
    public string TokenEndpoint { get; set; } = "/oauth/token";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string Scope { get; set; } = "apply.submit";
}
