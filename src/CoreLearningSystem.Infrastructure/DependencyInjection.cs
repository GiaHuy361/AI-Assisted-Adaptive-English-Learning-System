using System;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using CoreLearningSystem.Application.Interfaces;
using CoreLearningSystem.Infrastructure.Persistence;
using CoreLearningSystem.Infrastructure.Persistence.Repositories;
using CoreLearningSystem.Infrastructure.Services;

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

        return services;
    }
}
