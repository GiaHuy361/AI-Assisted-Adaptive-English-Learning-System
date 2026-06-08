using AdaptiveLearning.GrpcService.Services;
using CoreLearningSystem.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddGrpc();
builder.Services.AddScoped<IQuizWeaknessAnalyzer, QuizWeaknessAnalyzer>();
builder.Services.AddRecommendationReadServicesForGrpc(builder.Configuration);

var app = builder.Build();

// Configure the HTTP request pipeline.
app.MapGrpcService<RecommendationGrpcService>();

// Root: click localhost:50080 trên Docker Desktop → thấy info + link health
app.MapGet("/", () => Results.Json(new
{
    service  = "AdaptiveLearning.GrpcService",
    status   = "ONLINE",
    version  = "1.0.0",
    endpoints = new
    {
        health           = "http://localhost:50080/health",
        grpc_cleartext   = "grpc://localhost:50051"
    }
}));
app.MapGet("/health", () => Results.Json(new { status = "Healthy", service = "AdaptiveLearning.GrpcService" }));

app.Run();

public partial class Program { }

