using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Confluent.Kafka;
using AdaptiveLearning.Worker.Options;
using AdaptiveLearning.Worker.Services;
using AdaptiveLearning.Worker.Handlers;
using AdaptiveLearning.Worker.Consumers;
using AdaptiveLearning.Contracts.Events;
using CoreLearningSystem.Infrastructure;
using CoreLearningSystem.Infrastructure.Services;
using Hangfire;
using CoreLearningSystem.Application.Options;
using CoreLearningSystem.Application.Interfaces;

namespace AdaptiveLearning.Worker;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // Register strongly-typed options
        builder.Services.Configure<KafkaOptions>(builder.Configuration.GetSection(KafkaOptions.Position));
        builder.Services.Configure<RedisOptions>(builder.Configuration.GetSection(RedisOptions.Position));
        builder.Services.Configure<RecommendationGrpcOptions>(builder.Configuration.GetSection(RecommendationGrpcOptions.Position));
        builder.Services.Configure<BackgroundJobOptions>(builder.Configuration.GetSection(BackgroundJobOptions.Position));
        builder.Services.AddInfrastructureServices(builder.Configuration);

        // NOTE: IProcessedEventStore (RedisProcessedEventStore) is registered by AddInfrastructureServices above.

        // Register Handlers
        builder.Services.AddTransient<IEventHandler<QuizSubmittedEvent>, QuizSubmittedEventHandler>();
        builder.Services.AddTransient<IEventHandler<LessonCompletedEvent>, LessonCompletedEventHandler>();
        builder.Services.AddTransient<IEventHandler<FeedbackSubmittedEvent>, FeedbackSubmittedEventHandler>();
        builder.Services.AddTransient<IEventHandler<PlacementTestCompletedEvent>, PlacementTestCompletedEventHandler>();
        builder.Services.AddTransient<IEventHandler<GoalCompletedEvent>, GoalCompletedEventHandler>();
        builder.Services.AddTransient<IEventHandler<BadgeAwardedEvent>, BadgeAwardedEventHandler>();
        builder.Services.AddTransient<IEventHandler<NotificationCreatedEvent>, NotificationCreatedEventHandler>();

        // Register Hangfire Server
        builder.Services.AddHangfireServer();

        // Register gRPC Client and its Wrapper
        builder.Services.AddGrpcClient<AdaptiveLearning.GrpcService.RecommendationService.RecommendationServiceClient>((sp, o) =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RecommendationGrpcOptions>>().Value;
            o.Address = new Uri(options.ServiceUrl);
        });
        builder.Services.AddScoped<IRecommendationGrpcClient, RecommendationGrpcClient>();

        // Register Kafka Producer (Singleton) for DLQ publish
        builder.Services.AddSingleton<IProducer<string, string>>(sp =>
        {
            var bootstrapServers = builder.Configuration.GetValue<string>("Kafka:BootstrapServers") ?? "localhost:9092";
            var config = new ProducerConfig
            {
                BootstrapServers = bootstrapServers,
                Acks = Acks.All,
                MessageTimeoutMs = 5000,
                EnableIdempotence = true
            };
            return new ProducerBuilder<string, string>(config).Build();
        });

        // Register Consumer Background Service
        builder.Services.AddHostedService<KafkaConsumerHostedService>();

        var host = builder.Build();

        // Register Hangfire Recurring Jobs on startup
        using (var scope = host.Services.CreateScope())
        {
            var options = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<JobScheduleOptions>>().Value;
            var jobManager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
            RecurringJobScheduler.ScheduleJobs(jobManager, options);
        }

        host.Run();
    }
}
