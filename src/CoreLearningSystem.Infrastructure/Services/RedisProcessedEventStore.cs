using System;
using System.Text;
using System.Threading.Tasks;
using CoreLearningSystem.Application.Interfaces;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace CoreLearningSystem.Infrastructure.Services;

/// <summary>
/// Redis-backed implementation of IProcessedEventStore.
/// Uses atomic SET NX EX for lock acquisition.
/// Falls back gracefully when Redis is unavailable.
/// </summary>
public sealed class RedisProcessedEventStore : IProcessedEventStore
{
    private readonly IConnectionMultiplexer _mux;
    private readonly ICacheKeyBuilder _keys;
    private readonly ILogger<RedisProcessedEventStore> _logger;

    private static readonly TimeSpan DefaultCompletedTtl = TimeSpan.FromHours(48);
    private static readonly TimeSpan DefaultProcessingTtl = TimeSpan.FromMinutes(5);

    public RedisProcessedEventStore(
        IConnectionMultiplexer mux,
        ICacheKeyBuilder keys,
        ILogger<RedisProcessedEventStore> logger)
    {
        _mux = mux;
        _keys = keys;
        _logger = logger;
    }

    private IDatabase Db() => _mux.GetDatabase();

    // ── Legacy ──────────────────────────────────────────────────────────────
    public async Task<bool> HasBeenProcessedAsync(Guid eventId)
    {
        try
        {
            var key = _keys.ProcessedEventCompleted(eventId.ToString());
            return await Db().KeyExistsAsync(key).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis HasBeenProcessed check failed for event={EventId}; treating as not processed", eventId);
            return false;
        }
    }

    public async Task MarkAsProcessedAsync(Guid eventId, TimeSpan? ttl = null)
    {
        try
        {
            var key = _keys.ProcessedEventCompleted(eventId.ToString());
            await Db().StringSetAsync(key, "1", ttl ?? DefaultCompletedTtl).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis MarkAsProcessed failed for event={EventId}", eventId);
        }
    }

    // ── Owner-token state machine ────────────────────────────────────────────
    public async Task<bool> TryAcquireProcessingLockAsync(Guid eventId, string ownerId, TimeSpan lockTtl)
    {
        try
        {
            // If already completed — do not allow re-processing
            var completedKey = _keys.ProcessedEventCompleted(eventId.ToString());
            if (await Db().KeyExistsAsync(completedKey).ConfigureAwait(false))
                return false;

            var processingKey = _keys.ProcessedEventProcessing(eventId.ToString());
            // Atomic SET NX EX — only succeeds if key doesn't exist
            var acquired = await Db().StringSetAsync(
                processingKey, ownerId, lockTtl, When.NotExists).ConfigureAwait(false);

            if (!acquired)
                _logger.LogDebug("Processing lock already held for event={EventId}", eventId);

            return acquired;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis TryAcquireProcessingLock failed for event={EventId}; allowing acquisition (DB constraints will protect)", eventId);
            return true; // fail-open: handlers must rely on DB unique constraints
        }
    }

    public async Task<bool> MarkAsCompletedAsync(Guid eventId, string ownerId, TimeSpan completedTtl)
    {
        try
        {
            var processingKey = _keys.ProcessedEventProcessing(eventId.ToString());
            var current = await Db().StringGetAsync(processingKey).ConfigureAwait(false);
            if (current.IsNullOrEmpty || (string)current! != ownerId)
            {
                _logger.LogWarning("MarkAsCompleted ownership mismatch for event={EventId}; current owner={Owner}", eventId, (string?)current);
                return false;
            }

            var completedKey = _keys.ProcessedEventCompleted(eventId.ToString());
            var batch = Db().CreateBatch();
            var setTask = batch.StringSetAsync(completedKey, "1", completedTtl);
            var delTask = batch.KeyDeleteAsync(processingKey);
            batch.Execute();
            await Task.WhenAll(setTask, delTask).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis MarkAsCompleted failed for event={EventId}", eventId);
            return false;
        }
    }

    public async Task ReleaseProcessingLockAsync(Guid eventId, string ownerId)
    {
        try
        {
            var processingKey = _keys.ProcessedEventProcessing(eventId.ToString());
            var current = await Db().StringGetAsync(processingKey).ConfigureAwait(false);
            if (!current.IsNullOrEmpty && (string)current! == ownerId)
                await Db().KeyDeleteAsync(processingKey).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis ReleaseProcessingLock failed for event={EventId}", eventId);
        }
    }

    public async Task<bool> IsCompletedAsync(Guid eventId)
    {
        try
        {
            var completedKey = _keys.ProcessedEventCompleted(eventId.ToString());
            return await Db().KeyExistsAsync(completedKey).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis IsCompleted check failed for event={EventId}; returning false", eventId);
            return false;
        }
    }
}
