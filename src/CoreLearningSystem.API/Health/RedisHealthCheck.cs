using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;
using System.Threading;
using System.Threading.Tasks;
using System;

namespace CoreLearningSystem.API.Health;

public class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _redis;

    public RedisHealthCheck(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_redis.IsConnected)
            {
                return Task.FromResult(HealthCheckResult.Healthy("Redis connection is active."));
            }
            return Task.FromResult(HealthCheckResult.Degraded("Redis is disconnected (degraded state)."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Degraded("Redis health check failed.", ex));
        }
    }
}
