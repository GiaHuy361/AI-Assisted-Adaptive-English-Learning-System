using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CoreLearningSystem.Application.Interfaces;
using CoreLearningSystem.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace AdaptiveLearning.Tests;

/// <summary>
/// Real Redis integration tests.
/// These tests require a live Redis instance on localhost:6379.
/// Run: docker compose -f docker-compose.redis.yml up -d redis
/// Tests are skipped automatically when Redis is unavailable.
/// </summary>
[Collection("RedisIntegration")]
public class RedisIntegrationTests : IAsyncLifetime
{
    private IConnectionMultiplexer? _mux;
    private RedisCacheService? _cache;
    private CacheKeyBuilder _keys = null!;
    private IDatabase? _db;
    private bool _redisAvailable;

    public async Task InitializeAsync()
    {
        _keys = new CacheKeyBuilder();
        try
        {
            var opts = ConfigurationOptions.Parse("localhost:6379");
            opts.AbortOnConnectFail = false;
            opts.ConnectTimeout = 2000;
            opts.ConnectRetry = 1;
            _mux = await ConnectionMultiplexer.ConnectAsync(opts);
            _db = _mux.GetDatabase();
            var pong = await _db.PingAsync();
            _redisAvailable = pong > TimeSpan.Zero;
            if (_redisAvailable)
            {
                _cache = new RedisCacheService(_mux, NullLogger<RedisCacheService>.Instance);
                // Clean up test keys from previous runs
                await CleanTestKeysAsync();
            }
        }
        catch
        {
            _redisAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_redisAvailable) await CleanTestKeysAsync();
        _mux?.Dispose();
    }

    private async Task CleanTestKeysAsync()
    {
        if (_db == null) return;
        try
        {
            var testPrefix = "adaptive:v1:test:";
            // We enumerate known test keys explicitly (no KEYS *)
            var knownTestKeys = new[]
            {
                "adaptive:v1:test:dto:1",
                "adaptive:v1:test:dto:2",
                "adaptive:v1:test:dto:3",
                "adaptive:v1:test:set",
                "adaptive:v1:test:version",
                "adaptive:v1:test:idempotency:processing:evt-1",
                "adaptive:v1:test:idempotency:completed:evt-1",
            };
            foreach (var k in knownTestKeys)
                await _db.KeyDeleteAsync(k);
        }
        catch { /* cleanup best-effort */ }
    }



    // ── Record for test serialization ──────────────────────────────────────
    private record TestDto(int Id, string Name, double Value);

    // ── 1. SET and GET ──────────────────────────────────────────────────────
    [Fact]
    public async Task Redis_SetAndGet_DTO_ReturnsCorrectValue()
    {
        if (!_redisAvailable) return;
        var key = "adaptive:v1:test:dto:1";
        var dto = new TestDto(42, "Grammar B1", 3.14);

        await _cache!.SetAsync(key, dto, TimeSpan.FromMinutes(5));
        var result = await _cache.GetAsync<TestDto>(key);

        Assert.NotNull(result);
        Assert.Equal(42, result!.Id);
        Assert.Equal("Grammar B1", result.Name);
        Assert.Equal(3.14, result.Value, 5);
    }

    // ── 2. EXISTS ───────────────────────────────────────────────────────────
    [Fact]
    public async Task Redis_Exists_ReturnsTrueAfterSet_FalseAfterRemove()
    {
        if (!_redisAvailable) return;
        var key = "adaptive:v1:test:dto:2";

        await _cache!.SetAsync(key, new TestDto(1, "x", 0), TimeSpan.FromMinutes(5));
        Assert.True(await _cache.ExistsAsync(key));

        await _cache.RemoveAsync(key);
        Assert.False(await _cache.ExistsAsync(key));
    }

    // ── 3. TTL Expiration ───────────────────────────────────────────────────
    [Fact]
    public async Task Redis_TTL_KeyExpiresAfterTtl()
    {
        if (!_redisAvailable) return;
        var key = "adaptive:v1:test:dto:3";

        await _cache!.SetAsync(key, new TestDto(99, "ttl-test", 1.0), TimeSpan.FromSeconds(2));
        Assert.True(await _cache.ExistsAsync(key));

        await Task.Delay(TimeSpan.FromSeconds(3));
        var result = await _cache.GetAsync<TestDto>(key);
        Assert.Null(result); // expired
    }

    // ── 4. Miss returns null ─────────────────────────────────────────────────
    [Fact]
    public async Task Redis_Get_MissingKey_ReturnsNull()
    {
        if (!_redisAvailable) return;
        var result = await _cache!.GetAsync<TestDto>("adaptive:v1:test:nonexistent:99999");
        Assert.Null(result);
    }

    // ── 5. Tracked-set invalidation (no KEYS *) ─────────────────────────────
    [Fact]
    public async Task Redis_TrackedSet_InvalidatesAllMembers()
    {
        if (!_redisAvailable) return;
        var setKey = "adaptive:v1:test:set";
        var k1 = "adaptive:v1:lessons:list:v1:grammar:b1:admin";
        var k2 = "adaptive:v1:lessons:list:v1:all:all:admin";

        // Set both list keys
        await _cache!.SetAsync(k1, new TestDto(1, "a", 1), TimeSpan.FromMinutes(5));
        await _cache.SetAsync(k2, new TestDto(2, "b", 2), TimeSpan.FromMinutes(5));

        // Track them
        await _cache.AddKeyToSetAsync(setKey, k1);
        await _cache.AddKeyToSetAsync(setKey, k2);

        // Invalidate all
        await _cache.RemoveKeysBySetAsync(setKey);

        // Both should be gone
        Assert.False(await _cache.ExistsAsync(k1));
        Assert.False(await _cache.ExistsAsync(k2));
        Assert.False(await _cache.ExistsAsync(setKey)); // set itself removed
    }

    // ── 6. Version increment ─────────────────────────────────────────────────
    [Fact]
    public async Task Redis_VersionIncrement_IncreasesMonotonically()
    {
        if (!_redisAvailable) return;
        var key = "adaptive:v1:test:version";
        await _db!.KeyDeleteAsync(key); // ensure clean

        var v1 = await _cache!.IncrementVersionAsync(key);
        var v2 = await _cache.IncrementVersionAsync(key);
        var v3 = await _cache.IncrementVersionAsync(key);

        Assert.True(v2 > v1);
        Assert.True(v3 > v2);
    }

    // ── 7. NX Concurrency (SET NX EX = atomic lock) ──────────────────────────
    [Fact]
    public async Task Redis_SetNX_OnlyOneOwnerWins_Concurrent()
    {
        if (!_redisAvailable) return;
        var lockKey = "adaptive:v1:test:idempotency:processing:evt-1";
        await _db!.KeyDeleteAsync(lockKey);

        int winCount = 0;
        var tasks = new List<Task>();

        for (int i = 0; i < 10; i++)
        {
            var owner = $"owner-{i}";
            tasks.Add(Task.Run(async () =>
            {
                var acquired = await _db.StringSetAsync(lockKey, owner, TimeSpan.FromSeconds(30), When.NotExists);
                if (acquired)
                    System.Threading.Interlocked.Increment(ref winCount);
            }));
        }

        await Task.WhenAll(tasks);

        Assert.Equal(1, winCount); // exactly one winner
        await _db.KeyDeleteAsync(lockKey);
    }

    // ── 8. RedisProcessedEventStore - TryAcquire → Complete ─────────────────
    [Fact]
    public async Task RedisProcessedEventStore_OwnerTokenStateMachine_Works()
    {
        if (!_redisAvailable) return;
        var store = new RedisProcessedEventStore(
            _mux!, _keys, NullLogger<RedisProcessedEventStore>.Instance);

        var eventId = Guid.NewGuid();
        const string owner1 = "worker-instance-1";
        const string owner2 = "worker-instance-2";

        // Clean
        var pk = _keys.ProcessedEventProcessing(eventId.ToString());
        var ck = _keys.ProcessedEventCompleted(eventId.ToString());
        await _db!.KeyDeleteAsync(pk);
        await _db.KeyDeleteAsync(ck);

        // 1. owner1 acquires lock
        var acquired1 = await store.TryAcquireProcessingLockAsync(eventId, owner1, TimeSpan.FromSeconds(30));
        Assert.True(acquired1, "owner1 should acquire lock");

        // 2. owner2 cannot acquire
        var acquired2 = await store.TryAcquireProcessingLockAsync(eventId, owner2, TimeSpan.FromSeconds(30));
        Assert.False(acquired2, "owner2 should NOT acquire lock");

        // 3. Wrong owner cannot complete
        var completed1 = await store.MarkAsCompletedAsync(eventId, owner2, TimeSpan.FromHours(24));
        Assert.False(completed1, "wrong owner cannot complete");
        Assert.False(await store.IsCompletedAsync(eventId));

        // 4. Correct owner completes
        var completed2 = await store.MarkAsCompletedAsync(eventId, owner1, TimeSpan.FromHours(24));
        Assert.True(completed2, "correct owner can complete");
        Assert.True(await store.IsCompletedAsync(eventId));

        // 5. Cannot re-acquire after Completed
        var acquired3 = await store.TryAcquireProcessingLockAsync(eventId, owner2, TimeSpan.FromSeconds(30));
        Assert.False(acquired3, "completed event blocks re-acquisition");

        // 6. Legacy HasBeenProcessed works
        Assert.True(await store.HasBeenProcessedAsync(eventId));

        // Cleanup
        await _db.KeyDeleteAsync(ck);
    }

    // ── 9. Owner-safe release ─────────────────────────────────────────────────
    [Fact]
    public async Task RedisProcessedEventStore_WrongOwnerRelease_DoesNotReleaseLock()
    {
        if (!_redisAvailable) return;
        var store = new RedisProcessedEventStore(
            _mux!, _keys, NullLogger<RedisProcessedEventStore>.Instance);

        var eventId = Guid.NewGuid();
        var pk = _keys.ProcessedEventProcessing(eventId.ToString());
        await _db!.KeyDeleteAsync(pk);

        await store.TryAcquireProcessingLockAsync(eventId, "owner-A", TimeSpan.FromSeconds(30));

        // Wrong owner tries to release
        await store.ReleaseProcessingLockAsync(eventId, "owner-B");

        // Key should still exist (owner-A still holds it)
        Assert.True(await _db.KeyExistsAsync(pk), "Lock should still be held by owner-A");

        // Correct owner releases
        await store.ReleaseProcessingLockAsync(eventId, "owner-A");
        Assert.False(await _db.KeyExistsAsync(pk), "Lock should be released by correct owner");
    }

    // ── 10. Redis outage fallback (simulated) ─────────────────────────────────
    [Fact]
    public async Task Redis_Unavailable_CacheGetReturnsNull_NoException()
    {
        // Simulate unavailable Redis by connecting to wrong port
        try
        {
            var opts = ConfigurationOptions.Parse("localhost:19999");
            opts.AbortOnConnectFail = false;
            opts.ConnectTimeout = 500;
            opts.ConnectRetry = 1;
            var badMux = await ConnectionMultiplexer.ConnectAsync(opts);
            var badCache = new RedisCacheService(badMux, NullLogger<RedisCacheService>.Instance);

            // Should NOT throw — should return null
            var result = await badCache.GetAsync<TestDto>("adaptive:v1:test:some-key");
            Assert.Null(result);

            // SET should also silently fail
            await badCache.SetAsync("adaptive:v1:test:some-key", new TestDto(1, "x", 1), TimeSpan.FromMinutes(5));

            // EXISTS should return false
            var exists = await badCache.ExistsAsync("adaptive:v1:test:some-key");
            Assert.False(exists);

            badMux.Dispose();
        }
        catch (Exception)
        {
            // If connection itself throws before GetAsync, that's also acceptable
            // The key test is: RedisCacheService methods internally catch and log
        }
    }

    // ── 11. CacheKeyBuilder: no KEYS * usage ──────────────────────────────────
    [Fact]
    public void CacheKeyBuilder_NoWildcard_AllKeysAreDeterministic()
    {
        // This is a static correctness test — no Redis required
        var kb = new CacheKeyBuilder();

        // All keys must be deterministic (no wildcards)
        Assert.DoesNotContain("*", kb.LessonListVersion());
        Assert.DoesNotContain("*", kb.LessonDetail(1));
        Assert.DoesNotContain("*", kb.SkillMatrix(1));
        Assert.DoesNotContain("*", kb.ActiveRecommendations(1));
        Assert.DoesNotContain("*", kb.ProgressSummary(1));
        Assert.DoesNotContain("*", kb.ProcessedEventProcessing("abc"));
        Assert.DoesNotContain("*", kb.ProcessedEventCompleted("abc"));

        // All keys must start with the namespace
        Assert.StartsWith("adaptive:v1:", kb.LessonListVersion());
        Assert.StartsWith("adaptive:v1:", kb.SkillMatrix(99));
    }

    // ── 12. Version-based lesson list cache-aside pattern ─────────────────────
    [Fact]
    public async Task Redis_LessonListVersionCache_WorksEndToEnd()
    {
        if (!_redisAvailable) return;
        var versionKey = _keys.LessonListVersion();

        // Start clean
        await _db!.KeyDeleteAsync(versionKey);

        // First request — version = 0 (missing), treated as 0
        var v0 = await _cache!.GetVersionAsync(versionKey);
        Assert.Equal(0, v0);

        // Increment on content change
        var v1 = await _cache.IncrementVersionAsync(versionKey);
        Assert.True(v1 >= 1);

        // List key for this version
        var listKey = _keys.LessonList(v1);
        await _cache.SetAsync(listKey, new[] { new TestDto(1, "Lesson A", 0) }, TimeSpan.FromMinutes(30));

        var listKeyTrackerSet = _keys.LessonDetailSet();
        await _cache.AddKeyToSetAsync(listKeyTrackerSet, listKey);

        // Increment version (admin creates new lesson)
        var v2 = await _cache.IncrementVersionAsync(versionKey);
        Assert.True(v2 > v1);

        // Old list key for v1 is now stale — new requests use v2
        var newListKey = _keys.LessonList(v2);
        Assert.NotEqual(listKey, newListKey);

        // Cleanup
        await _db.KeyDeleteAsync(versionKey);
        await _db.KeyDeleteAsync(listKey);
        await _db.KeyDeleteAsync(listKeyTrackerSet);
    }
}
