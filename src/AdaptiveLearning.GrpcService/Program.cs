using AdaptiveLearning.GrpcService.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddGrpc();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.MapGrpcService<RecommendationGrpcService>();

// Expose a basic health-friendly root response
app.MapGet("/", () => new { Status = "ONLINE", Service = "AdaptiveLearning.GrpcService", Version = "1.0.0-skeleton" });

app.Run();
