using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Application.Interfaces;

namespace CoreLearningSystem.Application.Features.Dashboard;

public record DashboardStatsDto(
    int TotalUsers,
    int TotalLearners,
    int ActiveLearners,
    int TotalLessons,
    int TotalQuizzes,
    int TotalQuizAttempts,
    double PassRatePercentage,
    int PendingFeedbacksCount,
    int TotalGoals,
    int WeakLearnersCount
);

public record WeakLearnerDto(
    int LearnerId,
    int UserId,
    string Username,
    string Email,
    string FullName,
    double AverageScore,
    int QuizAttempts,
    string Level,
    string ActivityStatus
);

// ADMIN DASHBOARD STATS QUERY
public record GetDashboardStatsQuery() : IRequest<ApiResponse<DashboardStatsDto>>;

public class GetDashboardStatsQueryHandler : IRequestHandler<GetDashboardStatsQuery, ApiResponse<DashboardStatsDto>>
{
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<LearnerProfile> _learnerRepository;
    private readonly IRepository<Lesson> _lessonRepository;
    private readonly IRepository<Quiz> _quizRepository;
    private readonly IRepository<QuizAttempt> _attemptRepository;
    private readonly IRepository<Domain.Entities.Feedback> _feedbackRepository;
    private readonly IRepository<GoalSetting> _goalRepository;

    public GetDashboardStatsQueryHandler(
        IRepository<User> userRepository,
        IRepository<LearnerProfile> learnerRepository,
        IRepository<Lesson> lessonRepository,
        IRepository<Quiz> quizRepository,
        IRepository<QuizAttempt> attemptRepository,
        IRepository<Domain.Entities.Feedback> feedbackRepository,
        IRepository<GoalSetting> goalRepository)
    {
        _userRepository = userRepository;
        _learnerRepository = learnerRepository;
        _lessonRepository = lessonRepository;
        _quizRepository = quizRepository;
        _attemptRepository = attemptRepository;
        _feedbackRepository = feedbackRepository;
        _goalRepository = goalRepository;
    }

    public async Task<ApiResponse<DashboardStatsDto>> Handle(GetDashboardStatsQuery request, CancellationToken cancellationToken)
    {
        var users = await _userRepository.GetAllAsync();
        var learners = await _learnerRepository.GetAllAsync();
        var lessons = await _lessonRepository.GetAllAsync();
        var quizzes = await _quizRepository.GetAllAsync();
        var attempts = await _attemptRepository.GetAllAsync();
        var feedbacks = await _feedbackRepository.GetAllAsync();
        var goals = await _goalRepository.GetAllAsync();

        int totalUsers = users.Count();
        int totalLearners = learners.Count();
        int activeLearners = learners.Count(l => l.ActivityStatus == Domain.Enums.ActivityStatus.Active);
        int totalLessons = lessons.Count();
        int totalQuizzes = quizzes.Count();
        int totalAttempts = attempts.Count();

        int passed = attempts.Count(a => a.IsPassed);
        double passRate = totalAttempts > 0 ? ((double)passed / totalAttempts) * 100.0 : 0.0;

        int pendingFeedbacks = feedbacks.Count(fb => fb.ReviewedByAdminId == null);
        int totalGoals = goals.Count();

        // Weak learners: avg score < 50 (at least 1 attempt)
        int weakLearnersCount = attempts
            .GroupBy(a => a.LearnerProfileId)
            .Select(g => new { AvgScore = g.Average(a => a.Score) })
            .Count(x => x.AvgScore < 50);

        var dto = new DashboardStatsDto(
            totalUsers,
            totalLearners,
            activeLearners,
            totalLessons,
            totalQuizzes,
            totalAttempts,
            Math.Round(passRate, 2),
            pendingFeedbacks,
            totalGoals,
            weakLearnersCount
        );

        return ApiResponse<DashboardStatsDto>.SuccessResponse(dto);
    }
}

// GET WEAK LEARNERS QUERY
public record GetWeakLearnersQuery() : IRequest<ApiResponse<System.Collections.Generic.IEnumerable<WeakLearnerDto>>>;

public class GetWeakLearnersQueryHandler : IRequestHandler<GetWeakLearnersQuery, ApiResponse<System.Collections.Generic.IEnumerable<WeakLearnerDto>>>
{
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<LearnerProfile> _learnerRepository;
    private readonly IRepository<QuizAttempt> _attemptRepository;

    public GetWeakLearnersQueryHandler(
        IRepository<User> userRepository,
        IRepository<LearnerProfile> learnerRepository,
        IRepository<QuizAttempt> attemptRepository)
    {
        _userRepository = userRepository;
        _learnerRepository = learnerRepository;
        _attemptRepository = attemptRepository;
    }

    public async Task<ApiResponse<System.Collections.Generic.IEnumerable<WeakLearnerDto>>> Handle(GetWeakLearnersQuery request, CancellationToken cancellationToken)
    {
        var users = await _userRepository.GetAllAsync();
        var learners = await _learnerRepository.GetAllAsync();
        var attempts = await _attemptRepository.GetAllAsync();

        var learnerDict = learners.ToDictionary(l => l.Id);
        var userDict = users.ToDictionary(u => u.Id);

        var weakLearners = attempts
            .GroupBy(a => a.LearnerProfileId)
            .Select(g => new { LearnerId = g.Key, AvgScore = g.Average(a => a.Score), Count = g.Count() })
            .Where(x => x.AvgScore < 50)
            .Select(x =>
            {
                if (!learnerDict.TryGetValue(x.LearnerId, out var profile)) return null;
                if (!userDict.TryGetValue(profile.UserId, out var user)) return null;

                var activityStatus = profile.ActivityStatus == Domain.Enums.ActivityStatus.Active ? "Tích cực" : "Ít hoạt động";
                var level = profile.Level == Domain.Enums.EnglishLevel.None ? "Chưa xác định" : profile.Level.ToString();

                return new WeakLearnerDto(
                    profile.Id, user.Id, user.Username, user.Email, user.FullName,
                    Math.Round(x.AvgScore, 1), x.Count, level, activityStatus
                );
            })
            .Where(d => d != null)
            .OrderBy(d => d!.AverageScore)
            .ToList();

        return ApiResponse<System.Collections.Generic.IEnumerable<WeakLearnerDto>>.SuccessResponse(weakLearners!);
    }
}
