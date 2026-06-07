using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AdaptiveLearning.Worker;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("AdaptiveLearning.Worker is starting up...");
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AdaptiveLearning.Worker background tasks execution started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            // Placeholder loop
            _logger.LogDebug("AdaptiveLearning.Worker is running in the background.");
            await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("AdaptiveLearning.Worker is shutting down...");
        return base.StopAsync(cancellationToken);
    }
}
