using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;
using AdaptiveLearning.Contracts.Events;
using AdaptiveLearning.Contracts.Topics;
using AdaptiveLearning.Worker;
using AdaptiveLearning.Worker.Consumers;
using AdaptiveLearning.Worker.Options;
using AdaptiveLearning.Worker.Services;
using CoreLearningSystem.Application.Interfaces;

namespace AdaptiveLearning.Tests;

public class IntegrationTests
{
    private const string BootstrapServers = "localhost:9092";

    // A flag-controlled handler to test retry and DLQ behaviors dynamically
    public class TestQuizSubmittedEventHandler : IEventHandler<QuizSubmittedEvent>
    {
        public static int HandleCallCount { get; set; } = 0;
        public static bool ShouldThrowAlways { get; set; } = false;
        public static bool ShouldThrowTransientTwice { get; set; } = false;

        public Task HandleAsync(QuizSubmittedEvent ev)
        {
            HandleCallCount++;

            if (ShouldThrowAlways)
            {
                throw new InvalidOperationException("Simulated persistent failure.");
            }

            if (ShouldThrowTransientTwice && HandleCallCount <= 2)
            {
                throw new TimeoutException("Simulated transient failure.");
            }

            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Run_Kafka_E2E_Integration_Suite()
    {
        // Reset state
        TestQuizSubmittedEventHandler.HandleCallCount = 0;
        TestQuizSubmittedEventHandler.ShouldThrowAlways = false;
        TestQuizSubmittedEventHandler.ShouldThrowTransientTwice = false;

        // Verify Kafka is reachable before running tests (prevents test hang)
        if (!await IsKafkaAvailableAsync())
        {
            var requireInfra = Environment.GetEnvironmentVariable("REQUIRE_INFRASTRUCTURE_TESTS") == "true";
            if (requireInfra)
            {
                Assert.Fail("Kafka is required but not available on localhost:9092");
            }
            Console.WriteLine("Kafka broker is not available at localhost:9092. Skipping E2E Integration tests.");
            return;
        }

        // Setup test host representing our Worker service
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // Register configurations
                services.Configure<KafkaOptions>(opt =>
                {
                    opt.BootstrapServers = BootstrapServers;
                    opt.ConsumerGroupId = "test-group-" + Guid.NewGuid();
                    opt.AutoOffsetReset = "Latest";
                    opt.RetryCount = 3;
                    opt.RetryDelaySeconds = 1;
                    opt.DeadLetterTopic = TopicNames.DeadLetterTopic;
                });

                services.AddSingleton<IProcessedEventStore, InMemoryProcessedEventStore>();
                
                // Register test handler instead of the production one
                services.AddTransient<IEventHandler<QuizSubmittedEvent>, TestQuizSubmittedEventHandler>();
                
                // Keep other handlers as dummy implementations
                services.AddTransient<IEventHandler<LessonCompletedEvent>, DummyHandler<LessonCompletedEvent>>();
                services.AddTransient<IEventHandler<FeedbackSubmittedEvent>, DummyHandler<FeedbackSubmittedEvent>>();
                services.AddTransient<IEventHandler<PlacementTestCompletedEvent>, DummyHandler<PlacementTestCompletedEvent>>();

                // Register Producer for DLQ redirection
                services.AddSingleton<IProducer<string, string>>(sp =>
                {
                    var config = new ProducerConfig
                    {
                        BootstrapServers = BootstrapServers,
                        Acks = Acks.All
                    };
                    return new ProducerBuilder<string, string>(config).Build();
                });

                // Register the hosted consumer service
                services.AddHostedService<KafkaConsumerHostedService>();
            })
            .Build();

        // Start Hosted service in the background
        var cts = new CancellationTokenSource();
        await host.StartAsync(cts.Token);

        // Wait for consumer to join group and prepare partition assignments
        await Task.Delay(8000);

        // Helper Producer to push messages in tests
        var producerConfig = new ProducerConfig { BootstrapServers = BootstrapServers };
        using var testProducer = new ProducerBuilder<string, string>(producerConfig).Build();

        try
        {
            // --- SCENARIO 1: Valid QuizSubmittedEvent Flow ---
            var eventId = Guid.NewGuid();
            var correlationId = Guid.NewGuid();
            var quizEvent = new QuizSubmittedEvent
            {
                EventId = eventId,
                CorrelationId = correlationId,
                UserId = 1,
                QuizId = 10,
                QuizAttemptId = 200,
                Score = 90.0,
                TotalQuestions = 10,
                CorrectAnswers = 9,
                SubmittedAt = DateTimeOffset.UtcNow
            };

            await SendTestMessageAsync(testProducer, TopicNames.QuizSubmitted, eventId, correlationId, "QuizSubmittedEvent", quizEvent);

            // Wait for processing
            await Task.Delay(3000);
            Assert.Equal(1, TestQuizSubmittedEventHandler.HandleCallCount);

            // --- SCENARIO 2: Duplicate Event Detection ---
            // Send the exact same eventId again
            await SendTestMessageAsync(testProducer, TopicNames.QuizSubmitted, eventId, correlationId, "QuizSubmittedEvent", quizEvent);
            await Task.Delay(2000);
            
            // Call count should remain 1 (skipped processing)
            Assert.Equal(1, TestQuizSubmittedEventHandler.HandleCallCount);

            // --- SCENARIO 3: Retry & Recovery ---
            TestQuizSubmittedEventHandler.HandleCallCount = 0;
            TestQuizSubmittedEventHandler.ShouldThrowTransientTwice = true;

            var retryEventId = Guid.NewGuid();
            var retryEvent = quizEvent with { EventId = retryEventId };

            await SendTestMessageAsync(testProducer, TopicNames.QuizSubmitted, retryEventId, correlationId, "QuizSubmittedEvent", retryEvent);
            
            // Wait for retries (delay is 1s, 2s. Total wait ~5s)
            await Task.Delay(6000);
            
            // Should succeed on 3rd attempt (CallCount = 3: 2 throws + 1 success)
            Assert.Equal(3, TestQuizSubmittedEventHandler.HandleCallCount);

            // --- SCENARIO 4: Retry Exhausted -> DLQ ---
            TestQuizSubmittedEventHandler.HandleCallCount = 0;
            TestQuizSubmittedEventHandler.ShouldThrowAlways = true;
            TestQuizSubmittedEventHandler.ShouldThrowTransientTwice = false;

            var dlqEventId = Guid.NewGuid();
            var dlqEvent = quizEvent with { EventId = dlqEventId };

            await SendTestMessageAsync(testProducer, TopicNames.QuizSubmitted, dlqEventId, correlationId, "QuizSubmittedEvent", dlqEvent);
            
            // Wait for 3 retries (1s, 2s, 4s. Total wait ~9s)
            await Task.Delay(10000);

            // Call count should be 4 (1 original + 3 retries)
            Assert.Equal(4, TestQuizSubmittedEventHandler.HandleCallCount);

            // Verify message exists in DLQ topic
            var dlqMessage = await ReadDlqMessageAsync(dlqEventId);
            Assert.NotNull(dlqMessage);
            Assert.Contains("Max retries exceeded.", dlqMessage);
            Assert.Contains(dlqEventId.ToString(), dlqMessage);

            // --- SCENARIO 5: Invalid JSON -> DLQ ---
            var malformedJson = "{invalid-json-payload}";
            var malformedMessage = new Message<string, string> { Key = "malformed", Value = malformedJson };
            malformedMessage.Headers = new Headers
            {
                { "event-id", Guid.NewGuid().ToByteArray() },
                { "correlation-id", Guid.NewGuid().ToByteArray() },
                { "event-type", Encoding.UTF8.GetBytes("QuizSubmittedEvent") }
            };

            await testProducer.ProduceAsync(TopicNames.QuizSubmitted, malformedMessage);
            await Task.Delay(2000);

            var malformedDlq = await ReadDlqMessageAsync(Guid.Empty, "malformed");
            Assert.NotNull(malformedDlq);
            Assert.Contains("DeserializationFailure", malformedDlq);
        }
        finally
        {
            // Graceful shutdown Worker
            cts.Cancel();
            await host.StopAsync();
        }
    }

    private async Task<bool> IsKafkaAvailableAsync()
    {
        try
        {
            var config = new ProducerConfig { BootstrapServers = BootstrapServers, MessageTimeoutMs = 1500 };
            using var p = new ProducerBuilder<string, string>(config).Build();
            await p.ProduceAsync(TopicNames.QuizSubmitted, new Message<string, string> { Key = "probe", Value = "{}" });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task SendTestMessageAsync<T>(IProducer<string, string> producer, string topic, Guid eventId, Guid correlationId, string eventType, T payload)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var message = new Message<string, string> { Key = eventId.ToString(), Value = json };
        message.Headers = new Headers
        {
            { "event-id", eventId.ToByteArray() },
            { "correlation-id", correlationId.ToByteArray() },
            { "event-type", Encoding.UTF8.GetBytes(eventType) }
        };
        await producer.ProduceAsync(topic, message);
    }

    private async Task<string> ReadDlqMessageAsync(Guid targetEventId, string key = null)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = BootstrapServers,
            GroupId = "dlq-verifier-" + Guid.NewGuid(),
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(TopicNames.DeadLetterTopic);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(cts.Token);
                if (result == null) continue;

                if (key != null && result.Message.Key == key)
                {
                    return result.Message.Value;
                }

                if (targetEventId != Guid.Empty && result.Message.Value.Contains(targetEventId.ToString()))
                {
                    return result.Message.Value;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
        return null;
    }

    public class DummyHandler<T> : IEventHandler<T> where T : BaseEvent
    {
        public Task HandleAsync(T ev) => Task.CompletedTask;
    }
}
