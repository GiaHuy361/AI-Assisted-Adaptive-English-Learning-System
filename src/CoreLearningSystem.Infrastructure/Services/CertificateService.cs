using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using CoreLearningSystem.Application.Interfaces;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;
using CoreLearningSystem.Infrastructure.Persistence;

namespace CoreLearningSystem.Infrastructure.Services;

public class CertificateService : ICertificateService
{
    private readonly AppDbContext _context;
    private readonly IKafkaPublisher _kafkaPublisher;

    public CertificateService(AppDbContext context, IKafkaPublisher kafkaPublisher)
    {
        _context = context;
        _kafkaPublisher = kafkaPublisher;
    }

    public async Task<CertificateTestResult> RecordResultAsync(CertificateTestResult result)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));

        // 1. Idempotency Check by SourceQuizAttemptId
        if (result.SourceQuizAttemptId.HasValue)
        {
            var existing = await _context.CertificateTestResults
                .FirstOrDefaultAsync(r => r.SourceQuizAttemptId == result.SourceQuizAttemptId.Value);
            if (existing != null)
            {
                return existing;
            }
        }

        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            // Set fields
            result.CreatedAt = DateTime.UtcNow;
            result.Passed = result.Score >= result.TargetScore;

            await _context.CertificateTestResults.AddAsync(result);
            await _context.SaveChangesAsync();

            // 2. Find active certificate goals
            var goalType = result.CertificateType switch
            {
                CertificateType.TOEIC => GoalType.TOEIC,
                CertificateType.IELTS => GoalType.IELTS,
                CertificateType.VSTEP => GoalType.VSTEP,
                _ => throw new ArgumentException("Unsupported certificate type.")
            };

            var activeGoals = await _context.GoalSettings
                .Where(g => g.LearnerProfileId == result.LearnerProfileId &&
                            g.Status == GoalStatus.Active &&
                            g.Type == goalType)
                .ToListAsync();

            foreach (var goal in activeGoals)
            {
                // Verify if target value is met
                if (result.Score >= goal.TargetValue)
                {
                    var sourceEventId = $"cert_result_{result.Id}";

                    // Check if history already has this event
                    var alreadyProcessed = await _context.GoalProgressHistories
                        .AnyAsync(h => h.GoalId == goal.Id && h.SourceEventId == sourceEventId);

                    if (alreadyProcessed) continue;

                    // Update Goal
                    var statusBefore = goal.Status;
                    var prevValue = goal.CurrentValue;
                    goal.Status = GoalStatus.Completed;
                    goal.IsCompleted = true;
                    goal.CompletedAt = DateTime.UtcNow;
                    goal.CurrentValue = result.Score;
                    goal.ProgressPercentage = 100.0;
                    goal.UpdatedAt = DateTime.UtcNow;

                    _context.GoalSettings.Update(goal);

                    // Add History
                    var history = new GoalProgressHistory
                    {
                        GoalId = goal.Id,
                        LearnerProfileId = result.LearnerProfileId,
                        PreviousValue = prevValue,
                        AddedValue = result.Score - prevValue,
                        NewValue = result.Score,
                        StatusBefore = statusBefore,
                        StatusAfter = GoalStatus.Completed,
                        Reason = $"Mock certificate test {result.CertificateType} passed with score {result.Score} (Target: {goal.TargetValue}).",
                        SourceEventId = sourceEventId,
                        RecordedAt = DateTime.UtcNow
                    };
                    await _context.GoalProgressHistories.AddAsync(history);

                    // Save inside transaction
                    await _context.SaveChangesAsync();

                    // Publish event
                    var ev = new CoreLearningSystem.Application.DTOs.Events.GoalCompletedEvent(
                        goal.Id,
                        result.LearnerProfileId,
                        goal.Target,
                        DateTime.UtcNow
                    );
                    await _kafkaPublisher.PublishGoalCompletedAsync(ev);
                }
            }

            await tx.CommitAsync();
            return result;
        }
        catch (Exception)
        {
            await tx.RollbackAsync();
            throw;
        }
    }
}
