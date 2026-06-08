using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using CoreLearningSystem.Application.Interfaces;

namespace AdaptiveLearning.Worker.Services;

/// <summary>
/// In-memory implementation of IProcessedEventStore for testing.
/// NOT safe for multi-instance deployments — use RedisProcessedEventStore in production.
/// </summary>
public class InMemoryProcessedEventStore : IProcessedEventStore
{
    // key -> (state, ownerId, expiry)
    private readonly ConcurrentDictionary<Guid, (string State, string OwnerId, DateTime Expiry)> _store = new();
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan ProcessingTtl = TimeSpan.FromMinutes(5);

    // ── Legacy ──────────────────────────────────────────────────────────────

    public Task<bool> HasBeenProcessedAsync(Guid eventId)
    {
        if (_store.TryGetValue(eventId, out var entry) && entry.Expiry > DateTime.UtcNow)
            return Task.FromResult(entry.State == "Completed");
        return Task.FromResult(false);
    }

    public Task MarkAsProcessedAsync(Guid eventId, TimeSpan? ttl = null)
    {
        var expiry = DateTime.UtcNow.Add(ttl ?? DefaultTtl);
        _store[eventId] = ("Completed", string.Empty, expiry);
        CleanupExpired();
        return Task.CompletedTask;
    }

    // ── Owner-token state machine ───────────────────────────────────────────

    public Task<bool> TryAcquireProcessingLockAsync(Guid eventId, string ownerId, TimeSpan lockTtl)
    {
        // Already completed?
        if (_store.TryGetValue(eventId, out var existing) && existing.Expiry > DateTime.UtcNow)
        {
            if (existing.State == "Completed") return Task.FromResult(false);
            if (existing.State == "Processing") return Task.FromResult(false); // another owner
        }

        var expiry = DateTime.UtcNow.Add(lockTtl);
        var added = _store.TryAdd(eventId, ("Processing", ownerId, expiry));
        if (!added)
        {
            // Try to replace only if expired
            if (_store.TryGetValue(eventId, out var old) && old.Expiry <= DateTime.UtcNow)
            {
                _store[eventId] = ("Processing", ownerId, expiry);
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }
        return Task.FromResult(true);
    }

    public Task<bool> MarkAsCompletedAsync(Guid eventId, string ownerId, TimeSpan completedTtl)
    {
        if (_store.TryGetValue(eventId, out var entry) && entry.OwnerId == ownerId && entry.State == "Processing")
        {
            _store[eventId] = ("Completed", string.Empty, DateTime.UtcNow.Add(completedTtl));
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public Task ReleaseProcessingLockAsync(Guid eventId, string ownerId)
    {
        if (_store.TryGetValue(eventId, out var entry) && entry.OwnerId == ownerId && entry.State == "Processing")
            _store.TryRemove(eventId, out _);
        return Task.CompletedTask;
    }

    public Task<bool> IsCompletedAsync(Guid eventId)
    {
        if (_store.TryGetValue(eventId, out var entry) && entry.Expiry > DateTime.UtcNow)
            return Task.FromResult(entry.State == "Completed");
        return Task.FromResult(false);
    }

    private void CleanupExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in _store)
            if (kvp.Value.Expiry <= now) _store.TryRemove(kvp.Key, out _);
    }
}
