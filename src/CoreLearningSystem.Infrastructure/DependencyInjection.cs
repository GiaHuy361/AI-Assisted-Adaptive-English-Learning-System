using System;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using CoreLearningSystem.Application.Interfaces;
using CoreLearningSystem.Application.Options;
using CoreLearningSystem.Infrastructure.Persistence;
using CoreLearningSystem.Infrastructure.Persistence.Repositories;
using CoreLearningSystem.Infrastructure.Services;
using Hangfire;
using Hangfire.MySql;
using StackExchange.Redis;

namespace CoreLearningSystem.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        
        services.AddDbContext<AppDbContext>(options =>
            options.UseMySql(
                connectionString, 
                new MySqlServerVersion(new Version(8, 0, 30))
            ));

        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        
        // Register Real Kafka Producer as Singleton
        services.AddSingleton<Confluent.Kafka.IProducer<string, string>>(sp =>
        {
            var config = new Confluent.Kafka.ProducerConfig
            {
                BootstrapServers = configuration["Kafka:BootstrapServers"] ?? "localhost:9092",
                Acks = Confluent.Kafka.Acks.All,
                MessageTimeoutMs = 5000,
                EnableIdempotence = true
            };
            return new Confluent.Kafka.ProducerBuilder<string, string>(config).Build();
        });

        services.AddScoped<IKafkaPublisher, KafkaPublisher>();
        services.AddScoped<ISkillMatrixService, SkillMatrixService>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();

        // Register Recommendation Engine and Service
        services.Configure<CoreLearningSystem.Application.Options.RecommendationOptions>(configuration.GetSection(CoreLearningSystem.Application.Options.RecommendationOptions.Position));
        services.AddScoped<IAdaptiveRecommendationEngine, AdaptiveRecommendationEngine>();
        services.AddScoped<IRecommendationService, RecommendationService>();

        // Register Goal Tracking and Achievement Engine
        services.Configure<CoreLearningSystem.Application.Options.AchievementOptions>(configuration.GetSection(CoreLearningSystem.Application.Options.AchievementOptions.Position));
        services.AddScoped<IGoalTrackingService, GoalTrackingService>();
        services.AddScoped<IAchievementEngine, AchievementEngine>();
        services.AddScoped<IAchievementService, AchievementService>();

        // Phase 7 options
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.Position));
        services.Configure<JobScheduleOptions>(configuration.GetSection(JobScheduleOptions.Position));
        services.Configure<CleanupOptions>(configuration.GetSection(CleanupOptions.Position));

        // Phase 8 options
        services.Configure<RedisOptions>(configuration.GetSection(RedisOptions.Position));
        services.Configure<FeedbackAnalysisOptions>(configuration.GetSection(FeedbackAnalysisOptions.Position));
        services.Configure<FeedbackRecommendationOptions>(configuration.GetSection(FeedbackRecommendationOptions.Position));

        // Phase 8: Redis connection (Singleton — one multiplexer for the lifetime)
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var redisSection = configuration.GetSection(RedisOptions.Position);
            var connStr = redisSection["ConnectionString"] ?? "localhost:6379";
            var configOpts = ConfigurationOptions.Parse(connStr);
            configOpts.AbortOnConnectFail = false; // graceful degradation
            configOpts.ConnectRetry = 3;
            configOpts.ConnectTimeout = 3000;
            return ConnectionMultiplexer.Connect(configOpts);
        });

        // Phase 8: Cache services
        services.AddSingleton<ICacheKeyBuilder, CacheKeyBuilder>();
        services.AddSingleton<ICacheService, RedisCacheService>();

        // Phase 8: Distributed idempotency store (Redis-backed)
        services.AddSingleton<IProcessedEventStore, RedisProcessedEventStore>();

        // Phase 8: Feedback Analysis
        services.AddScoped<IFeedbackAnalysisService, FeedbackAnalysisService>();

        // Services
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IEmailSender, EmailSender>();
        services.AddScoped<BackgroundJobExecutor>();

        // Background Jobs
        services.AddScoped<LearningReminderJob>();
        services.AddScoped<WeeklyLearningReportJob>();
        services.AddScoped<GoalStatusTrackingJob>();
        services.AddScoped<AchievementCheckingJob>();
        services.AddScoped<SkillDecayJob>();
        services.AddScoped<CleanupJob>();

        // Hangfire client/storage configuration with Allow User Variables=True
        var hangfireConnStr = connectionString;
        if (hangfireConnStr != null)
        {
            if (!hangfireConnStr.Contains("Allow User Variables=True", StringComparison.OrdinalIgnoreCase) &&
                !hangfireConnStr.Contains("AllowUserVariables=True", StringComparison.OrdinalIgnoreCase))
            {
                hangfireConnStr = hangfireConnStr.TrimEnd(';') + ";Allow User Variables=True;";
            }
        }

        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseFilter(new AutomaticRetryAttribute { Attempts = 3 })
            .UseStorage(new MySqlStorage(
                hangfireConnStr,
                new MySqlStorageOptions
                {
                    TransactionIsolationLevel = System.Transactions.IsolationLevel.ReadCommitted,
                    QueuePollInterval = TimeSpan.FromSeconds(15),
                    PrepareSchemaIfNecessary = true,
                    TablesPrefix = "Hangfire"
                }
            )));

        return services;
    }
}
