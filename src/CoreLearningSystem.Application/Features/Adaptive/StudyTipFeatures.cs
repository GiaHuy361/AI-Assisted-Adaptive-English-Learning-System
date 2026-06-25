using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Application.Interfaces;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;

namespace CoreLearningSystem.Application.Features.Adaptive;

// ─────────────────────────────────────────────────────────────
// Query
// ─────────────────────────────────────────────────────────────

/// <summary>
/// Query to generate a rule-based study tip for a learner.
/// Resolved from: Skill Matrix → Weakness → Learning Path → Goal → fallback.
/// No external AI API is called.
/// </summary>
public record GetStudyTipQuery(int UserId) : IRequest<ApiResponse<StudyTipDto>>;

// ─────────────────────────────────────────────────────────────
// Handler
// ─────────────────────────────────────────────────────────────

public class GetStudyTipQueryHandler : IRequestHandler<GetStudyTipQuery, ApiResponse<StudyTipDto>>
{
    private readonly IRepository<LearnerProfile> _profileRepo;
    private readonly IRepository<SkillMatrix> _skillMatrixRepo;
    private readonly IRepository<LearnerWeaknessHistory> _weaknessRepo;
    private readonly IRepository<Recommendation> _recommendationRepo;
    private readonly IRepository<Lesson> _lessonRepo;
    private readonly IRepository<GoalSetting> _goalRepo;
    private readonly ICacheService _cache;
    private readonly ICacheKeyBuilder _keys;

    private static readonly TimeSpan TipCacheTtl = TimeSpan.FromMinutes(10);

    public GetStudyTipQueryHandler(
        IRepository<LearnerProfile> profileRepo,
        IRepository<SkillMatrix> skillMatrixRepo,
        IRepository<LearnerWeaknessHistory> weaknessRepo,
        IRepository<Recommendation> recommendationRepo,
        IRepository<Lesson> lessonRepo,
        IRepository<GoalSetting> goalRepo,
        ICacheService cache,
        ICacheKeyBuilder keys)
    {
        _profileRepo = profileRepo;
        _skillMatrixRepo = skillMatrixRepo;
        _weaknessRepo = weaknessRepo;
        _recommendationRepo = recommendationRepo;
        _lessonRepo = lessonRepo;
        _goalRepo = goalRepo;
        _cache = cache;
        _keys = keys;
    }

    public async Task<ApiResponse<StudyTipDto>> Handle(GetStudyTipQuery request, CancellationToken cancellationToken)
    {
        // ── 1. Resolve learner profile ────────────────────────────────────
        var profiles = await _profileRepo.FindAsync(p => p.UserId == request.UserId);
        var profile = profiles.FirstOrDefault();

        if (profile == null)
            return ApiResponse<StudyTipDto>.SuccessResponse(BuildFallbackTip(0));

        var learnerId = profile.Id;

        // ── 2. Cache lookup ───────────────────────────────────────────────
        var cacheKey = _keys.StudyTip(learnerId);
        var cached = await _cache.GetAsync<StudyTipDto>(cacheKey, cancellationToken);
        if (cached != null)
            return ApiResponse<StudyTipDto>.SuccessResponse(cached);

        // ── 3. Generate tip ───────────────────────────────────────────────
        var tip = await GenerateTipAsync(learnerId, cancellationToken);

        // ── 4. Cache the result ───────────────────────────────────────────
        await _cache.SetAsync(cacheKey, tip, TipCacheTtl, cancellationToken);

        return ApiResponse<StudyTipDto>.SuccessResponse(tip);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Rule engine – priority order:
    //   1. Skill Matrix lowest score → weak skill
    //   2. Weakness history → weak topic within that skill
    //   3. Active recommendation matching weak skill → recommended lesson
    //   4. Goal near completion → goal-based tip
    //   5. Fallback
    // ─────────────────────────────────────────────────────────────────────
    private async Task<StudyTipDto> GenerateTipAsync(int learnerId, CancellationToken ct)
    {
        // 3.1 – Skill Matrix: find weakest skill
        var matrices = (await _skillMatrixRepo.FindAsync(m => m.LearnerProfileId == learnerId)).ToList();

        SkillMatrix? weakestMatrix = null;
        if (matrices.Count > 0)
            weakestMatrix = matrices.OrderBy(m => m.CurrentScore).First();

        string? weakSkill = weakestMatrix?.Skill.ToString();
        string? weakTopic = null;

        // 3.2 – Weakness history: find most repeated weak topic for that skill
        if (weakestMatrix != null)
        {
            var weaknesses = await _weaknessRepo.FindAsync(w =>
                w.LearnerProfileId == learnerId &&
                w.Skill == weakestMatrix.Skill &&
                w.Status == WeaknessStatus.Active);

            var topWeakness = weaknesses
                .OrderByDescending(w => w.OccurrenceCount)
                .FirstOrDefault();

            weakTopic = topWeakness?.Topic;
        }

        // 3.3 – Active recommendations: find top lesson matching weak skill
        var recommendedLessonIds = new List<int>();
        Lesson? recommendedLesson = null;

        if (weakestMatrix != null)
        {
            var recommendations = await _recommendationRepo.FindAsync(r =>
                r.LearnerProfileId == learnerId &&
                r.Status == RecommendationStatus.Active &&
                r.Skill == weakestMatrix.Skill);

            var topRec = recommendations.OrderByDescending(r => r.PriorityScore).FirstOrDefault();
            if (topRec != null)
            {
                recommendedLessonIds.Add(topRec.LessonId);
                var lessons = await _lessonRepo.FindAsync(l => l.Id == topRec.LessonId);
                recommendedLesson = lessons.FirstOrDefault();
            }
        }

        // 3.4 – Build tip text based on available data
        if (weakSkill != null && recommendedLesson != null)
        {
            var tip = BuildWeakSkillWithLessonTip(learnerId, weakSkill, weakTopic, recommendedLesson, recommendedLessonIds);
            return tip;
        }

        if (weakSkill != null)
        {
            return BuildWeakSkillNoLessonTip(learnerId, weakSkill, weakTopic, recommendedLessonIds);
        }

        // 3.5 – No skill matrix yet: check if goal is close to completion
        var goals = await _goalRepo.FindAsync(g =>
            g.LearnerProfileId == learnerId &&
            !g.IsCompleted &&
            g.Deadline > DateTime.UtcNow);

        var nearGoal = goals
            .Where(g => g.ProgressPercentage >= 70 && g.ProgressPercentage < 100)
            .OrderByDescending(g => g.ProgressPercentage)
            .FirstOrDefault();

        if (nearGoal != null)
        {
            var remaining = (int)Math.Ceiling((100 - nearGoal.ProgressPercentage) / 10.0);
            return new StudyTipDto
            {
                LearnerId = learnerId,
                TipText = $"Bạn sắp hoàn thành mục tiêu \"{nearGoal.Target}\". Hãy học thêm {remaining} bài để đạt mục tiêu.",
                WeakSkill = null,
                WeakTopic = null,
                RecommendedAction = "Continue learning",
                RecommendedLessonIds = recommendedLessonIds,
                GeneratedAt = DateTime.UtcNow
            };
        }

        // 3.6 – Full fallback
        return BuildFallbackTip(learnerId);
    }

    private static StudyTipDto BuildWeakSkillWithLessonTip(
        int learnerId, string weakSkill, string? weakTopic, Lesson lesson, List<int> lessonIds)
    {
        var topicPart = weakTopic != null ? $" (chủ đề: {weakTopic})" : string.Empty;
        return new StudyTipDto
        {
            LearnerId = learnerId,
            TipText = $"Bạn đang yếu {weakSkill}{topicPart}. Hôm nay nên học bài \"{lesson.Title}\" để cải thiện kỹ năng này.",
            WeakSkill = weakSkill,
            WeakTopic = weakTopic,
            RecommendedAction = "Start recommended lesson",
            RecommendedLessonIds = lessonIds,
            GeneratedAt = DateTime.UtcNow
        };
    }

    private static StudyTipDto BuildWeakSkillNoLessonTip(
        int learnerId, string weakSkill, string? weakTopic, List<int> lessonIds)
    {
        var topicPart = weakTopic != null ? $" (chủ đề: {weakTopic})" : string.Empty;
        return new StudyTipDto
        {
            LearnerId = learnerId,
            TipText = $"Bạn đang yếu {weakSkill}{topicPart}. Hôm nay nên ôn lại các bài thuộc kỹ năng này trước khi làm quiz tiếp.",
            WeakSkill = weakSkill,
            WeakTopic = weakTopic,
            RecommendedAction = "Review weak skill lessons",
            RecommendedLessonIds = lessonIds,
            GeneratedAt = DateTime.UtcNow
        };
    }

    private static StudyTipDto BuildFallbackTip(int learnerId) => new()
    {
        LearnerId = learnerId,
        TipText = "Hôm nay bạn nên hoàn thành một bài học ngắn để hệ thống cập nhật gợi ý chính xác hơn.",
        WeakSkill = null,
        WeakTopic = null,
        RecommendedAction = "Complete one lesson",
        RecommendedLessonIds = new List<int>(),
        GeneratedAt = DateTime.UtcNow
    };
}
