using System.Text.Json;
using System.Text.Json.Serialization;

namespace MockBoard.Adapter.Services;

public sealed record ApplicationSubmission(
    [property: JsonPropertyName("application_id")] string ApplicationId,
    [property: JsonPropertyName("job_external_id")] string JobExternalId,
    [property: JsonPropertyName("candidate_id")] string CandidateId,
    [property: JsonPropertyName("payload")] JsonElement Payload,
    [property: JsonPropertyName("signature")] string Signature);
