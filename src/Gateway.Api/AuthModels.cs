namespace Gateway.Api;

public record ClientCredentialsPayload(string GrantType, string ClientId, string ClientSecret, string? Scope);
