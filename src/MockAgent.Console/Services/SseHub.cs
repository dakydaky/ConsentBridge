using System.Collections.Concurrent;
using System.Threading.Channels;

namespace MockAgent.ConsoleApp.Services;

public class SseHub
{
    private readonly ConcurrentDictionary<Guid, Channel<string>> _clients = new();

    public async Task SubscribeAsync(HttpResponse response, CancellationToken ct)
    {
        response.Headers.Add("Content-Type", "text/event-stream");
        response.Headers.Add("Cache-Control", "no-cache");
        response.Headers.Add("Connection", "keep-alive");

        var id = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<string>();
        _clients[id] = channel;
        try
        {
            await response.Body.FlushAsync(ct);
            await foreach (var msg in channel.Reader.ReadAllAsync(ct))
            {
                var data = $"data: {msg}\n\n";
                var bytes = System.Text.Encoding.UTF8.GetBytes(data);
                await response.Body.WriteAsync(bytes, ct);
                await response.Body.FlushAsync(ct);
            }
        }
        catch { }
        finally
        {
            _clients.TryRemove(id, out _);
        }
    }

    public void Broadcast(string message)
    {
        foreach (var kv in _clients)
        {
            kv.Value.Writer.TryWrite(message);
        }
    }
}

