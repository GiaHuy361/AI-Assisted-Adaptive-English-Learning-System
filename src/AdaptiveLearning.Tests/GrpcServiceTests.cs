using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using AdaptiveLearning.Contracts.Events;
using AdaptiveLearning.GrpcService;
using AdaptiveLearning.GrpcService.Services;
using AdaptiveLearning.Worker.Options;
using AdaptiveLearning.Worker.Services;
using CoreLearningSystem.Application.Interfaces;

namespace AdaptiveLearning.Tests;

public class GrpcServiceTests
{
    private readonly ILoggerFactory _loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

    // Mock service to test retries and custom server behavior
    public class MockRecommendationService : RecommendationService.RecommendationServiceBase
    {
        public int CallCount { get; set; } = 0;
        public int ThrowCount { get; set; } = 0;
        public StatusCode ThrowStatusCode { get; set; } = StatusCode.Unavailable;

        public override Task<AnalyzeQuizSubmissionResponse> AnalyzeQuizSubmission(AnalyzeQuizSubmissionRequest request, ServerCallContext context)
        {
            CallCount++;
            if (CallCount <= ThrowCount)
            {
                throw new RpcException(new Status(ThrowStatusCode, $"Simulated transient error {CallCount}"));
            }

            return Task.FromResult(new AnalyzeQuizSubmissionResponse
            {
                Success = true,
                AnalysisId = Guid.NewGuid().ToString(),
                UserId = request.UserId,
                WeakestSkill = "Reading",
                Reason = "Simulated success response",
                ProcessedAt = DateTime.UtcNow.ToString("o"),
                Message = "Simulated Success"
            });
        }

        public override Task<StatusResponse> GetServiceStatus(StatusRequest request, ServerCallContext context)
        {
            return Task.FromResult(new StatusResponse
            {
                ServiceName = "MockRecommendationService",
                Status = "HEALTHY",
                Version = "1.0.0"
            });
        }
    }

    [Fact]
    public async Task GetServiceStatus_Should_ReturnHealthyStatus()
    {
        // Start real server on random port
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.Services.AddGrpc();
        builder.Services.AddSingleton<IQuizWeaknessAnalyzer, QuizWeaknessAnalyzer>();
        builder.Services.AddSingleton<IRecommendationService>(new Moq.Mock<IRecommendationService>().Object);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, 0, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            });
        });
        var app = builder.Build();
        app.MapGrpcService<RecommendationGrpcService>();
        await app.StartAsync();

        try
        {
            var serverUrl = app.Urls.First();
            using var channel = GrpcChannel.ForAddress(serverUrl);
            var client = new RecommendationService.RecommendationServiceClient(channel);

            // Act
            var status = await client.GetServiceStatusAsync(new StatusRequest());

            // Assert
            Assert.NotNull(status);
            Assert.Equal("AdaptiveLearning.GrpcService", status.ServiceName);
            Assert.Equal("HEALTHY", status.Status);
            Assert.NotEmpty(status.ServerTime);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task AnalyzeQuizSubmission_Should_PerformWeaknessAnalysisSuccessfully()
    {
        // Start real server
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.Services.AddGrpc();
        builder.Services.AddSingleton<IQuizWeaknessAnalyzer, QuizWeaknessAnalyzer>();
        builder.Services.AddSingleton<IRecommendationService>(new Moq.Mock<IRecommendationService>().Object);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, 0, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            });
        });
        var app = builder.Build();
        app.MapGrpcService<RecommendationGrpcService>();
        await app.StartAsync();

        try
        {
            var serverUrl = app.Urls.First();
            using var channel = GrpcChannel.ForAddress(serverUrl);
            var client = new RecommendationService.RecommendationServiceClient(channel);

            // Create valid request
            var request = new AnalyzeQuizSubmissionRequest
            {
                EventId = Guid.NewGuid().ToString(),
                CorrelationId = Guid.NewGuid().ToString(),
                UserId = 1,
                QuizId = 10,
                QuizAttemptId = 100,
                Score = 50.0,
                TotalQuestions = 2,
                CorrectAnswers = 1,
                SubmittedAt = DateTimeOffset.UtcNow.ToString("o")
            };
            request.Answers.Add(new AnswerDetail { QuestionId = 1, Skill = "Listening", Topic = "P1", Level = "A1", IsCorrect = true });
            request.Answers.Add(new AnswerDetail { QuestionId = 2, Skill = "Listening", Topic = "P2", Level = "A1", IsCorrect = false });

            // Act
            var response = await client.AnalyzeQuizSubmissionAsync(request);

            // Assert
            Assert.NotNull(response);
            Assert.True(response.Success);
            Assert.Equal("Listening", response.WeakestSkill);
            Assert.Contains("P2", response.WeakTopics);
            Assert.Single(response.SkillScores);
            Assert.Equal(50.0, response.SkillScores[0].Score);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("", "98877292-69ab-4ad6-b31c-d762955f190e", 1, 10, 100, 2, 1, "EventId must be a valid, non-empty GUID string.")]
    [InlineData("invalid-guid", "98877292-69ab-4ad6-b31c-d762955f190e", 1, 10, 100, 2, 1, "EventId must be a valid, non-empty GUID string.")]
    [InlineData("98877292-69ab-4ad6-b31c-d762955f190d", "", 1, 10, 100, 2, 1, "CorrelationId must be a valid, non-empty GUID string.")]
    [InlineData("98877292-69ab-4ad6-b31c-d762955f190d", "invalid-guid", 1, 10, 100, 2, 1, "CorrelationId must be a valid, non-empty GUID string.")]
    [InlineData("98877292-69ab-4ad6-b31c-d762955f190d", "98877292-69ab-4ad6-b31c-d762955f190e", 0, 10, 100, 2, 1, "UserId must be positive.")]
    [InlineData("98877292-69ab-4ad6-b31c-d762955f190d", "98877292-69ab-4ad6-b31c-d762955f190e", 1, 0, 100, 2, 1, "QuizId must be positive.")]
    [InlineData("98877292-69ab-4ad6-b31c-d762955f190d", "98877292-69ab-4ad6-b31c-d762955f190e", 1, 10, 0, 2, 1, "QuizAttemptId must be positive.")]
    [InlineData("98877292-69ab-4ad6-b31c-d762955f190d", "98877292-69ab-4ad6-b31c-d762955f190e", 1, 10, 100, 0, 0, "TotalQuestions must be positive.")]
    [InlineData("98877292-69ab-4ad6-b31c-d762955f190d", "98877292-69ab-4ad6-b31c-d762955f190e", 1, 10, 100, 2, -1, "CorrectAnswers cannot be negative.")]
    [InlineData("98877292-69ab-4ad6-b31c-d762955f190d", "98877292-69ab-4ad6-b31c-d762955f190e", 1, 10, 100, 2, 3, "CorrectAnswers cannot exceed TotalQuestions.")]
    public async Task AnalyzeQuizSubmission_Should_ThrowInvalidArgumentRpcException_WhenBasicValidationFails(
        string eventId, string correlationId, int userId, int quizId, int quizAttemptId, int totalQuestions, int correctAnswers, string expectedErrorMessage)
    {
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.Services.AddGrpc();
        builder.Services.AddSingleton<IQuizWeaknessAnalyzer, QuizWeaknessAnalyzer>();
        builder.Services.AddSingleton<IRecommendationService>(new Moq.Mock<IRecommendationService>().Object);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, 0, listenOptions => { listenOptions.Protocols = HttpProtocols.Http2; });
        });
        var app = builder.Build();
        app.MapGrpcService<RecommendationGrpcService>();
        await app.StartAsync();

        try
        {
            var serverUrl = app.Urls.First();
            using var channel = GrpcChannel.ForAddress(serverUrl);
            var client = new RecommendationService.RecommendationServiceClient(channel);

            var request = new AnalyzeQuizSubmissionRequest
            {
                EventId = eventId,
                CorrelationId = correlationId,
                UserId = userId,
                QuizId = quizId,
                QuizAttemptId = quizAttemptId,
                Score = 50.0,
                TotalQuestions = totalQuestions,
                CorrectAnswers = correctAnswers,
                SubmittedAt = DateTimeOffset.UtcNow.ToString("o")
            };
            // Populate answers to pass some checks, unless totalQuestions is 0
            for (int i = 0; i < totalQuestions; i++)
            {
                request.Answers.Add(new AnswerDetail { QuestionId = i + 1, Skill = "Reading", IsCorrect = i < correctAnswers });
            }

            var ex = await Assert.ThrowsAsync<RpcException>(() => client.AnalyzeQuizSubmissionAsync(request).ResponseAsync);
            Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
            Assert.Contains(expectedErrorMessage, ex.Status.Detail);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task RecommendationGrpcClient_Should_RetryOnTransientError_AndRecover()
    {
        var mockService = new MockRecommendationService
        {
            ThrowCount = 2, // Fail twice
            ThrowStatusCode = StatusCode.Unavailable
        };

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.Services.AddGrpc();
        builder.Services.AddSingleton(mockService);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, 0, listenOptions => { listenOptions.Protocols = HttpProtocols.Http2; });
        });
        var app = builder.Build();
        app.MapGrpcService<MockRecommendationService>();
        await app.StartAsync();

        try
        {
            var serverUrl = app.Urls.First();
            using var channel = GrpcChannel.ForAddress(serverUrl);
            var rawClient = new RecommendationService.RecommendationServiceClient(channel);

            var options = Options.Create(new RecommendationGrpcOptions
            {
                ServiceUrl = serverUrl,
                MaxRetryAttempts = 3,
                RetryDelayMilliseconds = 100,
                RequestTimeoutSeconds = 5
            });

            var clientLogger = _loggerFactory.CreateLogger<RecommendationGrpcClient>();
            var grpcClient = new RecommendationGrpcClient(rawClient, options, clientLogger);

            var quizEvent = new QuizSubmittedEvent
            {
                EventId = Guid.NewGuid(),
                CorrelationId = Guid.NewGuid(),
                UserId = 1,
                QuizId = 10,
                QuizAttemptId = 100,
                Score = 50.0,
                TotalQuestions = 1,
                CorrectAnswers = 0,
                SubmittedAt = DateTimeOffset.UtcNow,
                AnswerDetails = new List<QuizAnswerDetail>
                {
                    new() { QuestionId = 1, SkillName = "Reading", Topic = "T1", Level = "A1", IsCorrect = false }
                }
            };

            // Act
            var result = await grpcClient.AnalyzeQuizSubmissionAsync(quizEvent, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Success);
            Assert.Equal("Reading", result.WeakestSkill);
            Assert.Equal(3, mockService.CallCount); // 2 throws + 1 success = 3 calls
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task RecommendationGrpcClient_Should_ThrowRpcException_WhenRetriesExhausted()
    {
        var mockService = new MockRecommendationService
        {
            ThrowCount = 5, // Throw 5 times, but max retries is 2
            ThrowStatusCode = StatusCode.Unavailable
        };

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.Services.AddGrpc();
        builder.Services.AddSingleton(mockService);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, 0, listenOptions => { listenOptions.Protocols = HttpProtocols.Http2; });
        });
        var app = builder.Build();
        app.MapGrpcService<MockRecommendationService>();
        await app.StartAsync();

        try
        {
            var serverUrl = app.Urls.First();
            using var channel = GrpcChannel.ForAddress(serverUrl);
            var rawClient = new RecommendationService.RecommendationServiceClient(channel);

            var options = Options.Create(new RecommendationGrpcOptions
            {
                ServiceUrl = serverUrl,
                MaxRetryAttempts = 2, // Max attempts = 1 + 2 = 3
                RetryDelayMilliseconds = 50,
                RequestTimeoutSeconds = 5
            });

            var clientLogger = _loggerFactory.CreateLogger<RecommendationGrpcClient>();
            var grpcClient = new RecommendationGrpcClient(rawClient, options, clientLogger);

            var quizEvent = new QuizSubmittedEvent
            {
                EventId = Guid.NewGuid(),
                CorrelationId = Guid.NewGuid(),
                UserId = 1,
                QuizId = 10,
                QuizAttemptId = 100,
                Score = 50.0,
                TotalQuestions = 1,
                CorrectAnswers = 0,
                SubmittedAt = DateTimeOffset.UtcNow,
                AnswerDetails = new List<QuizAnswerDetail>
                {
                    new() { QuestionId = 1, SkillName = "Reading", Topic = "T1", Level = "A1", IsCorrect = false }
                }
            };

            // Act & Assert
            var ex = await Assert.ThrowsAsync<RpcException>(() => grpcClient.AnalyzeQuizSubmissionAsync(quizEvent, CancellationToken.None));
            Assert.Equal(StatusCode.Unavailable, ex.StatusCode);
            Assert.Equal(3, mockService.CallCount); // 3 attempts made, all failed
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task RecommendationGrpcClient_Should_ThrowRpcExceptionWithoutRetrying_WhenNonTransientErrorEncountered()
    {
        var mockService = new MockRecommendationService
        {
            ThrowCount = 2,
            ThrowStatusCode = StatusCode.InvalidArgument // Non-transient
        };

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.Services.AddGrpc();
        builder.Services.AddSingleton(mockService);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, 0, listenOptions => { listenOptions.Protocols = HttpProtocols.Http2; });
        });
        var app = builder.Build();
        app.MapGrpcService<MockRecommendationService>();
        await app.StartAsync();

        try
        {
            var serverUrl = app.Urls.First();
            using var channel = GrpcChannel.ForAddress(serverUrl);
            var rawClient = new RecommendationService.RecommendationServiceClient(channel);

            var options = Options.Create(new RecommendationGrpcOptions
            {
                ServiceUrl = serverUrl,
                MaxRetryAttempts = 3,
                RetryDelayMilliseconds = 50,
                RequestTimeoutSeconds = 5
            });

            var clientLogger = _loggerFactory.CreateLogger<RecommendationGrpcClient>();
            var grpcClient = new RecommendationGrpcClient(rawClient, options, clientLogger);

            var quizEvent = new QuizSubmittedEvent
            {
                EventId = Guid.NewGuid(),
                CorrelationId = Guid.NewGuid(),
                UserId = 1,
                QuizId = 10,
                QuizAttemptId = 100,
                Score = 50.0,
                TotalQuestions = 1,
                CorrectAnswers = 0,
                SubmittedAt = DateTimeOffset.UtcNow,
                AnswerDetails = new List<QuizAnswerDetail>
                {
                    new() { QuestionId = 1, SkillName = "Reading", Topic = "T1", Level = "A1", IsCorrect = false }
                }
            };

            // Act & Assert
            var ex = await Assert.ThrowsAsync<RpcException>(() => grpcClient.AnalyzeQuizSubmissionAsync(quizEvent, CancellationToken.None));
            Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
            Assert.Equal(1, mockService.CallCount); // Fails immediately, no retries
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
