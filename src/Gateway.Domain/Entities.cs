using System;
using System.Collections.Generic;

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
    public Guid TokenId { get; set; }
    public DateTime TokenExpiresAt { get; set; }
    public string? ApprovedByEmail { get; set; }
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

public class Tenant
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = default!;
    public string DisplayName { get; set; } = default!;
    public TenantType Type { get; set; }
    public string? JwksEndpoint { get; set; }
    public string? CallbackUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<TenantCredential> Credentials { get; set; } = new List<TenantCredential>();
}

public enum TenantType
{
    Agent = 0,
    Board = 1
}

public class TenantCredential
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    public string ClientId { get; set; } = default!;
    public string ClientSecretHash { get; set; } = default!;
    public string Scopes { get; set; } = "apply.submit";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastRotatedAt { get; set; }
}
