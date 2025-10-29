namespace MockAgent.ConsoleApp;

public class DemoState
{
    private readonly List<ConsentRequestItem> _consents = new();
    private readonly List<ApplicationItem> _applications = new();

    public IReadOnlyList<ConsentRequestItem> Consents => _consents.OrderByDescending(c => c.CreatedAt).ToList();
    public IReadOnlyList<ApplicationItem> Applications => _applications.OrderByDescending(a => a.CreatedAt).ToList();

    public void AddConsent(ConsentRequestItem item) => _consents.Add(item);
    public void AddApplication(ApplicationItem item) => _applications.Add(item);
}

public record ConsentRequestItem(string CandidateEmail, string RequestId, DateTimeOffset CreatedAt);
public record ApplicationItem(string CandidateEmail, string JobRef, string ResponseSnippet, DateTimeOffset CreatedAt);

