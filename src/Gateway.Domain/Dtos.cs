namespace Gateway.Domain;

public record CreateConsentRequestDto(string CandidateEmail, string AgentTenantId, string BoardTenantId, IReadOnlyList<string> Scopes);
public record VerifyConsentRequestDto(string Code);
public record ConsentDecisionDto(bool Approve);

public record ApplyPayloadDto(
    string ConsentToken,
    CandidateDto Candidate,
    JobRefDto Job,
    MaterialsDto Materials,
    MetaDto Meta
);

public record CandidateDto(string Id, ContactDto Contact, PiiDto Pii, DocumentDto Cv);
public record ContactDto(string Email, string? Phone);
public record PiiDto(string FirstName, string LastName);
public record DocumentDto(string Url, string Sha256);

public record JobRefDto(string ExternalId, string Title, string Company, string ApplyEndpoint);

public record MaterialsDto(CoverLetterDto CoverLetter, IReadOnlyList<AnswerDto> Answers);
public record CoverLetterDto(string Text);
public record AnswerDto(string QuestionId, string AnswerText);

public record MetaDto(string Locale, string UserAgent, DateTime Ts);

public record ConsentViewDto(
    Guid Id,
    string AgentTenantId,
    string BoardTenantId,
    string? CandidateEmail,
    IReadOnlyList<string> Scopes,
    ConsentStatus Status,
    DateTime IssuedAt,
    DateTime ExpiresAt,
    DateTime TokenIssuedAt,
    DateTime TokenExpiresAt,
    Guid TokenId,
    string? TokenKeyId,
    string? TokenAlgorithm,
    DateTime? RevokedAt);

public record ApplicationRecordDto(
    Guid Id,
    Guid ConsentId,
    string AgentTenantId,
    string BoardTenantId,
    ApplicationStatus Status,
    DateTime SubmittedAt,
    string PayloadHash,
    string? SubmissionSignature,
    string? SubmissionKeyId,
    string? SubmissionAlgorithm,
    string? Receipt,
    string? ReceiptSignature,
    string? ReceiptHash);

public record DsrRequestDto(string CandidateEmail);
public record DsrDeleteRequestDto(string CandidateEmail, bool Confirm);

public record DsrExportResult(
    string CandidateEmail,
    DateTime? CandidateCreatedAt,
    IReadOnlyList<DsrConsentRecord> Consents,
    IReadOnlyList<DsrApplicationRecord> Applications,
    IReadOnlyList<DsrConsentRequestRecord> ConsentRequests,
    IReadOnlyList<DsrAuditEventRecord> AuditEvents);

public record DsrConsentRecord(
    Guid Id,
    string AgentTenantId,
    string BoardTenantId,
    string Scopes,
    ConsentStatus Status,
    DateTime IssuedAt,
    DateTime ExpiresAt,
    DateTime? RevokedAt);

public record DsrApplicationRecord(
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
    string? ReceiptSignature,
    string? ReceiptHash);

public record DsrConsentRequestRecord(
    Guid Id,
    string AgentTenantId,
    string BoardTenantId,
    string CandidateEmail,
    string Scopes,
    ConsentRequestStatus Status,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    DateTime? VerifiedAt,
    DateTime? DecisionAt);

public record DsrAuditEventRecord(
    Guid Id,
    string Category,
    string Action,
    string EntityType,
    string EntityId,
    DateTime CreatedAt,
    string? Jti,
    string? Metadata);

public record DsrDeleteResult(
    int ConsentsDeleted,
    int ApplicationsDeleted,
    int ConsentRequestsDeleted,
    bool CandidateDeleted);
