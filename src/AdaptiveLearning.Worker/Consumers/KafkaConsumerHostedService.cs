using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AdaptiveLearning.Contracts.Events;
using AdaptiveLearning.Contracts.Topics;
using AdaptiveLearning.Worker.Options;
using AdaptiveLearning.Worker.Services;

namespace AdaptiveLearning.Worker.Consumers;

public class KafkaConsumerHostedService : BackgroundService
{
    private readonly KafkaOptions _kafkaOptions;
    private readonly IServiceProvider _serviceProvider;
    private readonly IProcessedEventStore _eventStore;
    private readonly ILogger<KafkaConsumerHostedService> _logger;
    private readonly IProducer<string, string> _producer;

    public KafkaConsumerHostedService(
        IOptions<KafkaOptions> kafkaOptions,
        IServiceProvider serviceProvider,
        IProcessedEventStore eventStore,
        ILogger<KafkaConsumerHostedService> logger,
        IProducer<string, string> producer)
    {
        _kafkaOptions = kafkaOptions.Value;
        _serviceProvider = serviceProvider;
        _eventStore = eventStore;
        _logger = logger;
        _producer = producer;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("KafkaConsumerHostedService is starting up...");
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        _logger.LogInformation("KafkaConsumerHostedService started. Connection to BootstrapServers: {BootstrapServers}...", _kafkaOptions.BootstrapServers);

        var config = new ConsumerConfig
        {
            BootstrapServers = _kafkaOptions.BootstrapServers,
            GroupId = _kafkaOptions.ConsumerGroupId,
            AutoOffsetReset = (AutoOffsetReset)Enum.Parse(typeof(AutoOffsetReset), _kafkaOptions.AutoOffsetReset, true),
            EnableAutoCommit = false // Manual commit
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();

        // Subscribe to topics
        consumer.Subscribe(new[]
        {
            TopicNames.QuizSubmitted,
            TopicNames.LessonCompleted,
            TopicNames.FeedbackSubmitted,
            TopicNames.PlacementTestCompleted,
            TopicNames.GoalCompleted,
            TopicNames.BadgeAwarded
        });

        _logger.LogInformation("Subscribed to topics: {QuizSubmitted}, {LessonCompleted}, {FeedbackSubmitted}, {PlacementTestCompleted}, {GoalCompleted}, {BadgeAwarded}",
            TopicNames.QuizSubmitted, TopicNames.LessonCompleted, TopicNames.FeedbackSubmitted, TopicNames.PlacementTestCompleted, TopicNames.GoalCompleted, TopicNames.BadgeAwarded);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var consumeResult = consumer.Consume(stoppingToken);
                if (consumeResult == null || consumeResult.IsPartitionEOF) continue;

                _logger.LogInformation("Received message from Topic: {Topic}, Partition: {Partition}, Offset: {Offset}, Key: {Key}",
                    consumeResult.Topic, consumeResult.Partition.Value, consumeResult.Offset.Value, consumeResult.Message.Key);

                await ProcessMessageWithRetryAsync(consumer, consumeResult, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Consume loop cancelled via CancellationToken.");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in consumer loop.");
                await Task.Delay(1000, stoppingToken);
            }
        }

        _logger.LogInformation("Closing Kafka consumer...");
        consumer.Close();
    }

    private async Task ProcessMessageWithRetryAsync(IConsumer<string, string> consumer, ConsumeResult<string, string> consumeResult, CancellationToken stoppingToken)
    {
        Guid eventId = Guid.Empty;
        Guid correlationId = Guid.Empty;
        string eventType = string.Empty;
        int currentAttempt = 0;

        try
        {
            // Parse headers if available
            if (consumeResult.Message.Headers != null)
            {
                if (consumeResult.Message.Headers.TryGetLastBytes("correlation-id", out var corrBytes))
                {
                    correlationId = new Guid(corrBytes);
                }
                if (consumeResult.Message.Headers.TryGetLastBytes("event-id", out var evBytes))
                {
                    eventId = new Guid(evBytes);
                }
                if (consumeResult.Message.Headers.TryGetLastBytes("event-type", out var typeBytes))
                {
                    eventType = Encoding.UTF8.GetString(typeBytes);
                }
            }

            // Attempt to deserialize base event to extract EventId/CorrelationId if headers failed
            using var doc = JsonDocument.Parse(consumeResult.Message.Value);
            var root = doc.RootElement;
            if (eventId == Guid.Empty && root.TryGetProperty("eventId", out var idProp))
            {
                Guid.TryParse(idProp.GetString(), out eventId);
            }
            if (correlationId == Guid.Empty && root.TryGetProperty("correlationId", out var corrProp))
            {
                Guid.TryParse(corrProp.GetString(), out correlationId);
            }
            if (string.IsNullOrEmpty(eventType) && root.TryGetProperty("eventType", out var typeProp))
            {
                eventType = typeProp.GetString() ?? string.Empty;
            }

            if (eventId == Guid.Empty)
            {
                throw new JsonException("Message payload lacks a valid EventId.");
            }

            // Idempotency Check
            if (await _eventStore.HasBeenProcessedAsync(eventId))
            {
                _logger.LogWarning("Duplicate event detected. Skipping processing. EventId: {EventId}, Topic: {Topic}", eventId, consumeResult.Topic);
                consumer.Commit(consumeResult);
                return;
            }

            // Route to proper handler with retry mechanism
            bool success = false;
            while (currentAttempt <= _kafkaOptions.RetryCount && !success)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    await DispatchToHandlerAsync(scope, consumeResult.Topic, consumeResult.Message.Value);
                    
                    success = true;
                    await _eventStore.MarkAsProcessedAsync(eventId);
                    consumer.Commit(consumeResult);
                    
                    _logger.LogInformation("Successfully processed and committed message. EventId: {EventId}, Topic: {Topic}", eventId, consumeResult.Topic);
                }
                catch (Exception ex)
                {
                    bool isTransient = true;
                    if (ex is Grpc.Core.RpcException rpcEx && 
                        (rpcEx.StatusCode == Grpc.Core.StatusCode.InvalidArgument ||
                         rpcEx.StatusCode == Grpc.Core.StatusCode.Unauthenticated ||
                         rpcEx.StatusCode == Grpc.Core.StatusCode.PermissionDenied ||
                         rpcEx.StatusCode == Grpc.Core.StatusCode.NotFound))
                    {
                        isTransient = false;
                    }
                    else if (ex.InnerException is Grpc.Core.RpcException innerRpcEx && 
                             (innerRpcEx.StatusCode == Grpc.Core.StatusCode.InvalidArgument ||
                              innerRpcEx.StatusCode == Grpc.Core.StatusCode.Unauthenticated ||
                              innerRpcEx.StatusCode == Grpc.Core.StatusCode.PermissionDenied ||
                              innerRpcEx.StatusCode == Grpc.Core.StatusCode.NotFound))
                    {
                        isTransient = false;
                    }

                    if (!isTransient)
                    {
                        _logger.LogError(ex, "Non-transient gRPC error encountered. Skipping retries. EventId: {EventId}", eventId);
                        break;
                    }

                    currentAttempt++;
                    _logger.LogWarning(ex, "Failed to process message (Attempt {Attempt}/{MaxRetry}). EventId: {EventId}", 
                        currentAttempt, _kafkaOptions.RetryCount, eventId);

                    if (currentAttempt <= _kafkaOptions.RetryCount)
                    {
                        var delay = TimeSpan.FromSeconds(_kafkaOptions.RetryDelaySeconds * Math.Pow(2, currentAttempt - 1));
                        _logger.LogInformation("Waiting for {DelaySeconds}s before retry...", delay.TotalSeconds);
                        await Task.Delay(delay, stoppingToken);
                    }
                }
            }

            if (!success)
            {
                // All retries failed -> DLQ
                _logger.LogError("All processing retries failed for EventId: {EventId}. Redirecting to DLQ...", eventId);
                await RedirectToDlqAsync(consumeResult, eventId, correlationId, eventType, "ProcessingFailure", "Max retries exceeded.", currentAttempt);
                consumer.Commit(consumeResult);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Malformed message payload. Sending directly to DLQ. Payload: {Payload}", consumeResult.Message.Value);
            await RedirectToDlqAsync(consumeResult, eventId, correlationId, eventType, "DeserializationFailure", ex.Message, 0);
            consumer.Commit(consumeResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critical error handling message. Sending to DLQ.");
            await RedirectToDlqAsync(consumeResult, eventId, correlationId, eventType, "CriticalFailure", ex.Message, 0);
            consumer.Commit(consumeResult);
        }
    }

    private async Task DispatchToHandlerAsync(IServiceScope scope, string topic, string payload)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        switch (topic)
        {
            case TopicNames.QuizSubmitted:
                var quizEvent = JsonSerializer.Deserialize<QuizSubmittedEvent>(payload, options)
                    ?? throw new JsonException("Failed to deserialize QuizSubmittedEvent.");
                var quizHandler = scope.ServiceProvider.GetRequiredService<IEventHandler<QuizSubmittedEvent>>();
                await quizHandler.HandleAsync(quizEvent);
                break;

            case TopicNames.LessonCompleted:
                var lessonEvent = JsonSerializer.Deserialize<LessonCompletedEvent>(payload, options)
                    ?? throw new JsonException("Failed to deserialize LessonCompletedEvent.");
                var lessonHandler = scope.ServiceProvider.GetRequiredService<IEventHandler<LessonCompletedEvent>>();
                await lessonHandler.HandleAsync(lessonEvent);
                break;

            case TopicNames.FeedbackSubmitted:
                var feedbackEvent = JsonSerializer.Deserialize<FeedbackSubmittedEvent>(payload, options)
                    ?? throw new JsonException("Failed to deserialize FeedbackSubmittedEvent.");
                var feedbackHandler = scope.ServiceProvider.GetRequiredService<IEventHandler<FeedbackSubmittedEvent>>();
                await feedbackHandler.HandleAsync(feedbackEvent);
                break;

            case TopicNames.PlacementTestCompleted:
                var placementEvent = JsonSerializer.Deserialize<PlacementTestCompletedEvent>(payload, options)
                    ?? throw new JsonException("Failed to deserialize PlacementTestCompletedEvent.");
                var placementHandler = scope.ServiceProvider.GetRequiredService<IEventHandler<PlacementTestCompletedEvent>>();
                await placementHandler.HandleAsync(placementEvent);
                break;

            case TopicNames.GoalCompleted:
                var goalCompletedEvent = JsonSerializer.Deserialize<GoalCompletedEvent>(payload, options)
                    ?? throw new JsonException("Failed to deserialize GoalCompletedEvent.");
                var goalCompletedHandler = scope.ServiceProvider.GetRequiredService<IEventHandler<GoalCompletedEvent>>();
                await goalCompletedHandler.HandleAsync(goalCompletedEvent);
                break;

            case TopicNames.BadgeAwarded:
                var badgeAwardedEvent = JsonSerializer.Deserialize<BadgeAwardedEvent>(payload, options)
                    ?? throw new JsonException("Failed to deserialize BadgeAwardedEvent.");
                var badgeAwardedHandler = scope.ServiceProvider.GetRequiredService<IEventHandler<BadgeAwardedEvent>>();
                await badgeAwardedHandler.HandleAsync(badgeAwardedEvent);
                break;

            default:
                throw new NotSupportedException($"Topic {topic} is not supported by dispatcher.");
        }
    }

    private async Task RedirectToDlqAsync(
        ConsumeResult<string, string> consumeResult,
        Guid eventId,
        Guid correlationId,
        string eventType,
        string errorType,
        string errorMessage,
        int retryCount)
    {
        var dlqEvent = new DeadLetterEvent
        {
            OriginalTopic = consumeResult.Topic,
            OriginalPartition = consumeResult.Partition.Value,
            OriginalOffset = consumeResult.Offset.Value,
            OriginalKey = consumeResult.Message.Key ?? string.Empty,
            TargetEventType = eventType,
            TargetEventId = eventId == Guid.Empty ? null : eventId,
            TargetCorrelationId = correlationId == Guid.Empty ? null : correlationId,
            OriginalPayload = consumeResult.Message.Value,
            ErrorType = errorType,
            ErrorMessage = errorMessage,
            FailedAt = DateTimeOffset.UtcNow,
            RetryCount = retryCount
        };

        var valueJson = JsonSerializer.Serialize(dlqEvent, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var message = new Message<string, string>
        {
            Key = consumeResult.Message.Key ?? eventId.ToString(),
            Value = valueJson
        };

        try
        {
            var dlqTopic = _kafkaOptions.DeadLetterTopic;
            if (string.IsNullOrEmpty(dlqTopic)) dlqTopic = TopicNames.DeadLetterTopic;

            _logger.LogInformation("Publishing failed message to DLQ topic {DlqTopic}...", dlqTopic);
            var deliveryResult = await _producer.ProduceAsync(dlqTopic, message);
            _logger.LogInformation("Successfully sent to DLQ: topic={Topic}, partition={Partition}, offset={Offset}",
                deliveryResult.Topic, deliveryResult.Partition, deliveryResult.Offset);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "FATAL: Failed to publish message to DLQ. Original EventId: {EventId}. Original Payload: {Payload}", 
                eventId, consumeResult.Message.Value);
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("KafkaConsumerHostedService is shutting down...");
        return base.StopAsync(cancellationToken);
    }
}
