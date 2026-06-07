using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CoreLearningSystem.Application.Interfaces;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace CoreLearningSystem.Infrastructure.Services;

public sealed class RedisCacheService : ICacheService
{
    private readonly IConnectionMultiplexer _mux;
    private readonly ILogger<RedisCacheService> _logger;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public RedisCacheService(IConnectionMultiplexer mux, ILogger<RedisCacheService> logger)
    {
        _mux = mux;
        _logger = logger;
    }

    private IDatabase Db() => _mux.GetDatabase();

    // ── Get ──────────────────────────────────────────────────────────────────
    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
    {
        try
        {
            var raw = await Db().StringGetAsync(key).ConfigureAwait(false);
            if (raw.IsNullOrEmpty) return null;
            return JsonSerializer.Deserialize<T>(raw!, _json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis GET failed for key={Key}; returning null (cache miss)", key);
            return null;
        }
    }

    // ── Set ──────────────────────────────────────────────────────────────────
    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default) where T : class
    {
        try
        {
            var payload = JsonSerializer.Serialize(value, _json);
            await Db().StringSetAsync(key, payload, ttl).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis SET failed for key={Key}; skipping cache write", key);
        }
    }

    // ── Remove ───────────────────────────────────────────────────────────────
    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await Db().KeyDeleteAsync(key).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis DEL failed for key={Key}", key);
        }
    }

    // ── Exists ───────────────────────────────────────────────────────────────
    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        try
        {
            return await Db().KeyExistsAsync(key).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis EXISTS failed for key={Key}; returning false", key);
            return false;
        }
    }

    // ── Tracked-Set Invalidation ─────────────────────────────────────────────
    public async Task AddKeyToSetAsync(string setKey, string memberKey, CancellationToken ct = default)
    {
        try
        {
            await Db().SetAddAsync(setKey, memberKey).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis SADD failed for setKey={SetKey}", setKey);
        }
    }

    public async Task RemoveKeysBySetAsync(string setKey, CancellationToken ct = default)
    {
        try
        {
            var db = Db();
            var members = await db.SetMembersAsync(setKey).ConfigureAwait(false);
            if (members.Length == 0) return;

            var batch = db.CreateBatch();
            var tasks = new System.Collections.Generic.List<Task>(members.Length + 1);
            foreach (var m in members)
                tasks.Add(batch.KeyDeleteAsync((string)m!));
            tasks.Add(batch.KeyDeleteAsync(setKey));
            batch.Execute();
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis SMEMBERS/DEL failed for setKey={SetKey}", setKey);
        }
    }

    // ── Version-Based List Invalidation ──────────────────────────────────────
    public async Task<long> IncrementVersionAsync(string versionKey, CancellationToken ct = default)
    {
        try
        {
            return await Db().StringIncrementAsync(versionKey).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis INCR failed for versionKey={Key}; returning 0", versionKey);
            return 0;
        }
    }

    public async Task<long> GetVersionAsync(string versionKey, CancellationToken ct = default)
    {
        try
        {
            var raw = await Db().StringGetAsync(versionKey).ConfigureAwait(false);
            return raw.IsNullOrEmpty ? 0 : (long)raw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis GET version failed for versionKey={Key}; returning 0", versionKey);
            return 0;
        }
    }
}
