using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Application.Interfaces;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;
using CoreLearningSystem.Infrastructure.Persistence;

namespace CoreLearningSystem.Infrastructure.Services;

public class SkillDecayJob
{
    private readonly AppDbContext _context;
    private readonly INotificationService _notificationService;
    private readonly BackgroundJobExecutor _executor;
    private readonly ILogger<SkillDecayJob> _logger;

    public SkillDecayJob(
        AppDbContext context,
        INotificationService notificationService,
        BackgroundJobExecutor executor,
        ILogger<SkillDecayJob> logger)
    {
        _context = context;
        _notificationService = notificationService;
        _executor = executor;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await _executor.ExecuteAsync("skill-decay", async (executionId, token) =>
        {
            var processed = 0;
            var succeeded = 0;
            var failed = 0;
            var skipped = 0;

            var now = DateTime.UtcNow;
            
            // Calculate decay period key based on current week Monday
            var weekStart = GetWeekStart(now);
            var decayPeriodKey = $"decay:{weekStart:yyyy-MM-dd}";

            // Fetch active learner profiles
            var activeProfiles = await _context.LearnerProfiles
                .Include(p => p.User)
                .Where(p => p.ActivityStatus == ActivityStatus.Active && !p.User.IsLocked)
                .ToListAsync(token);

            _logger.LogInformation("SkillDecayJob: Starting decay check for {Count} active profiles.", activeProfiles.Count);

            foreach (var profile in activeProfiles)
            {
                // Fetch skill matrices for the profile
                var skillMatrices = await _context.SkillMatrices
                    .Where(sm => sm.LearnerProfileId == profile.Id)
                    .ToListAsync(token);

                foreach (var matrix in skillMatrices)
                {
                    processed++;

                    try
                    {
                        // 1. Check if decay has already been applied for this skill in this period
                        var alreadyDecayed = await _context.SkillMatrixHistories
                            .AnyAsync(h => h.SkillMatrixId == matrix.Id && h.DecayPeriodKey == decayPeriodKey, token);

                        if (alreadyDecayed)
                        {
                            skipped++;
                            continue;
                        }

                        // 2. Find last meaningful activity date for this specific skill
                        var latestActivity = await _context.SkillMatrixHistories
                            .Where(h => h.LearnerProfileId == profile.Id && h.Skill == matrix.Skill && h.SourceType != MatrixSourceType.SkillDecay)
                            .Select(h => (DateTime?)h.RecordedAt)
                            .MaxAsync(token);

                        var lastActivityDate = latestActivity ?? matrix.CreatedAt;
                        var daysInactive = (now - lastActivityDate).TotalDays;

                        if (daysInactive < 30)
                        {
                            skipped++;
                            continue;
                        }

                        // 3. Count how many decays have occurred since last activity to determine if it is the first decay or further decay
                        var decayCount = await _context.SkillMatrixHistories
                            .CountAsync(h => h.SkillMatrixId == matrix.Id && h.SourceType == MatrixSourceType.SkillDecay && h.RecordedAt > lastActivityDate, token);

                        bool isFirstDecay = decayCount == 0;
                        bool isEligible = false;
                        double pointsToDecay = 0.0;

                        if (isFirstDecay)
                        {
                            isEligible = true;
                            pointsToDecay = 2.0;
                        }
                        else
                        {
                            // Eligible for subsequent decay if inactive for at least 30 + 14 * N days
                            var requiredDays = 30.0 + (14.0 * decayCount);
                            if (daysInactive >= requiredDays)
                            {
                                isEligible = true;
                                pointsToDecay = 1.0;
                            }
                        }

                        if (!isEligible)
                        {
                            skipped++;
                            continue;
                        }

                        // 4. Perform decay under transaction
                        await using var tx = await _context.Database.BeginTransactionAsync(token);
                        try
                        {
                            // Re-fetch within transaction to prevent concurrency issues
                            var dbMatrix = await _context.SkillMatrices.FindAsync(new object[] { matrix.Id }, token);
                            if (dbMatrix == null)
                            {
                                await tx.RollbackAsync(token);
                                skipped++;
                                continue;
                            }

                            // Re-check uniqueness inside transaction
                            var alreadyDecayedInTx = await _context.SkillMatrixHistories
                                .AnyAsync(h => h.SkillMatrixId == matrix.Id && h.DecayPeriodKey == decayPeriodKey, token);

                            if (alreadyDecayedInTx)
                            {
                                await tx.RollbackAsync(token);
                                skipped++;
                                continue;
                            }

                            double previousScore = dbMatrix.CurrentScore;
                            double newScore = Math.Max(0.0, previousScore - pointsToDecay);
                            var newLevel = ClassifyMasteryLevel(newScore);

                            dbMatrix.CurrentScore = newScore;
                            dbMatrix.MasteryLevel = newLevel;
                            dbMatrix.LastUpdatedAt = now;

                            _context.SkillMatrices.Update(dbMatrix);

                            var history = new SkillMatrixHistory
                            {
                                SkillMatrixId = dbMatrix.Id,
                                LearnerProfileId = profile.Id,
                                Skill = dbMatrix.Skill,
                                PreviousScore = previousScore,
                                AssessmentScore = 0.0,
                                NewScore = newScore,
                                SourceType = MatrixSourceType.SkillDecay,
                                SourceId = 0,
                                EventId = Guid.NewGuid(),
                                Reason = isFirstDecay 
                                    ? $"Skill decay due to 30 days of inactivity (-{pointsToDecay} points)." 
                                    : $"Skill decay due to subsequent 14 days of inactivity (-{pointsToDecay} points).",
                                DecayPeriodKey = decayPeriodKey,
                                RecordedAt = now
                            };

                            _context.SkillMatrixHistories.Add(history);
                            await _context.SaveChangesAsync(token);
                            await tx.CommitAsync(token);

                            // 5. Send notification to the user
                            var idempotencyKey = $"decay:{profile.UserId}:{matrix.Skill}:{decayPeriodKey}";
                            var notifReq = new CreateNotificationRequest
                            {
                                UserId = profile.UserId,
                                LearnerProfileId = profile.Id,
                                Type = NotificationType.SkillDecayWarning,
                                Channel = NotificationChannel.InApp,
                                Title = "Điểm kỹ năng bị giảm nhẹ",
                                Message = $"Điểm kỹ năng {dbMatrix.Skill} của bạn đã bị giảm {-pointsToDecay} điểm do không hoạt động trong {Math.Floor(daysInactive)} ngày qua.",
                                IdempotencyKey = idempotencyKey,
                                SourceType = "SkillDecay",
                                SourceId = dbMatrix.Id.ToString()
                            };

                            await _notificationService.CreateNotificationAsync(notifReq, token);
                            succeeded++;
                        }
                        catch (DbUpdateException)
                        {
                            // Uniqueness violation (e.g. concurrent thread won), safe to rollback and skip
                            await tx.RollbackAsync(token);
                            skipped++;
                        }
                        catch (Exception ex)
                        {
                            await tx.RollbackAsync(token);
                            failed++;
                            _logger.LogError(ex, "Error executing skill decay for matrix ID {MatrixId}", matrix.Id);
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        _logger.LogError(ex, "Error processing skill decay eligibility for matrix ID {MatrixId}", matrix.Id);
                    }
                }
            }

            return (processed, succeeded, failed, skipped);
        }, cancellationToken);
    }

    private static DateTime GetWeekStart(DateTime date)
    {
        var dayOfWeek = (int)date.DayOfWeek;
        var daysSinceMonday = (dayOfWeek == 0) ? 6 : dayOfWeek - 1;
        return date.Date.AddDays(-daysSinceMonday);
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
