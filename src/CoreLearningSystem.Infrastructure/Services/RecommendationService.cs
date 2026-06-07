using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Application.Interfaces;
using CoreLearningSystem.Application.Options;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;

namespace CoreLearningSystem.Infrastructure.Services;

public class RecommendationService : IRecommendationService
{
    private readonly IRepository<Recommendation> _recommendationRepo;
    private readonly IRepository<RecommendationHistory> _historyRepo;
    private readonly IRepository<Lesson> _lessonRepo;
    private readonly IRepository<LearnerProfile> _profileRepo;
    private readonly IRepository<LearnerProgress> _progressRepo;
    private readonly IRepository<LearnerWeaknessHistory> _weaknessRepo;
    private readonly IAdaptiveRecommendationEngine _engine;
    private readonly RecommendationOptions _options;
    private readonly ILogger<RecommendationService> _logger;

    public RecommendationService(
        IRepository<Recommendation> recommendationRepo,
        IRepository<RecommendationHistory> historyRepo,
        IRepository<Lesson> lessonRepo,
        IRepository<LearnerProfile> profileRepo,
        IRepository<LearnerProgress> progressRepo,
        IRepository<LearnerWeaknessHistory> weaknessRepo,
        IAdaptiveRecommendationEngine engine,
        IOptions<RecommendationOptions> options,
        ILogger<RecommendationService> logger)
    {
        _recommendationRepo = recommendationRepo;
        _historyRepo = historyRepo;
        _lessonRepo = lessonRepo;
        _profileRepo = profileRepo;
        _progressRepo = progressRepo;
        _weaknessRepo = weaknessRepo;
        _engine = engine;
        _options = options.Value;
        _logger = logger;

        // Validation of options
        if (_options.MaxRecommendations <= 0)
            throw new ArgumentException("MaxRecommendations must be greater than 0.", nameof(options));
        if (_options.RecommendationExpirationDays <= 0)
            throw new ArgumentException("RecommendationExpirationDays must be greater than 0.", nameof(options));
        if (_options.DismissedCooldownDays < 0)
            throw new ArgumentException("DismissedCooldownDays must be non-negative.", nameof(options));
        if (_options.MinimumPriorityScore < 0 || _options.MinimumPriorityScore > 100)
            throw new ArgumentException("MinimumPriorityScore must be between 0 and 100.", nameof(options));
    }

    public async Task<RecommendationResponse> GenerateRecommendationsAsync(RecommendationRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        _logger.LogInformation("GenerateRecommendationsAsync started. EventId: {EventId}, UserId: {UserId}",
            request.SourceEventId, request.UserId);

        // 1. Idempotency Check by SourceEventId
        var existingRecsWithEventId = await _recommendationRepo.FindAsync(r => r.SourceEventId == request.SourceEventId);
        var existingList = existingRecsWithEventId.ToList();
        if (existingList.Count > 0)
        {
            _logger.LogInformation("SourceEventId {SourceEventId} has already generated recommendations. Returning existing recommendations (Idempotent success).", request.SourceEventId);

            // Fetch lesson titles to build full response
            var lessonIds = existingList.Select(r => r.LessonId).ToList();
            var lessons = await _lessonRepo.FindAsync(l => lessonIds.Contains(l.Id));
            var lessonMap = lessons.ToDictionary(l => l.Id, l => l.Title);

            return new RecommendationResponse
            {
                UserId = request.UserId,
                WeakestSkill = request.WeakestSkill?.ToString() ?? string.Empty,
                WeakTopics = request.WeakTopics,
                RecommendedLessons = existingList.Select(r => new RecommendedLessonDto
                {
                    LessonId = r.LessonId,
                    Title = lessonMap.TryGetValue(r.LessonId, out var title) ? title : $"Lesson #{r.LessonId}",
                    PriorityScore = r.PriorityScore,
                    Reason = r.Reason
                }).ToList(),
                OverallReason = "Trả về danh sách gợi ý đã tạo từ trước (Idempotent)."
            };
        }

        // Begin transaction boundary
        await _recommendationRepo.BeginTransactionAsync();
        try
        {
            // 2. Resolve LearnerProfile with related collections
            var profiles = await _profileRepo.FindAsync(p => p.Id == request.LearnerProfileId);
            var profile = profiles.FirstOrDefault();
            if (profile == null)
            {
                throw new InvalidOperationException($"LearnerProfile with Id {request.LearnerProfileId} not found.");
            }

            // Load matrices & weakness histories to avoid EF lazy loading problems in testing
            var matrices = await _recommendationRepo.FindAsync(r => false); // dummy call to warm up if needed, but we query them directly:
            var skillMatricesList = (await _profileRepo.FindAsync(p => p.Id == profile.Id))
                .SelectMany(p => p.SkillMatrices).ToList();
            
            // 3. Expire old active recommendations
            var existingRecs = (await _recommendationRepo.FindAsync(r => r.LearnerProfileId == profile.Id)).ToList();
            foreach (var rec in existingRecs.Where(r => r.Status == RecommendationStatus.Active && r.ExpiresAt < DateTime.UtcNow))
            {
                var prevStatus = rec.Status;
                rec.Status = RecommendationStatus.Expired;
                rec.UpdatedAt = DateTime.UtcNow;
                await _recommendationRepo.UpdateAsync(rec);

                await _historyRepo.AddAsync(new RecommendationHistory
                {
                    RecommendationId = rec.Id,
                    LearnerProfileId = profile.Id,
                    LessonId = rec.LessonId,
                    SourceEventId = request.SourceEventId,
                    Action = RecommendationAction.Expired,
                    PreviousStatus = prevStatus,
                    NewStatus = RecommendationStatus.Expired,
                    Reason = "Gợi ý tự động hết hạn do quá thời gian hiệu lực",
                    RecordedAt = DateTime.UtcNow
                });
                _logger.LogInformation("Recommendation {RecId} for Lesson {LessonId} expired.", rec.Id, rec.LessonId);
            }

            // Refresh existing recommendations list after expiration
            existingRecs = (await _recommendationRepo.FindAsync(r => r.LearnerProfileId == profile.Id)).ToList();

            // 4. Load active/improving weaknesses
            var activeOrImprovingWeaknesses = (await _weaknessRepo.FindAsync(w =>
                w.LearnerProfileId == profile.Id &&
                (w.Status == WeaknessStatus.Active || w.Status == WeaknessStatus.Improving)
            )).ToList();

            // 5. Load completed lesson IDs
            var progress = await _progressRepo.FindAsync(p => p.LearnerProfileId == profile.Id && p.IsCompleted);
            var completedLessonIds = progress.Select(p => p.LessonId).ToHashSet();

            // 6. Identify blocked/accepted/dismissed recommendations
            var existingAcceptedLessonIds = existingRecs
                .Where(r => r.Status == RecommendationStatus.Accepted)
                .Select(r => r.LessonId)
                .ToHashSet();

            var dismissedLessonIdsWithCooldown = existingRecs
                .Where(r => r.Status == RecommendationStatus.Dismissed &&
                            r.DismissedAt.HasValue &&
                            r.DismissedAt.Value.AddDays(_options.DismissedCooldownDays) > DateTime.UtcNow)
                .Select(r => r.LessonId)
                .ToHashSet();

            // 7. Load all candidate lessons and filter them
            var allLessons = await _lessonRepo.GetAllAsync();
            var candidateLessons = new List<Lesson>();
            foreach (var lesson in allLessons)
            {
                if (lesson.Status != LessonStatus.Published)
                {
                    _logger.LogDebug("Lesson {LessonId} rejected: Trạng thái không phải Published.", lesson.Id);
                    continue;
                }
                if (completedLessonIds.Contains(lesson.Id))
                {
                    _logger.LogDebug("Lesson {LessonId} rejected: Bài học đã hoàn thành.", lesson.Id);
                    continue;
                }
                if (existingAcceptedLessonIds.Contains(lesson.Id))
                {
                    _logger.LogDebug("Lesson {LessonId} rejected: Đã có gợi ý được Chấp nhận (Accepted).", lesson.Id);
                    continue;
                }
                if (dismissedLessonIdsWithCooldown.Contains(lesson.Id))
                {
                    _logger.LogDebug("Lesson {LessonId} rejected: Đang trong thời gian cooldown sau khi Bỏ qua (Dismissed).", lesson.Id);
                    continue;
                }
                candidateLessons.Add(lesson);
            }

            // 8. Load repeated weaknesses
            var repeatedWeakTopics = activeOrImprovingWeaknesses
                .Where(w => w.OccurrenceCount >= 2)
                .Select(w => w.Topic)
                .ToList();

            // 9. Call ranking engine
            var rankedRecs = _engine.GenerateAndRank(
                candidateLessons,
                profile,
                activeOrImprovingWeaknesses,
                repeatedWeakTopics,
                request.WeakestSkill,
                request.WeakTopics,
                request.Level,
                request.SourceEventId
            );

            // Filter by minimum score
            var topRecs = rankedRecs
                .Where(r => r.PriorityScore >= _options.MinimumPriorityScore)
                .Take(_options.MaxRecommendations)
                .ToList();

            var finalRecommendations = new List<Recommendation>();
            var newTopLessonIds = topRecs.Select(r => r.LessonId).ToHashSet();

            // 10. Persist recommendations & log history
            foreach (var newRec in topRecs)
            {
                // Set expiration time
                newRec.ExpiresAt = DateTime.UtcNow.AddDays(_options.RecommendationExpirationDays);

                var existingRec = existingRecs.FirstOrDefault(r => r.LessonId == newRec.LessonId);
                if (existingRec != null)
                {
                    // Update/Regenerate existing recommendation
                    var prevStatus = existingRec.Status;
                    existingRec.PriorityScore = newRec.PriorityScore;
                    existingRec.Reason = newRec.Reason;
                    existingRec.SourceEventId = newRec.SourceEventId;
                    existingRec.GeneratedAt = DateTime.UtcNow;
                    existingRec.ExpiresAt = newRec.ExpiresAt;
                    existingRec.Status = RecommendationStatus.Active;
                    existingRec.UpdatedAt = DateTime.UtcNow;

                    await _recommendationRepo.UpdateAsync(existingRec);

                    await _historyRepo.AddAsync(new RecommendationHistory
                    {
                        RecommendationId = existingRec.Id,
                        LearnerProfileId = profile.Id,
                        LessonId = existingRec.LessonId,
                        SourceEventId = request.SourceEventId,
                        Action = RecommendationAction.Regenerated,
                        PreviousStatus = prevStatus,
                        NewStatus = RecommendationStatus.Active,
                        Reason = "Cập nhật và tái tạo lại gợi ý cũ",
                        RecordedAt = DateTime.UtcNow
                    });

                    finalRecommendations.Add(existingRec);
                }
                else
                {
                    // Create new recommendation
                    await _recommendationRepo.AddAsync(newRec);
                    await _recommendationRepo.SaveChangesAsync(); // save to generate PK ID for history record

                    await _historyRepo.AddAsync(new RecommendationHistory
                    {
                        RecommendationId = newRec.Id,
                        LearnerProfileId = profile.Id,
                        LessonId = newRec.LessonId,
                        SourceEventId = request.SourceEventId,
                        Action = RecommendationAction.Generated,
                        PreviousStatus = null,
                        NewStatus = RecommendationStatus.Active,
                        Reason = "Tạo mới gợi ý học tập thích ứng",
                        RecordedAt = DateTime.UtcNow
                    });

                    finalRecommendations.Add(newRec);
                }
            }

            // 11. Transition old Active recommendations (that are not in the new top list) to Replaced
            var activeRecsToReplace = existingRecs
                .Where(r => r.Status == RecommendationStatus.Active && !newTopLessonIds.Contains(r.LessonId));

            foreach (var oldActive in activeRecsToReplace)
            {
                var prevStatus = oldActive.Status;
                oldActive.Status = RecommendationStatus.Replaced;
                oldActive.UpdatedAt = DateTime.UtcNow;

                await _recommendationRepo.UpdateAsync(oldActive);

                await _historyRepo.AddAsync(new RecommendationHistory
                {
                    RecommendationId = oldActive.Id,
                    LearnerProfileId = profile.Id,
                    LessonId = oldActive.LessonId,
                    SourceEventId = request.SourceEventId,
                    Action = RecommendationAction.Replaced,
                    PreviousStatus = prevStatus,
                    NewStatus = RecommendationStatus.Replaced,
                    Reason = "Bị thay thế bởi danh sách gợi ý mới tối ưu hơn",
                    RecordedAt = DateTime.UtcNow
                });
            }

            await _recommendationRepo.SaveChangesAsync();
            await _recommendationRepo.CommitTransactionAsync();

            _logger.LogInformation("GenerateRecommendationsAsync completed successfully. EventId: {EventId}, Generated count: {Count}",
                request.SourceEventId, finalRecommendations.Count);

            return new RecommendationResponse
            {
                UserId = request.UserId,
                WeakestSkill = request.WeakestSkill?.ToString() ?? string.Empty,
                WeakTopics = request.WeakTopics,
                RecommendedLessons = finalRecommendations.Select(r => new RecommendedLessonDto
                {
                    LessonId = r.LessonId,
                    Title = r.Lesson?.Title ?? $"Lesson #{r.LessonId}",
                    PriorityScore = r.PriorityScore,
                    Reason = r.Reason
                }).ToList(),
                OverallReason = $"Hệ thống đã đề xuất {finalRecommendations.Count} bài học phù hợp nhất."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GenerateRecommendationsAsync failed. Rolling back transaction. EventId: {EventId}", request.SourceEventId);
            await _recommendationRepo.RollbackTransactionAsync();
            throw;
        }
    }

    public async Task HandleLessonCompletedAsync(int learnerProfileId, int lessonId, string sourceEventId)
    {
        _logger.LogInformation("HandleLessonCompletedAsync started. LearnerProfileId: {LearnerId}, LessonId: {LessonId}, SourceEventId: {SourceEventId}",
            learnerProfileId, lessonId, sourceEventId);

        // 1. Idempotency Check for Lesson Completion
        var existingHistory = await _historyRepo.FindAsync(h =>
            h.SourceEventId == sourceEventId &&
            h.Action == RecommendationAction.Completed &&
            h.LessonId == lessonId
        );
        if (existingHistory.Any())
        {
            _logger.LogWarning("Lesson completion for LessonId {LessonId} and EventId {EventId} already processed. Skipping (Idempotent success).", lessonId, sourceEventId);
            return;
        }

        await _recommendationRepo.BeginTransactionAsync();
        try
        {
            // Find Active or Accepted recommendation for this lesson
            var activeOrAccepted = await _recommendationRepo.FindAsync(r =>
                r.LearnerProfileId == learnerProfileId &&
                r.LessonId == lessonId &&
                (r.Status == RecommendationStatus.Active || r.Status == RecommendationStatus.Accepted)
            );
            var rec = activeOrAccepted.FirstOrDefault();

            if (rec == null)
            {
                _logger.LogInformation("No Active or Accepted recommendation found for LearnerProfileId {LearnerId} and LessonId {LessonId}. Just committing transaction.", learnerProfileId, lessonId);
                await _recommendationRepo.CommitTransactionAsync();
                return;
            }

            // Update status to Completed
            var prevStatus = rec.Status;
            rec.Status = RecommendationStatus.Completed;
            rec.CompletedAt = DateTime.UtcNow;
            rec.UpdatedAt = DateTime.UtcNow;

            await _recommendationRepo.UpdateAsync(rec);

            // Record history
            await _historyRepo.AddAsync(new RecommendationHistory
            {
                RecommendationId = rec.Id,
                LearnerProfileId = learnerProfileId,
                LessonId = lessonId,
                SourceEventId = sourceEventId,
                Action = RecommendationAction.Completed,
                PreviousStatus = prevStatus,
                NewStatus = RecommendationStatus.Completed,
                Reason = "Học viên hoàn thành bài học thành công",
                RecordedAt = DateTime.UtcNow
            });

            await _recommendationRepo.SaveChangesAsync();
            await _recommendationRepo.CommitTransactionAsync();

            _logger.LogInformation("Recommendation status updated to Completed. RecId: {RecId}, LessonId: {LessonId}", rec.Id, lessonId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HandleLessonCompletedAsync failed. Rolling back. EventId: {EventId}", sourceEventId);
            await _recommendationRepo.RollbackTransactionAsync();
            throw;
        }
    }
}
