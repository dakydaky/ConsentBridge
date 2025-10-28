using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gateway.Domain;

public static class ReceiptJson
{
    public static JsonSerializerOptions SerializerOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    public static byte[] SerializePayload(BoardReceiptPayload payload) =>
        JsonSerializer.SerializeToUtf8Bytes(payload, SerializerOptions);
}

public record BoardReceiptEnvelope(
    [property: JsonPropertyName("receipt")] BoardReceiptPayload Receipt,
    [property: JsonPropertyName("receipt_signature")] string ReceiptSignature);

public record BoardReceiptPayload(
    [property: JsonPropertyName("spec")] string Spec,
    [property: JsonPropertyName("application_id")] string ApplicationId,
    [property: JsonPropertyName("board_id")] string BoardId,
    [property: JsonPropertyName("job_external_id")] string JobExternalId,
    [property: JsonPropertyName("candidate_id")] string CandidateId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("received_at")] DateTime ReceivedAt,
    [property: JsonPropertyName("board_ref")] string BoardRef);
