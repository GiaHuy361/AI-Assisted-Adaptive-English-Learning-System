using System;
using System.Threading;
using System.Threading.Tasks;

namespace CoreLearningSystem.Application.Interfaces;

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class;
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default) where T : class;
    Task RemoveAsync(string key, CancellationToken ct = default);
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);

    // Tracked-set based invalidation (no KEYS * scan)
    Task AddKeyToSetAsync(string setKey, string memberKey, CancellationToken ct = default);
    Task RemoveKeysBySetAsync(string setKey, CancellationToken ct = default);

    // Version-based list invalidation
    Task<long> IncrementVersionAsync(string versionKey, CancellationToken ct = default);
    Task<long> GetVersionAsync(string versionKey, CancellationToken ct = default);
}
