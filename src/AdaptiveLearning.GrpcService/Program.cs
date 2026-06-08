using AdaptiveLearning.GrpcService.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddGrpc();
builder.Services.AddScoped<IQuizWeaknessAnalyzer, QuizWeaknessAnalyzer>();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.MapGrpcService<RecommendationGrpcService>();

// Expose a basic health-friendly root response
app.MapGet("/", () => new { Status = "ONLINE", Service = "AdaptiveLearning.GrpcService", Version = "1.0.0-skeleton" });
app.MapGet("/health", () => new { Status = "Healthy" });

app.Run();

public partial class Program { }

