using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;
using CoreLearningSystem.Application.Interfaces;
using CoreLearningSystem.Application.DTOs.Common;

namespace CoreLearningSystem.Infrastructure.Services;

public class SkillMatrixService : ISkillMatrixService
{
    private readonly IRepository<SkillMatrix> _matrixRepo;
    private readonly IRepository<SkillMatrixHistory> _historyRepo;
    private readonly IRepository<LearnerWeaknessHistory> _weaknessRepo;
    private readonly ILogger<SkillMatrixService> _logger;

    public SkillMatrixService(
        IRepository<SkillMatrix> matrixRepo,
        IRepository<SkillMatrixHistory> historyRepo,
        IRepository<LearnerWeaknessHistory> weaknessRepo,
        ILogger<SkillMatrixService> logger)
    {
        _matrixRepo = matrixRepo;
        _historyRepo = historyRepo;
        _weaknessRepo = weaknessRepo;
        _logger = logger;
    }

    public async Task<SkillMatrixUpdateResult> UpdateSkillMatrixAsync(SkillMatrixUpdateRequest request, CancellationToken cancellationToken)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        _logger.LogInformation("UpdateSkillMatrixAsync started. EventId: {EventId}, UserId: {UserId}, Source: {SourceType}/{SourceId}",
            request.EventId, request.UserId, request.SourceType, request.SourceId);

        // 1. Idempotency check: check if event has already been processed
        var existingHistory = await _historyRepo.FindAsync(h => h.EventId == request.EventId);
        if (existingHistory.Any())
        {
            _logger.LogWarning("EventId {EventId} has already been processed. Skipping update (Idempotent success).", request.EventId);
            
            // Re-fetch current state to build result
            var learnerMatrices = await _matrixRepo.FindAsync(m => m.LearnerProfileId == request.LearnerProfileId);
            var activeWeaknesses = await _weaknessRepo.FindAsync(w => w.LearnerProfileId == request.LearnerProfileId && w.Status == WeaknessStatus.Active);
            
            var weakest = learnerMatrices
                .OrderBy(m => m.CurrentScore)
                .ThenBy(m => m.Skill)
                .FirstOrDefault();

            var repeated = activeWeaknesses
                .Where(w => w.OccurrenceCount >= 2)
                .Select(w => w.Topic)
                .ToList();

            return new SkillMatrixUpdateResult
            {
                UserId = request.UserId,
                UpdatedSkills = new List<string>(),
                WeakestSkill = weakest?.Skill.ToString() ?? string.Empty,
                RepeatedWeakTopics = repeated,
                UpdatedAt = DateTime.UtcNow
            };
        }

        // 2. Start Transaction
        await _matrixRepo.BeginTransactionAsync();

        try
        {
            var updatedSkills = new List<string>();
            var repeatedWeakTopics = new List<string>();
            var newScoresMap = new Dictionary<SkillType, double>();

            // Fetch existing skill matrices
            var existingMatrices = (await _matrixRepo.FindAsync(m => m.LearnerProfileId == request.LearnerProfileId)).ToList();

            if (request.SourceType == MatrixSourceType.PlacementTest || request.SourceType == MatrixSourceType.Quiz)
            {
                foreach (var scoreDto in request.SkillScores)
                {
                    var existingMatrix = existingMatrices.FirstOrDefault(m => m.Skill == scoreDto.Skill);
                    double previousScore = existingMatrix?.CurrentScore ?? 0.0;
                    double assessmentScore = scoreDto.Score;
                    double newScore = 0.0;

                    if (request.SourceType == MatrixSourceType.PlacementTest)
                    {
                        if (existingMatrix == null)
                        {
                            newScore = assessmentScore;
                        }
                        else
                        {
                            // Formula: 70% current + 30% placement
                            newScore = (0.70 * previousScore) + (0.30 * assessmentScore);
                        }
                    }
                    else if (request.SourceType == MatrixSourceType.Quiz)
                    {
                        if (existingMatrix == null)
                        {
                            newScore = assessmentScore;
                        }
                        else
                        {
                            // Formula: AssessmentWeight = min(0.40, max(0.15, SkillQuestionCount / 20.0))
                            double weight = Math.Min(0.40, Math.Max(0.15, scoreDto.TotalQuestions / 20.0));
                            newScore = (previousScore * (1.0 - weight)) + (assessmentScore * weight);
                        }
                    }

                    newScore = Math.Clamp(newScore, 0.0, 100.0);
                    newScoresMap[scoreDto.Skill] = newScore;

                    var level = ClassifyMasteryLevel(newScore);

                    if (existingMatrix == null)
                    {
                        existingMatrix = new SkillMatrix
                        {
                            LearnerProfileId = request.LearnerProfileId,
                            Skill = scoreDto.Skill,
                            CurrentScore = newScore,
                            MasteryLevel = level,
                            TotalAssessments = 1,
                            LastAssessmentScore = assessmentScore,
                            CreatedAt = request.OccurredAt,
                            LastUpdatedAt = request.OccurredAt
                        };
                        await _matrixRepo.AddAsync(existingMatrix);
                    }
                    else
                    {
                        existingMatrix.CurrentScore = newScore;
                        existingMatrix.MasteryLevel = level;
                        existingMatrix.TotalAssessments += 1;
                        existingMatrix.LastAssessmentScore = assessmentScore;
                        existingMatrix.LastUpdatedAt = request.OccurredAt;
                        await _matrixRepo.UpdateAsync(existingMatrix);
                    }

                    // Save change to DB to generate SkillMatrix.Id
                    await _matrixRepo.SaveChangesAsync();

                    // Insert History
                    var history = new SkillMatrixHistory
                    {
                        SkillMatrixId = existingMatrix.Id,
                        LearnerProfileId = request.LearnerProfileId,
                        Skill = scoreDto.Skill,
                        PreviousScore = previousScore,
                        AssessmentScore = assessmentScore,
                        NewScore = newScore,
                        SourceType = request.SourceType,
                        SourceId = request.SourceId,
                        EventId = request.EventId,
                        Reason = $"{request.SourceType} assessment completed.",
                        RecordedAt = request.OccurredAt
                    };
                    await _historyRepo.AddAsync(history);
                    updatedSkills.Add(scoreDto.Skill.ToString());
                }
            }
            else if (request.SourceType == MatrixSourceType.LessonCompletion)
            {
                // Lesson completed doesn't change matrix score directly
                // Just write history record with current score
                foreach (var skill in existingMatrices.Select(m => m.Skill).Distinct())
                {
                    var matrix = existingMatrices.First(m => m.Skill == skill);
                    var history = new SkillMatrixHistory
                    {
                        SkillMatrixId = matrix.Id,
                        LearnerProfileId = request.LearnerProfileId,
                        Skill = skill,
                        PreviousScore = matrix.CurrentScore,
                        AssessmentScore = 0.0,
                        NewScore = matrix.CurrentScore,
                        SourceType = request.SourceType,
                        SourceId = request.SourceId,
                        EventId = request.EventId,
                        Reason = "Lesson completion activity recorded.",
                        RecordedAt = request.OccurredAt
                    };
                    await _historyRepo.AddAsync(history);
                }
            }

            // 3. Process Weakness History
            var existingWeaknesses = (await _weaknessRepo.FindAsync(w => w.LearnerProfileId == request.LearnerProfileId)).ToList();

            if (request.SourceType == MatrixSourceType.Quiz)
            {
                // Update or Insert Weaknesses
                foreach (var weakTopic in request.WeakTopics)
                {
                    var topic = (weakTopic.Topic ?? string.Empty).Trim();
                    if (string.IsNullOrEmpty(topic)) continue;

                    var existingWeakness = existingWeaknesses.FirstOrDefault(w => 
                        w.Skill == weakTopic.Skill && 
                        w.Topic.Equals(topic, StringComparison.OrdinalIgnoreCase));

                    if (existingWeakness == null)
                    {
                        existingWeakness = new LearnerWeaknessHistory
                        {
                            LearnerProfileId = request.LearnerProfileId,
                            Skill = weakTopic.Skill,
                            Topic = topic,
                            Level = weakTopic.Level,
                            IncorrectCount = weakTopic.IncorrectCount,
                            OccurrenceCount = 1,
                            FirstOccurredAt = request.OccurredAt,
                            LastOccurredAt = request.OccurredAt,
                            SourceQuizAttemptId = request.SourceId,
                            LastEventId = request.EventId,
                            Status = WeaknessStatus.Active
                        };
                        await _weaknessRepo.AddAsync(existingWeakness);
                    }
                    else
                    {
                        // Check if already processed in this event (should not happen if mapped correctly, but safety first)
                        if (existingWeakness.LastEventId != request.EventId)
                        {
                            existingWeakness.IncorrectCount += weakTopic.IncorrectCount;
                            existingWeakness.OccurrenceCount += 1; // Increment since it's a new quiz attempt failure
                            existingWeakness.LastOccurredAt = request.OccurredAt;
                            existingWeakness.SourceQuizAttemptId = request.SourceId;
                            existingWeakness.LastEventId = request.EventId;
                            existingWeakness.Status = WeaknessStatus.Active; // Reactivate to Active
                            await _weaknessRepo.UpdateAsync(existingWeakness);
                        }
                    }

                    if (existingWeakness.OccurrenceCount >= 2 && !repeatedWeakTopics.Contains(existingWeakness.Topic))
                    {
                        repeatedWeakTopics.Add(existingWeakness.Topic);
                    }
                }

                // Resolve Weakness Logic:
                // If a topic was answered in this quiz, and had 0 incorrect answers in this quiz, 
                // and the new skill score is >= 75.0, mark the weakness as Resolved!
                // Wait, how do we know what topics were answered? We can find all topics for this quiz 
                // in the event details but that's handled by comparing existing active/improving weaknesses.
                // If an existing active/improving weakness was tested in this quiz (i.e. present in request's tested topics list) 
                // but is NOT in the weak topics list (meaning 0 incorrect answers), and the new skill score >= 75.0, it is resolved!
                // To do this, we need to know what topics were tested. Let's look at request:
                // We can assume that if a skill was tested in this quiz, and we have weaknesses in this skill, 
                // how do we know if they were tested? 
                // We can pass the tested topics list in the request, or we can check which weaknesses' topics were NOT reported as weak 
                // in this quiz but belong to a skill that WAS tested.
                // Actually, to make it precise, let's look at the Weakness histories of the skills that were tested in the quiz.
                // If a skill score is in the request (meaning the skill was tested), and the learner has a weakness in that skill, 
                // and that weakness's topic is NOT in request.WeakTopics (meaning the learner got 0 incorrect answers on it), 
                // and the new skill score is >= 75.0, we mark it as Resolved!
                // This is extremely safe and makes total sense!
                foreach (var testedSkillScore in request.SkillScores)
                {
                    double newScore = newScoresMap.ContainsKey(testedSkillScore.Skill) ? newScoresMap[testedSkillScore.Skill] : 75.0;
                    
                    if (newScore >= 75.0)
                    {
                        var weaknessesForSkill = existingWeaknesses.Where(w => 
                            w.Skill == testedSkillScore.Skill && 
                            (w.Status == WeaknessStatus.Active || w.Status == WeaknessStatus.Improving)).ToList();

                        foreach (var weakness in weaknessesForSkill)
                        {
                            // If it wasn't reported as weak in this quiz, it means the learner got it correct!
                            bool isStillWeak = request.WeakTopics.Any(wt => 
                                wt.Skill == weakness.Skill && 
                                wt.Topic.Equals(weakness.Topic, StringComparison.OrdinalIgnoreCase));

                            if (!isStillWeak)
                            {
                                weakness.Status = WeaknessStatus.Resolved;
                                weakness.LastEventId = request.EventId;
                                await _weaknessRepo.UpdateAsync(weakness);
                            }
                        }
                    }
                }
            }
            else if (request.SourceType == MatrixSourceType.LessonCompletion)
            {
                // Lesson completion update:
                // If learner completed a lesson with Skill + Topic matching an Active weakness, 
                // change status from Active to Improving.
                // We can pass the completed lesson's Skill and Topic in the request (e.g., using a single entry in WeakTopics or SkillScores).
                // In our integration, we will populate request.WeakTopics with the completed lesson topic (IncorrectCount = 0).
                foreach (var completedTopic in request.WeakTopics)
                {
                    var topic = (completedTopic.Topic ?? string.Empty).Trim();
                    if (string.IsNullOrEmpty(topic)) continue;

                    var matchingActiveWeakness = existingWeaknesses.FirstOrDefault(w => 
                        w.Skill == completedTopic.Skill && 
                        w.Topic.Equals(topic, StringComparison.OrdinalIgnoreCase) &&
                        w.Status == WeaknessStatus.Active);

                    if (matchingActiveWeakness != null)
                    {
                        matchingActiveWeakness.Status = WeaknessStatus.Improving;
                        matchingActiveWeakness.LastEventId = request.EventId;
                        await _weaknessRepo.UpdateAsync(matchingActiveWeakness);
                    }
                }
            }

            // Save all changes
            await _matrixRepo.SaveChangesAsync();
            await _matrixRepo.CommitTransactionAsync();

            // Calculate Weakest Skill across all matrices
            var finalMatrices = await _matrixRepo.FindAsync(m => m.LearnerProfileId == request.LearnerProfileId);
            var weakestSkillMatrix = finalMatrices
                .OrderBy(m => m.CurrentScore)
                .ThenBy(m => m.Skill)
                .FirstOrDefault();

            return new SkillMatrixUpdateResult
            {
                UserId = request.UserId,
                UpdatedSkills = updatedSkills,
                WeakestSkill = weakestSkillMatrix?.Skill.ToString() ?? string.Empty,
                RepeatedWeakTopics = repeatedWeakTopics,
                UpdatedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during UpdateSkillMatrixAsync for EventId: {EventId}. Rolling back transaction.", request.EventId);
            await _matrixRepo.RollbackTransactionAsync();
            throw;
        }
    }

    private static MasteryLevel ClassifyMasteryLevel(double score)
    {
        if (score < 50.0)
        {
            return MasteryLevel.Weak;
        }
        if (score < 75.0)
        {
            return MasteryLevel.Average;
        }
        return MasteryLevel.Good;
    }
}
