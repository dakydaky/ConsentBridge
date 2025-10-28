namespace Gateway.Domain;

public class Candidate
{
    public Guid Id { get; set; }
    public string EmailHash { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
}

public class Consent
{
    public Guid Id { get; set; }
    public Guid CandidateId { get; set; }
    public Candidate? Candidate { get; set; }
    public string AgentTenantId { get; set; } = default!;
    public string BoardTenantId { get; set; } = default!;
    public string Scopes { get; set; } = "apply:submit";
    public ConsentStatus Status { get; set; }
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

public enum ConsentStatus
{
    Active = 1,
    Revoked = 2
}

public class Application
{
    public Guid Id { get; set; }
    public Guid ConsentId { get; set; }
    public Consent? Consent { get; set; }
    public string AgentTenantId { get; set; } = default!;
    public string BoardTenantId { get; set; } = default!;
    public ApplicationStatus Status { get; set; }
    public DateTime SubmittedAt { get; set; }
    public string? Receipt { get; set; }
    public string PayloadHash { get; set; } = default!;
}

public enum ApplicationStatus
{
    Pending = 1,
    Accepted = 2,
    Failed = 3
}
