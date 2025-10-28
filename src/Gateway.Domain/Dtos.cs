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
