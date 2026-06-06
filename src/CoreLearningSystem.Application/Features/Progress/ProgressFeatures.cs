using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Application.Interfaces;

namespace CoreLearningSystem.Application.Features.Progress;

public record ProgressSummaryDto(
    int LearnerId,
    int LessonsCompleted,
    int LessonsInProgress,
    double OverallCompletionRate,
    double AverageQuizScore,
    int QuizzesPassed
);

// READ SUMMARY
public record GetProgressSummaryQuery(int LearnerId) : IRequest<ApiResponse<ProgressSummaryDto>>;

public class GetProgressSummaryQueryHandler : IRequestHandler<GetProgressSummaryQuery, ApiResponse<ProgressSummaryDto>>
{
    private readonly IRepository<LearnerProgress> _progressRepository;
    private readonly IRepository<QuizAttempt> _attemptRepository;

    public GetProgressSummaryQueryHandler(
        IRepository<LearnerProgress> progressRepository, 
        IRepository<QuizAttempt> attemptRepository)
    {
        _progressRepository = progressRepository;
        _attemptRepository = attemptRepository;
    }

    public async Task<ApiResponse<ProgressSummaryDto>> Handle(GetProgressSummaryQuery request, CancellationToken cancellationToken)
    {
        var progresses = await _progressRepository.FindAsync(p => p.LearnerProfileId == request.LearnerId);
        var attempts = await _attemptRepository.FindAsync(a => a.LearnerProfileId == request.LearnerId);

        int completed = progresses.Count(p => p.IsCompleted);
        int totalLessonsAssigned = progresses.Count() > 0 ? progresses.Count() : 1;
        double completionRate = ((double)completed / totalLessonsAssigned) * 100.0;

        int quizzesPassed = attempts.Count(a => a.IsPassed);
        double avgScore = attempts.Any() ? attempts.Average(a => a.Score) : 0.0;

        var dto = new ProgressSummaryDto(
            request.LearnerId,
            completed,
            progresses.Count(p => !p.IsCompleted),
            Math.Round(completionRate, 2),
            Math.Round(avgScore, 2),
            quizzesPassed
        );

        return ApiResponse<ProgressSummaryDto>.SuccessResponse(dto);
    }
}

public record LessonHistoryDto(
    int LessonId,
    string LessonTitle,
    string Skill,
    string Level,
    DateTime CompletedAt
);

public record LearnerProgressDetailsDto(
    int UserId,
    int LearnerProfileId,
    int LessonsCompleted,
    int TotalLessons,
    double OverallCompletionRate,
    int QuizzesDone,
    int QuizzesPassed,
    double AverageQuizScore,
    List<CoreLearningSystem.Application.Features.Users.QuizAttemptHistoryDto> QuizHistory,
    List<CoreLearningSystem.Application.Features.Users.SkillProgressDto> SkillProgress,
    List<LessonHistoryDto> LessonHistory
);

public record GetLearnerProgressDetailsQuery(int UserId) : IRequest<ApiResponse<LearnerProgressDetailsDto>>;

public class GetLearnerProgressDetailsQueryHandler : IRequestHandler<GetLearnerProgressDetailsQuery, ApiResponse<LearnerProgressDetailsDto>>
{
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<LearnerProfile> _profileRepository;
    private readonly IRepository<Lesson> _lessonRepository;
    private readonly IRepository<LearnerProgress> _progressRepository;
    private readonly IRepository<QuizAttempt> _attemptRepository;
    private readonly IRepository<Quiz> _quizRepository;
    private readonly IRepository<QuizAttemptDetail> _detailRepository;
    private readonly IRepository<Question> _questionRepository;

    public GetLearnerProgressDetailsQueryHandler(
        IRepository<User> userRepository,
        IRepository<LearnerProfile> profileRepository,
        IRepository<Lesson> lessonRepository,
        IRepository<LearnerProgress> progressRepository,
        IRepository<QuizAttempt> attemptRepository,
        IRepository<Quiz> quizRepository,
        IRepository<QuizAttemptDetail> detailRepository,
        IRepository<Question> questionRepository)
    {
        _userRepository = userRepository;
        _profileRepository = profileRepository;
        _lessonRepository = lessonRepository;
        _progressRepository = progressRepository;
        _attemptRepository = attemptRepository;
        _quizRepository = quizRepository;
        _detailRepository = detailRepository;
        _questionRepository = questionRepository;
    }

    public async Task<ApiResponse<LearnerProgressDetailsDto>> Handle(GetLearnerProgressDetailsQuery request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(request.UserId);
        if (user == null) return ApiResponse<LearnerProgressDetailsDto>.FailureResponse("User not found.");

        var profiles = await _profileRepository.FindAsync(p => p.UserId == user.Id);
        var profile = profiles.FirstOrDefault();
        if (profile == null) return ApiResponse<LearnerProgressDetailsDto>.FailureResponse("Learner profile not found.");

        // Completed lessons
        var progresses = await _progressRepository.FindAsync(p => p.LearnerProfileId == profile.Id && p.IsCompleted);
        var completedCount = progresses.Count();

        // Published lessons
        var lessons = await _lessonRepository.FindAsync(l => l.Status == CoreLearningSystem.Domain.Enums.LessonStatus.Published);
        int totalLessons = lessons.Count();
        double completionRate = totalLessons > 0 ? ((double)completedCount / totalLessons) * 100.0 : 0.0;
        completionRate = Math.Round(Math.Min(completionRate, 100.0), 2);

        // Quiz attempts
        var attempts = await _attemptRepository.FindAsync(a => a.LearnerProfileId == profile.Id);
        int quizzesDone = attempts.Count();
        int quizzesPassed = attempts.Count(a => a.IsPassed);
        double avgScore = attempts.Any() ? attempts.Average(a => a.Score) : 0.0;
        avgScore = Math.Round(avgScore, 2);

        // Quiz history
        var quizzes = await _quizRepository.GetAllAsync();
        var quizDict = quizzes.ToDictionary(q => q.Id);
        var quizHistory = attempts.Select(a =>
        {
            var quizTitle = quizDict.TryGetValue(a.QuizId, out var q) ? q.Title : "Unknown Quiz";
            var quizDuration = q?.DurationMinutes ?? 0;
            var maxScore = q?.MaxScore ?? 10.0;
            return new CoreLearningSystem.Application.Features.Users.QuizAttemptHistoryDto(
                a.Id,
                a.QuizId,
                quizTitle,
                a.AttemptedAt,
                a.Score,
                maxScore,
                quizDuration,
                a.IsPassed
            );
        }).OrderByDescending(h => h.AttemptedAt).ToList();

        // Skill progress stats
        var details = await _detailRepository.GetAllAsync();
        var questions = await _questionRepository.GetAllAsync();
        var questionDict = questions.ToDictionary(qn => qn.Id);

        var attemptIds = attempts.Select(a => a.Id).ToHashSet();
        var userDetails = details.Where(d => attemptIds.Contains(d.QuizAttemptId)).ToList();

        var skillStats = userDetails
            .Select(d => questionDict.TryGetValue(d.QuestionId, out var qn) ? new { d.IsCorrect, qn.Skill } : null)
            .Where(x => x != null)
            .GroupBy(x => x!.Skill)
            .Select(g =>
            {
                var skillName = g.Key.ToString();
                var totalQuestions = g.Count();
                var correctQuestions = g.Count(x => x!.IsCorrect);
                var averageScorePercent = totalQuestions > 0 ? ((double)correctQuestions / totalQuestions) * 100.0 : 0.0;

                return new CoreLearningSystem.Application.Features.Users.SkillProgressDto(
                    skillName,
                    Math.Round(averageScorePercent, 1),
                    correctQuestions,
                    totalQuestions
                );
            }).ToList();

        // Lesson history
        var lessonDict = lessons.ToDictionary(l => l.Id);
        var lessonHistory = progresses.Select(p =>
        {
            var lesson = lessonDict.TryGetValue(p.LessonId, out var l) ? l : null;
            return new LessonHistoryDto(
                p.LessonId,
                lesson?.Title ?? "Unknown Lesson",
                lesson?.Skill.ToString() ?? "Unknown Skill",
                lesson?.Level.ToString() ?? "Unknown Level",
                p.CompletedAt ?? p.LastAccessedAt
            );
        }).OrderByDescending(lh => lh.CompletedAt).ToList();

        var result = new LearnerProgressDetailsDto(
            user.Id,
            profile.Id,
            completedCount,
            totalLessons,
            completionRate,
            quizzesDone,
            quizzesPassed,
            avgScore,
            quizHistory,
            skillStats,
            lessonHistory
        );

        return ApiResponse<LearnerProgressDetailsDto>.SuccessResponse(result);
    }
}

