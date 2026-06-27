using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Confluent.Kafka;
using StackExchange.Redis;
using AdaptiveLearning.Worker.Options;
using CoreLearningSystem.Application.Options;
using Microsoft.Extensions.Configuration;

namespace AdaptiveLearning.Worker.Services;

public class WorkerHealthService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly IConnectionMultiplexer _redis;
    private readonly RecommendationGrpcOptions _grpcOptions;
    private readonly ILogger<WorkerHealthService> _logger;
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(3) };

    private const string HealthFilePath = "/tmp/adaptive-worker-health.txt";

    public WorkerHealthService(
        IConfiguration configuration,
        IConnectionMultiplexer redis,
        IOptions<RecommendationGrpcOptions> grpcOptions,
        ILogger<WorkerHealthService> logger)
    {
        _configuration = configuration;
        _redis = redis;
        _grpcOptions = grpcOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WorkerHealthService background monitoring started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await UpdateHealthFileAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while updating worker health file.");
            }

            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    private async Task UpdateHealthFileAsync(CancellationToken cancellationToken)
    {
        // 1. Check Kafka
        string kafkaStatus = "Unhealthy";
        try
        {
            var bootstrapServers = _configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
            using var adminClient = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrapServers }).Build();
            var meta = adminClient.GetMetadata(TimeSpan.FromSeconds(2));
            if (meta != null && meta.Brokers.Any())
            {
                kafkaStatus = "Healthy";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Worker health check: Kafka is unreachable.");
        }

        // 2. Check Redis
        string redisStatus = "Unhealthy";
        try
        {
            if (_redis.IsConnected)
            {
                redisStatus = "Healthy";
            }
            else
            {
                redisStatus = "Degraded";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Worker health check: Redis is unreachable.");
            redisStatus = "Degraded"; // according to the degraded policy
        }

        // 3. Check gRPC
        string grpcStatus = "Unhealthy";
        try
        {
            // Map grpc-service URL from options to its HTTP health endpoint (port 50580)
            var serviceUrl = _grpcOptions.ServiceUrl ?? "http://grpc-service:50551";
            var healthUrl = serviceUrl;
            if (serviceUrl.Contains("grpc-service:50551"))
            {
                healthUrl = "http://grpc-service:50580/health";
            }
            else if (serviceUrl.Contains("localhost:50551"))
            {
                healthUrl = "http://localhost:50580/health";
            }

            var response = await _httpClient.GetAsync(healthUrl, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                grpcStatus = "Healthy";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Worker health check: gRPC service health endpoint is unreachable.");
        }

        // 4. Check Hangfire / MySQL
        string hangfireStatus = "Unhealthy";
        try
        {
            using (var conn = Hangfire.JobStorage.Current.GetConnection())
            {
                hangfireStatus = "Healthy";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Worker health check: Hangfire/MySQL storage check failed.");
        }

        // 5. Determine overall health status
        // Redis being degraded does not make the overall health unhealthy,
        // but Kafka, gRPC, and Hangfire (MySQL) are required for core worker duties.
        string overallHealth = (kafkaStatus == "Healthy" && grpcStatus == "Healthy" && hangfireStatus == "Healthy")
            ? "Healthy"
            : "Unhealthy";

        var timestamp = DateTime.UtcNow.ToString("o");
        var healthContent = $"timestamp={timestamp}\n" +
                            $"overall={overallHealth}\n" +
                            $"kafka={kafkaStatus}\n" +
                            $"redis={redisStatus}\n" +
                            $"grpc={grpcStatus}\n" +
                            $"hangfire={hangfireStatus}\n";

        try
        {
            var dir = Path.GetDirectoryName(HealthFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await File.WriteAllTextAsync(HealthFilePath, healthContent, cancellationToken);
            _logger.LogDebug("Worker health file updated: overall={Overall}", overallHealth);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write health file to path {Path}", HealthFilePath);
        }
    }
}
