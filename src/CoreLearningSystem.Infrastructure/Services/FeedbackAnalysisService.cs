using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Application.Interfaces;
using CoreLearningSystem.Application.Options;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CoreLearningSystem.Infrastructure.Services;

/// <summary>
/// Aggregates feedback into FeedbackAnalysis rows (upsert by AggregateKey).
/// Sends admin alert Notification when the aggregate crosses a warning/critical threshold.
/// Does NOT leak learner comment content into admin alerts.
/// </summary>
public sealed class FeedbackAnalysisService : IFeedbackAnalysisService
{
    private readonly IRepository<FeedbackAnalysis> _analysisRepo;
    private readonly IRepository<User> _userRepo;
    private readonly INotificationService _notificationService;
    private readonly FeedbackAnalysisOptions _options;
    private readonly ILogger<FeedbackAnalysisService> _logger;

    private const int NegativeThreshold = 2; // rating <= 2 is low/negative

    public FeedbackAnalysisService(
        IRepository<FeedbackAnalysis> analysisRepo,
        IRepository<User> userRepo,
        INotificationService notificationService,
        IOptions<FeedbackAnalysisOptions> options,
        ILogger<FeedbackAnalysisService> logger)
    {
        _analysisRepo = analysisRepo;
        _userRepo = userRepo;
        _notificationService = notificationService;
        _options = options.Value;
        _logger = logger;
    }

    // ── ProcessFeedbackAsync ────────────────────────────────────────────────
    public async Task ProcessFeedbackAsync(
        int feedbackId,
        int learnerProfileId,
        FeedbackTargetType targetType,
        int? targetId,
        int rating,
        CancellationToken ct = default)
    {
        var aggregateKey = FeedbackAnalysis.BuildAggregateKey(targetType, targetId);
        rating = Math.Clamp(rating, 1, 5);

        try
        {
            // Upsert via FindAsync (IRepository does not have FirstOrDefaultAsync)
            var existing = await _analysisRepo.FindAsync(a => a.AggregateKey == aggregateKey);
            var analysis = existing.FirstOrDefault();
            bool isNew = analysis == null;

            if (isNew)
            {
                analysis = new FeedbackAnalysis
                {
                    AggregateKey = aggregateKey,
                    TargetType = targetType,
                    TargetId = targetId,
                    FeedbackCount = 0,
                    AverageRating = 0,
                    PositiveCount = 0,
                    NeutralCount = 0,
                    NegativeCount = 0,
                    LowRatingCount = 0,
                    LastFeedbackAt = DateTime.UtcNow,
                    LastAnalyzedAt = DateTime.UtcNow,
                    AlertStatus = FeedbackAlertStatus.Normal,
                    UpdatedAt = DateTime.UtcNow
                };
            }

            // Incremental aggregate update
            double prevTotal = analysis!.AverageRating * analysis.FeedbackCount;
            analysis.FeedbackCount++;
            analysis.AverageRating = (prevTotal + rating) / analysis.FeedbackCount;

            if (rating >= 4)
                analysis.PositiveCount++;
            else if (rating <= NegativeThreshold)
            {
                analysis.NegativeCount++;
                analysis.LowRatingCount++;
            }
            else
                analysis.NeutralCount++;

            analysis.LastFeedbackAt = DateTime.UtcNow;
            analysis.LastAnalyzedAt = DateTime.UtcNow;
            analysis.UpdatedAt = DateTime.UtcNow;

            var previousAlertStatus = analysis.AlertStatus;
            var newAlertStatus = EvaluateAlertStatus(analysis);
            analysis.AlertStatus = newAlertStatus;

            if (isNew)
                await _analysisRepo.AddAsync(analysis);
            else
                await _analysisRepo.UpdateAsync(analysis);

            await _analysisRepo.SaveChangesAsync();

            _logger.LogInformation(
                "FeedbackAnalysis upserted. Key={Key}, Count={Count}, Avg={Avg:F2}, Alert={Alert}",
                aggregateKey, analysis.FeedbackCount, analysis.AverageRating, newAlertStatus);

            // Send admin alert only when escalating to Warning or Critical
            if (newAlertStatus != previousAlertStatus &&
                (newAlertStatus == FeedbackAlertStatus.Warning || newAlertStatus == FeedbackAlertStatus.Critical))
            {
                await SendAdminAlertAsync(analysis, ct);
                analysis.AlertedAt = DateTime.UtcNow;
                await _analysisRepo.UpdateAsync(analysis);
                await _analysisRepo.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "FeedbackAnalysisService.ProcessFeedbackAsync failed for feedbackId={FeedbackId}", feedbackId);
        }
    }

    // ── GetAnalysisAsync ────────────────────────────────────────────────────
    public async Task<FeedbackAnalysis?> GetAnalysisAsync(string aggregateKey, CancellationToken ct = default)
    {
        var results = await _analysisRepo.FindAsync(a => a.AggregateKey == aggregateKey);
        return results.FirstOrDefault();
    }

    // ── GetAnalysesForTypeAsync ─────────────────────────────────────────────
    public async Task<List<FeedbackAnalysis>> GetAnalysesForTypeAsync(
        FeedbackTargetType targetType, CancellationToken ct = default)
    {
        var results = await _analysisRepo.FindAsync(a => a.TargetType == targetType);
        return results.ToList();
    }

    // ── Private helpers ─────────────────────────────────────────────────────
    private FeedbackAlertStatus EvaluateAlertStatus(FeedbackAnalysis analysis)
    {
        // Never downgrade from Resolved automatically
        if (analysis.AlertStatus == FeedbackAlertStatus.Resolved)
            return FeedbackAlertStatus.Resolved;

        if (analysis.FeedbackCount < _options.MinimumCountForAlert)
            return FeedbackAlertStatus.Normal;

        double lowRatingRate = (double)analysis.LowRatingCount / analysis.FeedbackCount;

        if (analysis.AverageRating <= _options.CriticalAverageRatingThreshold ||
            lowRatingRate >= _options.CriticalLowRatingRateThreshold)
            return FeedbackAlertStatus.Critical;

        if (analysis.AverageRating <= _options.WarningAverageRatingThreshold ||
            lowRatingRate >= _options.WarningLowRatingRateThreshold)
            return FeedbackAlertStatus.Warning;

        return FeedbackAlertStatus.Normal;
    }

    private async Task SendAdminAlertAsync(FeedbackAnalysis analysis, CancellationToken ct)
    {
        try
        {
            var admins = await _userRepo.FindAsync(u => u.Role == UserRole.Admin);
            var adminList = admins.ToList();

            if (!adminList.Any())
            {
                _logger.LogWarning("FeedbackAlert: No admin users found to notify.");
                return;
            }

            var severity = analysis.AlertStatus == FeedbackAlertStatus.Critical ? "⚠️ CRITICAL" : "⚠️ WARNING";
            var targetLabel = analysis.TargetId.HasValue
                ? $"{analysis.TargetType} #{analysis.TargetId}"
                : "System (global)";

            var title = $"{severity} Feedback Alert – {targetLabel}";
            // Safe: no learner comment content is included
            var message = $"[{analysis.AggregateKey}] received {analysis.FeedbackCount} feedback(s). " +
                          $"Avg rating: {analysis.AverageRating:F2}/5. " +
                          $"Low-rating count: {analysis.LowRatingCount}. " +
                          $"Status escalated to: {analysis.AlertStatus}.";

            foreach (var admin in adminList)
            {
                // Idempotency key: alert tier + date hour prevents duplicates within the cooldown window
                var idempotencyKey =
                    $"feedback-alert:{analysis.AggregateKey}:{analysis.AlertStatus}:{DateTime.UtcNow:yyyyMMdd-HH}";

                try
                {
                    await _notificationService.CreateNotificationAsync(
                        new CreateNotificationRequest
                        {
                            UserId = admin.Id,
                            Type = NotificationType.FeedbackAlert,
                            Channel = NotificationChannel.InApp,
                            Title = title,
                            Message = message,
                            IdempotencyKey = idempotencyKey,
                            SourceType = "FeedbackAnalysis",
                            SourceId = analysis.Id.ToString()
                        }, ct);

                    _logger.LogInformation(
                        "FeedbackAlert sent to admin userId={UserId}, key={Key}, status={Status}",
                        admin.Id, analysis.AggregateKey, analysis.AlertStatus);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "FeedbackAlert notification failed for admin userId={UserId}", admin.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SendAdminAlertAsync failed for aggregateKey={Key}", analysis.AggregateKey);
        }
    }
}
