using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace MockBoard.Adapter.Services;

public sealed record ApplicationPayloadDetails(
    string? ConsentToken,
    CandidateDetails Candidate,
    JobDetails Job,
    MaterialsDetails Materials,
    MetaDetails Meta)
{
    public static ApplicationPayloadDetails? TryParse(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var candidate = CandidateDetails.From(payload);
        var job = JobDetails.From(payload);
        var materials = MaterialsDetails.From(payload);
        var meta = MetaDetails.From(payload);

        if (candidate == CandidateDetails.Empty &&
            job == JobDetails.Empty &&
            materials == MaterialsDetails.Empty &&
            meta == MetaDetails.Empty)
        {
            return null;
        }

        var consentToken = payload.GetStringOrNull("consentToken");
        return new ApplicationPayloadDetails(consentToken, candidate, job, materials, meta);
    }
}

public sealed record CandidateDetails(
    string? Id,
    string? Email,
    string? Phone,
    string? FirstName,
    string? LastName,
    string? CvUrl,
    string? CvSha256)
{
    public static CandidateDetails Empty { get; } = new(null, null, null, null, null, null, null);

    public static CandidateDetails From(JsonElement payload)
    {
        if (!payload.TryGetObject("candidate", out var candidate))
        {
            return Empty;
        }

        var id = candidate.GetStringOrNull("id");

        string? email = null;
        string? phone = null;
        if (candidate.TryGetObject("contact", out var contact))
        {
            email = contact.GetStringOrNull("email");
            phone = contact.GetStringOrNull("phone");
        }

        string? firstName = null;
        string? lastName = null;
        if (candidate.TryGetObject("pii", out var pii))
        {
            firstName = pii.GetStringOrNull("firstName");
            lastName = pii.GetStringOrNull("lastName");
        }

        string? cvUrl = null;
        string? cvSha256 = null;
        if (candidate.TryGetObject("cv", out var cv))
        {
            cvUrl = cv.GetStringOrNull("url");
            cvSha256 = cv.GetStringOrNull("sha256");
        }

        return new CandidateDetails(id, email, phone, firstName, lastName, cvUrl, cvSha256);
    }
}

public sealed record JobDetails(string? ExternalId, string? Title, string? Company, string? ApplyEndpoint)
{
    public static JobDetails Empty { get; } = new(null, null, null, null);

    public static JobDetails From(JsonElement payload)
    {
        if (!payload.TryGetObject("job", out var job))
        {
            return Empty;
        }

        var externalId = job.GetStringOrNull("externalId");
        var title = job.GetStringOrNull("title");
        var company = job.GetStringOrNull("company");
        var applyEndpoint = job.GetStringOrNull("applyEndpoint");

        return new JobDetails(externalId, title, company, applyEndpoint);
    }
}

public sealed record MaterialsDetails(string? CoverLetterText, IReadOnlyList<AnswerDetails> Answers)
{
    public static MaterialsDetails Empty { get; } = new(null, Array.Empty<AnswerDetails>());

    public static MaterialsDetails From(JsonElement payload)
    {
        if (!payload.TryGetObject("materials", out var materials))
        {
            return Empty;
        }

        string? coverLetterText = null;
        if (materials.TryGetObject("coverLetter", out var coverLetter))
        {
            coverLetterText = coverLetter.GetStringOrNull("text");
        }

        var answers = new List<AnswerDetails>();
        if (materials.TryGetArray("answers", out var answersElement))
        {
            foreach (var answerElement in answersElement.EnumerateArray())
            {
                var questionId = answerElement.GetStringOrNull("questionId");
                var answerText = answerElement.GetStringOrNull("answerText");

                if (questionId is null && answerText is null)
                {
                    continue;
                }

                answers.Add(new AnswerDetails(questionId, answerText));
            }
        }

        return new MaterialsDetails(coverLetterText, answers);
    }
}

public sealed record AnswerDetails(string? QuestionId, string? AnswerText);

public sealed record MetaDetails(string? Locale, DateTime? Timestamp, string? UserAgent)
{
    public static MetaDetails Empty { get; } = new(null, null, null);

    public static MetaDetails From(JsonElement payload)
    {
        if (!payload.TryGetObject("meta", out var meta))
        {
            return Empty;
        }

        var locale = meta.GetStringOrNull("locale");
        var userAgent = meta.GetStringOrNull("userAgent");
        var tsRaw = meta.GetStringOrNull("ts");

        DateTime? timestamp = null;
        if (!string.IsNullOrWhiteSpace(tsRaw) &&
            DateTime.TryParse(tsRaw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
        {
            timestamp = parsed;
        }

        return new MetaDetails(locale, timestamp, userAgent);
    }
}

internal static class JsonElementExtensions
{
    public static string? GetStringOrNull(this JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    }

    public static bool TryGetObject(this JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.Object)
        {
            value = property;
            return true;
        }

        value = default;
        return false;
    }

    public static bool TryGetArray(this JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.Array)
        {
            value = property;
            return true;
        }

        value = default;
        return false;
    }
}
