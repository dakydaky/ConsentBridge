using System.Collections.Concurrent;

namespace MockBoard.Adapter.Services;

public class ApplicationFeed
{
    private readonly ConcurrentQueue<ApplicationEntry> _queue = new();

    public void Add(ApplicationEntry entry)
    {
        _queue.Enqueue(entry);
        while (_queue.Count > 20 && _queue.TryDequeue(out _))
        {
        }
    }

    public IReadOnlyList<ApplicationEntry> ListApplications() => _queue.Reverse().ToList();
}

public record ApplicationEntry(string Id, string CandidateEmail, string JobTitle, string Status, DateTime ReceivedAt);
