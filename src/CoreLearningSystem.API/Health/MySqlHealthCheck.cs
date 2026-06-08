using Microsoft.Extensions.Diagnostics.HealthChecks;
using CoreLearningSystem.Infrastructure.Persistence;
using System.Threading;
using System.Threading.Tasks;
using System;

namespace CoreLearningSystem.API.Health;

public class MySqlHealthCheck : IHealthCheck
{
    private readonly AppDbContext _context;

    public MySqlHealthCheck(AppDbContext context)
    {
        _context = context;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (await _context.Database.CanConnectAsync(cancellationToken))
            {
                return HealthCheckResult.Healthy("MySQL database connection is active.");
            }
            return HealthCheckResult.Unhealthy("Cannot connect to MySQL database.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("MySQL health check failed.", ex);
        }
    }
}
