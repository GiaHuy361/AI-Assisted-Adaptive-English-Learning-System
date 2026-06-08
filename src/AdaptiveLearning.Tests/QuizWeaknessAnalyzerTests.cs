using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using AdaptiveLearning.GrpcService.Services;

namespace AdaptiveLearning.Tests;

public class QuizWeaknessAnalyzerTests
{
    private readonly QuizWeaknessAnalyzer _analyzer = new();

    [Fact]
    public async Task AnalyzeAsync_Should_ThrowArgumentNullException_WhenInputIsNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _analyzer.AnalyzeAsync(null!));
    }

    [Fact]
    public async Task AnalyzeAsync_Should_ReturnDefaultResult_WhenNoAnswersProvided()
    {
        // Arrange
        var input = new QuizAnalysisInput
        {
            UserId = 1,
            QuizId = 10,
            QuizAttemptId = 100,
            Score = 0.0,
            TotalQuestions = 0,
            CorrectAnswers = 0,
            Answers = new List<AnswerAnalysisDetail>()
        };

        // Act
        var result = await _analyzer.AnalyzeAsync(input);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.AnalysisId);
        Assert.Equal(1, result.UserId);
        Assert.Equal(string.Empty, result.WeakestSkill);
        Assert.Empty(result.WeakTopics);
        Assert.Empty(result.SkillScores);
        Assert.Equal("No answers provided.", result.Reason);
    }

    [Fact]
    public async Task AnalyzeAsync_Should_CalculateScoreCorrectly_AndIdentifyWeakestSkill()
    {
        // Arrange
        var input = new QuizAnalysisInput
        {
            UserId = 42,
            Answers = new List<AnswerAnalysisDetail>
            {
                new() { QuestionId = 1, Skill = "Listening", Topic = "Accent", IsCorrect = true },
                new() { QuestionId = 2, Skill = "Listening", Topic = "Liaison", IsCorrect = false },
                new() { QuestionId = 3, Skill = "Reading", Topic = "Vocabulary", IsCorrect = true },
                new() { QuestionId = 4, Skill = "Writing", Topic = "Grammar", IsCorrect = true }
            }
        };

        // Act
        var result = await _analyzer.AnalyzeAsync(input);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Listening", result.WeakestSkill);
        Assert.Single(result.WeakTopics);
        Assert.Equal("Liaison", result.WeakTopics[0]);

        var listeningScore = result.SkillScores.FirstOrDefault(s => s.Skill.Equals("Listening", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(listeningScore);
        Assert.Equal(50.0, listeningScore.Score);
        Assert.Equal(2, listeningScore.TotalQuestions);
        Assert.Equal(1, listeningScore.CorrectAnswers);
        Assert.Equal(1, listeningScore.IncorrectAnswers);

        var readingScore = result.SkillScores.FirstOrDefault(s => s.Skill.Equals("Reading", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(readingScore);
        Assert.Equal(100.0, readingScore.Score);

        var writingScore = result.SkillScores.FirstOrDefault(s => s.Skill.Equals("Writing", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(writingScore);
        Assert.Equal(100.0, writingScore.Score);
    }

    [Fact]
    public async Task AnalyzeAsync_Should_ResolveTies_ByIncorrectAnswersCount()
    {
        // Scenario:
        // Listening: 0/1 correct (0% score, 1 incorrect)
        // Reading: 1/2 correct (50% score, 1 incorrect)
        // Writing: 1/3 correct (33.33% score, 2 incorrect)
        // Speaking: 0/2 correct (0% score, 2 incorrect)
        //
        // Both Listening and Speaking have 0% score.
        // Speaking has more incorrect answers (2) than Listening (1).
        // Therefore, Speaking must be chosen as the weakest skill.

        // Arrange
        var input = new QuizAnalysisInput
        {
            UserId = 1,
            Answers = new List<AnswerAnalysisDetail>
            {
                new() { QuestionId = 1, Skill = "Listening", Topic = "P1", IsCorrect = false },
                new() { QuestionId = 2, Skill = "Reading", Topic = "P2", IsCorrect = true },
                new() { QuestionId = 3, Skill = "Reading", Topic = "P3", IsCorrect = false },
                new() { QuestionId = 4, Skill = "Writing", Topic = "P4", IsCorrect = true },
                new() { QuestionId = 5, Skill = "Writing", Topic = "P5", IsCorrect = false },
                new() { QuestionId = 6, Skill = "Writing", Topic = "P6", IsCorrect = false },
                new() { QuestionId = 7, Skill = "Speaking", Topic = "P7", IsCorrect = false },
                new() { QuestionId = 8, Skill = "Speaking", Topic = "P8", IsCorrect = false }
            }
        };

        // Act
        var result = await _analyzer.AnalyzeAsync(input);

        // Assert
        Assert.Equal("Speaking", result.WeakestSkill);
        var weakTopics = result.WeakTopics;
        Assert.Equal(2, weakTopics.Count);
        Assert.Contains("P7", weakTopics);
        Assert.Contains("P8", weakTopics);
    }

    [Fact]
    public async Task AnalyzeAsync_Should_ResolveTiesAlphabetically_WhenScoreAndIncorrectCountAreEqual()
    {
        // Scenario:
        // Speaking: 0/1 correct (0% score, 1 incorrect)
        // Listening: 0/1 correct (0% score, 1 incorrect)
        // Both have 0% score and 1 incorrect answer.
        // Alphabetically, "Listening" comes before "Speaking" (case-insensitive).
        // Therefore, "Listening" must be chosen.

        // Arrange
        var input = new QuizAnalysisInput
        {
            UserId = 1,
            Answers = new List<AnswerAnalysisDetail>
            {
                new() { QuestionId = 1, Skill = "Speaking", Topic = "Fluency", IsCorrect = false },
                new() { QuestionId = 2, Skill = "Listening", Topic = "Accent", IsCorrect = false }
            }
        };

        // Act
        var result = await _analyzer.AnalyzeAsync(input);

        // Assert
        Assert.Equal("Listening", result.WeakestSkill);
    }

    [Fact]
    public async Task AnalyzeAsync_Should_FilterAndSortWeakTopicsCorrectly()
    {
        // Arrange
        var input = new QuizAnalysisInput
        {
            UserId = 1,
            Answers = new List<AnswerAnalysisDetail>
            {
                new() { QuestionId = 1, Skill = "Writing", Topic = "grammar", IsCorrect = false },
                new() { QuestionId = 2, Skill = "Writing", Topic = "Grammar", IsCorrect = false }, // Duplicate case-insensitive
                new() { QuestionId = 3, Skill = "Writing", Topic = "vocabulary", IsCorrect = false },
                new() { QuestionId = 4, Skill = "Writing", Topic = "Punctuation", IsCorrect = false },
                new() { QuestionId = 5, Skill = "Writing", Topic = "Spelling", IsCorrect = true } // Correct, so should not be in weak topics
            }
        };

        // Act
        var result = await _analyzer.AnalyzeAsync(input);

        // Assert
        Assert.Equal("Writing", result.WeakestSkill);
        // Should be ordered alphabetically: grammar, Punctuation, vocabulary (grammar vs Grammar -> first occurrence determines case casing or standard de-dup, here they are group case-insensitively)
        Assert.Equal(3, result.WeakTopics.Count);
        Assert.Equal("grammar", result.WeakTopics[0]); // first occurrence
        Assert.Equal("Punctuation", result.WeakTopics[1]);
        Assert.Equal("vocabulary", result.WeakTopics[2]);
    }
}
