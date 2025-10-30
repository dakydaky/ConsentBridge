using System.Collections.Concurrent;

namespace Candidate.PortalApp.Services;

public static class ProfileStore
{
    private static readonly ConcurrentDictionary<string, CandidateProfile> Store = new(StringComparer.OrdinalIgnoreCase);

    public static void Save(string email, CandidateProfile profile) => Store[email] = profile;
    public static bool TryGet(string email, out CandidateProfile? profile) => Store.TryGetValue(email, out profile);
}

public class CandidateProfile
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Phone { get; set; }
    public string? CvUrl { get; set; }
    public string? CvSha256 { get; set; }
    public string? CoverLetter { get; set; }
}

