using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Application.Options;
using CoreLearningSystem.Application.Interfaces;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;
using CoreLearningSystem.Infrastructure.Persistence;
using AdaptiveLearning.Contracts.Events;
using AdaptiveLearning.Contracts.Topics;

namespace CoreLearningSystem.Infrastructure.Services;

public class AchievementService : IAchievementService
{
    private readonly AppDbContext _context;
    private readonly IAchievementEngine _engine;
    private readonly IKafkaPublisher _publisher;
    private readonly AchievementOptions _options;
    private readonly ILogger<AchievementService> _logger;

    public AchievementService(
        AppDbContext context,
        IAchievementEngine engine,
        IKafkaPublisher publisher,
        IOptions<AchievementOptions> options,
        ILogger<AchievementService> logger)
    {
        _context = context;
        _engine = engine;
        _publisher = publisher;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AchievementAwardResult> EvaluateAndAwardAsync(AchievementEvaluationRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var result = new AchievementAwardResult { LearnerProfileId = request.LearnerProfileId };
        var now = request.OccurredAt == default ? DateTime.UtcNow : request.OccurredAt;

        // Load active badges
        var activeBadges = await _context.AchievementBadges
            .Where(b => b.IsActive)
            .ToListAsync(cancellationToken);

        if (!activeBadges.Any())
        {
            _logger.LogInformation("No active badges found in database.");
            return result;
        }

        // Load already earned badges
        var earnedBadges = await _context.LearnerBadges
            .Where(b => b.LearnerProfileId == request.LearnerProfileId)
            .ToListAsync(cancellationToken);

        // Fetch metrics from DB if they are not provided (i.e. default/zero values or we always recalculate to be safe)
        // Wait, to be robust, let's recalculate the metrics from database directly to prevent any worker caller mistakes.
        var completedLessonCount = await _context.LearnerProgresses
            .CountAsync(lp => lp.LearnerProfileId == request.LearnerProfileId && lp.IsCompleted, cancellationToken);

        var highScoreQuizCount = await _context.QuizAttempts
            .CountAsync(qa => qa.LearnerProfileId == request.LearnerProfileId && qa.Score >= _options.HighScoreThresholdPercent, cancellationToken);

        var completedGoalCount = await _context.GoalSettings
            .CountAsync(g => g.LearnerProfileId == request.LearnerProfileId && g.Status == GoalStatus.Completed, cancellationToken);

        var quizAttemptsCount = await _context.QuizAttempts
            .CountAsync(qa => qa.LearnerProfileId == request.LearnerProfileId, cancellationToken);

        var placementTestsCount = await _context.PlacementTestResults
            .CountAsync(ptr => ptr.LearnerProfileId == request.LearnerProfileId, cancellationToken);

        // Compute streak
        var streakDays = await CalculateCurrentStreakAsync(request.LearnerProfileId, now, cancellationToken);

        // Compute skill improvement
        var skillImprovementPoints = await CalculateSkillImprovementAsync(request.LearnerProfileId, cancellationToken);

        // Populate request metrics
        request.CompletedLessonCount = completedLessonCount;
        request.HighScoreQuizCount = highScoreQuizCount;
        request.CompletedGoalCount = completedGoalCount;
        request.CurrentStreakDays = streakDays;
        request.SkillImprovementPoints = skillImprovementPoints;
        request.IsFirstLesson = completedLessonCount == 1 && request.Trigger == AchievementTrigger.LessonCompleted;
        request.IsFirstQuiz = quizAttemptsCount == 1 && request.Trigger == AchievementTrigger.QuizSubmitted;
        request.IsFirstPlacementTest = placementTestsCount == 1 && request.Trigger == AchievementTrigger.PlacementTestCompleted;

        // Evaluate eligible achievements (pass empty list to get all whose criteria are met)
        var eligibleBadges = _engine.Evaluate(request, activeBadges, new List<LearnerBadge>());

        var newEligibleBadges = new List<EligibleAchievement>();
        var earnedIds = new HashSet<int>(earnedBadges.Select(b => b.BadgeId));
        foreach (var eligible in eligibleBadges)
        {
            if (earnedIds.Contains(eligible.AchievementId))
            {
                result.SkippedDuplicates++;
            }
            else
            {
                newEligibleBadges.Add(eligible);
            }
        }

        if (!newEligibleBadges.Any())
        {
            _logger.LogInformation("No new achievements earned for LearnerProfileId: {ProfileId}", request.LearnerProfileId);
            return result;
        }

        // Start database transaction
        await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var profile = await _context.LearnerProfiles.FindAsync(new object[] { request.LearnerProfileId }, cancellationToken);
            var userId = profile?.UserId ?? request.LearnerProfileId;

            var badgesToAward = new List<LearnerBadge>();

            foreach (var eligible in newEligibleBadges)
            {
                // Double-check uniqueness inside transaction
                var alreadyEarned = await _context.LearnerBadges
                    .AnyAsync(lb => lb.LearnerProfileId == request.LearnerProfileId && lb.BadgeId == eligible.AchievementId, cancellationToken);

                if (alreadyEarned)
                {
                    result.SkippedDuplicates++;
                    continue;
                }

                var newBadge = new LearnerBadge
                {
                    LearnerProfileId = request.LearnerProfileId,
                    BadgeId = eligible.AchievementId,
                    UnlockedAt = now,
                    SourceEventId = request.SourceEventId,
                    ProgressValue = eligible.MetricValue,
                    Reason = eligible.Reason
                };

                _context.LearnerBadges.Add(newBadge);
                badgesToAward.Add(newBadge);
            }

            if (badgesToAward.Any())
            {
                await _context.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);

                // Publish Kafka events after commit
                foreach (var badge in badgesToAward)
                {
                    var details = newEligibleBadges.First(e => e.AchievementId == badge.BadgeId);
                    
                    var eventObj = new BadgeAwardedEvent
                    {
                        UserId = userId,
                        LearnerProfileId = badge.LearnerProfileId,
                        AchievementId = badge.BadgeId,
                        AchievementCode = details.Code,
                        AchievementName = details.Name,
                        AwardedAt = badge.UnlockedAt,
                        ProgressValue = badge.ProgressValue,
                        Reason = badge.Reason
                    };

                    await _publisher.PublishAsync(TopicNames.BadgeAwarded, badge.BadgeId.ToString(), eventObj);

                    result.AwardedBadges.Add(new AwardedBadgeDto
                    {
                        AchievementId = badge.BadgeId,
                        Code = details.Code,
                        Name = details.Name,
                        MetricValue = badge.ProgressValue,
                        Reason = badge.Reason,
                        AwardedAt = badge.UnlockedAt
                    });

                    _logger.LogInformation("Badge awarded and published: Code={Code}, LearnerProfileId={ProfileId}", 
                        details.Code, request.LearnerProfileId);
                }
            }
            else
            {
                await tx.RollbackAsync(cancellationToken);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error awarding achievements for profile {ProfileId}", request.LearnerProfileId);
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<int> CalculateCurrentStreakAsync(int profileId, DateTime now, CancellationToken cancellationToken)
    {
        var progressDates = await _context.LearnerProgresses
            .Where(lp => lp.LearnerProfileId == profileId && lp.IsCompleted && lp.CompletedAt.HasValue)
            .Select(lp => lp.CompletedAt ?? DateTime.MinValue)
            .ToListAsync(cancellationToken);

        var quizDates = await _context.QuizAttempts
            .Where(qa => qa.LearnerProfileId == profileId)
            .Select(qa => qa.AttemptedAt)
            .ToListAsync(cancellationToken);

        var allDates = progressDates.Concat(quizDates).Select(d => d.Date).Distinct().OrderByDescending(d => d).ToList();
        if (!allDates.Any()) return 0;

        var today = now.Date;
        var yesterday = today.AddDays(-1);

        var latest = allDates.First();
        if (latest != today && latest != yesterday)
        {
            return 0;
        }

        int streak = 0;
        var current = latest;

        foreach (var date in allDates)
        {
            if (date == current)
            {
                streak++;
                current = current.AddDays(-1);
            }
            else if (date < current)
            {
                break;
            }
        }

        return streak;
    }

    private async Task<double> CalculateSkillImprovementAsync(int profileId, CancellationToken cancellationToken)
    {
        var skillMatrices = await _context.SkillMatrices
            .Where(sm => sm.LearnerProfileId == profileId)
            .ToListAsync(cancellationToken);

        var earliestHistories = await _context.SkillMatrixHistories
            .Where(smh => smh.LearnerProfileId == profileId)
            .GroupBy(smh => smh.Skill)
            .Select(g => new { Skill = g.Key, EarliestPreviousScore = g.OrderBy(x => x.RecordedAt).Select(x => x.PreviousScore).FirstOrDefault() })
            .ToListAsync(cancellationToken);

        double maxImprovement = 0.0;
        foreach (var sm in skillMatrices)
        {
            var earliest = earliestHistories.FirstOrDefault(eh => eh.Skill == sm.Skill);
            if (earliest != null)
            {
                var diff = sm.CurrentScore - earliest.EarliestPreviousScore;
                if (diff > maxImprovement)
                {
                    maxImprovement = diff;
                }
            }
        }

        return maxImprovement;
    }
}
