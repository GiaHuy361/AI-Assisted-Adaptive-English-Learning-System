using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace AdaptiveLearning.Worker.Services;

public class InMemoryProcessedEventStore : IProcessedEventStore
{
    private readonly ConcurrentDictionary<Guid, DateTime> _store = new();
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(24);

    public Task<bool> HasBeenProcessedAsync(Guid eventId)
    {
        if (_store.TryGetValue(eventId, out var expiry))
        {
            if (expiry > DateTime.UtcNow)
            {
                return Task.FromResult(true);
            }
            // Clean up expired entry
            _store.TryRemove(eventId, out _);
        }
        return Task.FromResult(false);
    }

    public Task MarkAsProcessedAsync(Guid eventId, TimeSpan? ttl = null)
    {
        var expiry = DateTime.UtcNow.Add(ttl ?? DefaultTtl);
        _store[eventId] = expiry;
        
        // Basic cleanup of all expired keys to avoid memory leaks
        CleanupExpired();

        return Task.CompletedTask;
    }

    private void CleanupExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in _store)
        {
            if (kvp.Value <= now)
            {
                _store.TryRemove(kvp.Key, out _);
            }
        }
    }
}
