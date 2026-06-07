using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using AdaptiveLearning.Worker.Options;

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

        // Register worker hosted service
        builder.Services.AddHostedService<Worker>();

        var host = builder.Build();
        host.Run();
    }
}
