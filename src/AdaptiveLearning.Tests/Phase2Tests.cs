using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using AdaptiveLearning.Contracts.Events;
using AdaptiveLearning.Worker.Services;

namespace AdaptiveLearning.Tests;

public class Phase2Tests
{
    [Fact]
    public void QuizSubmittedEvent_Should_SerializeAndDeserializeCorrectly()
    {
        // Arrange
        var originalEvent = new QuizSubmittedEvent
        {
            UserId = 1,
            QuizId = 10,
            QuizAttemptId = 100,
            Score = 85.5,
            TotalQuestions = 10,
            CorrectAnswers = 8,
            SubmittedAt = DateTimeOffset.UtcNow,
            AnswerDetails = new List<QuizAnswerDetail>
            {
                new() { QuestionId = 101, SkillName = "Listening", Topic = "Pronunciation", Level = "A1", IsCorrect = true },
                new() { QuestionId = 102, SkillName = "Reading", Topic = "Vocabulary", Level = "A1", IsCorrect = false }
            }
        };

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        // Act
        var json = JsonSerializer.Serialize(originalEvent, options);
        var deserializedEvent = JsonSerializer.Deserialize<QuizSubmittedEvent>(json, options);

        // Assert
        Assert.NotNull(deserializedEvent);
        Assert.Equal(originalEvent.EventId, deserializedEvent.EventId);
        Assert.Equal(originalEvent.EventType, deserializedEvent.EventType);
        Assert.Equal(originalEvent.UserId, deserializedEvent.UserId);
        Assert.Equal(originalEvent.QuizId, deserializedEvent.QuizId);
        Assert.Equal(originalEvent.Score, deserializedEvent.Score);
        Assert.Equal(originalEvent.AnswerDetails.Count, deserializedEvent.AnswerDetails.Count);
        Assert.Equal(originalEvent.AnswerDetails[0].SkillName, deserializedEvent.AnswerDetails[0].SkillName);
    }

    [Fact]
    public async Task InMemoryProcessedEventStore_Should_DetectDuplicates()
    {
        // Arrange
        var store = new InMemoryProcessedEventStore();
        var eventId = Guid.NewGuid();

        // Act & Assert
        bool isProcessedBefore = await store.HasBeenProcessedAsync(eventId);
        Assert.False(isProcessedBefore);

        await store.MarkAsProcessedAsync(eventId, TimeSpan.FromSeconds(10));

        bool isProcessedAfter = await store.HasBeenProcessedAsync(eventId);
        Assert.True(isProcessedAfter);
    }

    [Fact]
    public void DeadLetterEvent_Should_SerializeCorrectly()
    {
        // Arrange
        var dlqEvent = new DeadLetterEvent
        {
            OriginalTopic = "quiz-submitted",
            OriginalPartition = 0,
            OriginalOffset = 42,
            OriginalKey = "attempt-100",
            TargetEventType = "QuizSubmittedEvent",
            TargetEventId = Guid.NewGuid(),
            TargetCorrelationId = Guid.NewGuid(),
            OriginalPayload = "{\"userId\": 1}",
            ErrorType = "ProcessingFailure",
            ErrorMessage = "Transient error.",
            FailedAt = DateTimeOffset.UtcNow,
            RetryCount = 3
        };

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        // Act
        var json = JsonSerializer.Serialize(dlqEvent, options);
        var deserialized = JsonSerializer.Deserialize<DeadLetterEvent>(json, options);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(dlqEvent.OriginalTopic, deserialized.OriginalTopic);
        Assert.Equal(dlqEvent.OriginalOffset, deserialized.OriginalOffset);
        Assert.Equal(dlqEvent.TargetEventId, deserialized.TargetEventId);
        Assert.Equal(dlqEvent.ErrorMessage, deserialized.ErrorMessage);
    }
}
