using System;
using System.Threading.Tasks;

namespace CoreLearningSystem.Application.Interfaces;

/// <summary>
/// Distributed event idempotency store with owner-token state machine.
/// States: Processing (short TTL) -> Completed (long TTL)
/// Fallback: when Redis unavailable, logs warning and allows acquisition (handlers rely on DB constraints).
/// </summary>
public interface IProcessedEventStore
{
    // ── Legacy ────────────────────────────────────────────────────────────
    Task<bool> HasBeenProcessedAsync(Guid eventId);
    Task MarkAsProcessedAsync(Guid eventId, TimeSpan? ttl = null);

    // ── Owner-token state machine ─────────────────────────────────────────
    /// <summary>Try to acquire a processing lock. Returns true if lock acquired.</summary>
    Task<bool> TryAcquireProcessingLockAsync(Guid eventId, string ownerId, TimeSpan lockTtl);

    /// <summary>Mark event as Completed (only if owner matches). Returns true on success.</summary>
    Task<bool> MarkAsCompletedAsync(Guid eventId, string ownerId, TimeSpan completedTtl);

    /// <summary>Release the processing lock (only if owner matches).</summary>
    Task ReleaseProcessingLockAsync(Guid eventId, string ownerId);

    /// <summary>Check if the event is already in Completed state.</summary>
    Task<bool> IsCompletedAsync(Guid eventId);
}
