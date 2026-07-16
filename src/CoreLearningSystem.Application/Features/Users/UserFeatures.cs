using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;
using CoreLearningSystem.Application.Interfaces;
using CoreLearningSystem.Application.Features.Auth;

namespace CoreLearningSystem.Application.Features.Users;

public record LearnerProgressDto(string CurrentLevel, string ActivityStatus, double CompletedLessonsPercentage, double AverageTestScore);

public class UserDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsLocked { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginDate { get; set; }
    public string ActivityStatus { get; set; } = string.Empty;
    public LearnerProgressDto? LearnerProgress { get; set; }
    public string Level { get; set; } = "A1";

    public UserDto() { }

    public UserDto(int id, string username, string email, string fullName, string role, bool isLocked, DateTime createdAt, DateTime? lastLoginDate, string activityStatus, LearnerProgressDto? learnerProgress)
    {
        Id = id;
        Username = username;
        Email = email;
        FullName = fullName;
        Role = role;
        IsLocked = isLocked;
        CreatedAt = createdAt;
        LastLoginDate = lastLoginDate;
        ActivityStatus = activityStatus;
        LearnerProgress = learnerProgress;
    }
}

public record QuizAttemptHistoryDto(int AttemptId, int QuizId, string QuizTitle, DateTime AttemptedAt, double Score, double MaxScore, int DurationMinutes, bool IsPassed);

public record SkillProgressDto(string Skill, double AverageScore, int CorrectAnswersCount, int TotalQuestionsCount);

public record UserDetailExpandedDto(
    int Id,
    string Username,
    string Email,
    string FullName,
    string Role,
    bool IsLocked,
    DateTime CreatedAt,
    List<QuizAttemptHistoryDto>? QuizHistory,
    List<SkillProgressDto>? SkillProgress
);

// READ ALL
public record GetUsersQuery() : IRequest<ApiResponse<IEnumerable<UserDto>>>;

public class GetUsersQueryHandler : IRequestHandler<GetUsersQuery, ApiResponse<IEnumerable<UserDto>>>
{
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<LearnerProfile> _profileRepository;
    private readonly IRepository<Lesson> _lessonRepository;
    private readonly IRepository<LearnerProgress> _progressRepository;
    private readonly IRepository<QuizAttempt> _attemptRepository;
    private readonly IRepository<PlacementTestResult> _placementResultRepository;

    public GetUsersQueryHandler(
        IRepository<User> userRepository,
        IRepository<LearnerProfile> profileRepository,
        IRepository<Lesson> lessonRepository,
        IRepository<LearnerProgress> progressRepository,
        IRepository<QuizAttempt> attemptRepository,
        IRepository<PlacementTestResult> placementResultRepository)
    {
        _userRepository = userRepository;
        _profileRepository = profileRepository;
        _lessonRepository = lessonRepository;
        _progressRepository = progressRepository;
        _attemptRepository = attemptRepository;
        _placementResultRepository = placementResultRepository;
    }

    public async Task<ApiResponse<IEnumerable<UserDto>>> Handle(GetUsersQuery request, CancellationToken cancellationToken)
    {
        var users = await _userRepository.GetAllAsync();
        var profiles = await _profileRepository.GetAllAsync();
        var lessons = await _lessonRepository.GetAllAsync();
        var progresses = await _progressRepository.GetAllAsync();
        var attempts = await _attemptRepository.GetAllAsync();
        var placementResults = await _placementResultRepository.GetAllAsync();

        var totalPublishedLessons = lessons.Count(l => l.Status == LessonStatus.Published);
        var profileDict = profiles.ToDictionary(p => p.UserId);
        foreach (var u in users)
        {
            if (profileDict.TryGetValue(u.Id, out var profile))
            {
                u.LearnerProfile = profile;
            }
        }
        var progressGroup = progresses.Where(p => p.IsCompleted).GroupBy(p => p.LearnerProfileId).ToDictionary(g => g.Key, g => g.Count());
        var attemptGroup = attempts.GroupBy(a => a.LearnerProfileId).ToDictionary(g => g.Key, g => g.ToList());
        var placementLearnerProfileIds = placementResults.Select(r => r.LearnerProfileId).ToHashSet();

        var dtos = users.Select(u =>
        {
            LearnerProgressDto? progressDto = null;
            var activityStatusText = "";

            if (u.Role == UserRole.Learner && profileDict.TryGetValue(u.Id, out var profile))
            {
                ActivityStatus activityStatus;
                if (!u.LastLoginDate.HasValue)
                {
                    activityStatus = ActivityStatus.Inactive;
                }
                else
                {
                    var lastLoginUtc = DateTime.SpecifyKind(u.LastLoginDate.Value, DateTimeKind.Utc);
                    var daysDiff = (DateTime.UtcNow - lastLoginUtc).TotalDays;
                    activityStatus = daysDiff > 7 ? ActivityStatus.Inactive : ActivityStatus.Active;
                }

                profile.ActivityStatus = activityStatus;
                activityStatusText = profile.ActivityStatus == ActivityStatus.Active ? "Tích cực" : "Ít hoạt động";

                var completedCount = progressGroup.TryGetValue(profile.Id, out var count) ? count : 0;
                var percentage = totalPublishedLessons > 0 ? ((double)completedCount / totalPublishedLessons) * 100 : 0.0;
                percentage = Math.Min(percentage, 100.0);

                var averageScore = attemptGroup.TryGetValue(profile.Id, out var userAttempts) && userAttempts.Any()
                    ? userAttempts.Average(a => a.Score)
                    : 0.0;

                var currentLevelStr = profile.Level == EnglishLevel.None ? "Chưa làm bài đánh giá" : profile.Level.ToString();

                progressDto = new LearnerProgressDto(
                    currentLevelStr,
                    activityStatusText,
                    Math.Round(percentage, 1),
                    Math.Round(averageScore, 2)
                );
            }

            var dto = new UserDto(
                u.Id,
                u.Username,
                u.Email,
                u.FullName,
                u.Role.ToString(),
                u.IsLocked,
                u.CreatedAt,
                u.LastLoginDate,
                activityStatusText,
                progressDto
            );
            var hasTakenPlacement = u.LearnerProfile != null && placementLearnerProfileIds.Contains(u.LearnerProfile.Id);
            dto.Level = u.LearnerProfile != null 
                ? ((hasTakenPlacement && u.LearnerProfile.Level != EnglishLevel.None) ? u.LearnerProfile.Level.ToString() : "Chưa làm bài đánh giá") 
                : "Chưa làm bài đánh giá";
            return dto;
        }).ToList();

        // Strict manual loop override right before returning
        foreach (var userDto in dtos)
        {
            if (userDto.Role == "Learner")
            {
                string statusText;
                if (!userDto.LastLoginDate.HasValue)
                {
                    statusText = "Ít hoạt động";
                }
                else
                {
                    var lastLoginUtc = DateTime.SpecifyKind(userDto.LastLoginDate.Value, DateTimeKind.Utc);
                    var daysDiff = (DateTime.UtcNow - lastLoginUtc).TotalDays;
                    statusText = daysDiff > 7 ? "Ít hoạt động" : "Tích cực";
                }
                userDto.ActivityStatus = statusText;
                if (userDto.LearnerProgress != null)
                {
                    userDto.LearnerProgress = userDto.LearnerProgress with { ActivityStatus = statusText };
                }
            }
        }

        return ApiResponse<IEnumerable<UserDto>>.SuccessResponse(dtos);
    }
}

// EXPANDED DETAILS
public record GetUserDetailsExpandedQuery(int UserId) : IRequest<ApiResponse<UserDetailExpandedDto>>;

public class GetUserDetailsExpandedQueryHandler : IRequestHandler<GetUserDetailsExpandedQuery, ApiResponse<UserDetailExpandedDto>>
{
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<LearnerProfile> _profileRepository;
    private readonly IRepository<QuizAttempt> _attemptRepository;
    private readonly IRepository<Quiz> _quizRepository;
    private readonly IRepository<QuizAttemptDetail> _detailRepository;
    private readonly IRepository<Question> _questionRepository;

    public GetUserDetailsExpandedQueryHandler(
        IRepository<User> userRepository,
        IRepository<LearnerProfile> profileRepository,
        IRepository<QuizAttempt> attemptRepository,
        IRepository<Quiz> quizRepository,
        IRepository<QuizAttemptDetail> detailRepository,
        IRepository<Question> questionRepository)
    {
        _userRepository = userRepository;
        _profileRepository = profileRepository;
        _attemptRepository = attemptRepository;
        _quizRepository = quizRepository;
        _detailRepository = detailRepository;
        _questionRepository = questionRepository;
    }

    public async Task<ApiResponse<UserDetailExpandedDto>> Handle(GetUserDetailsExpandedQuery request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(request.UserId);
        if (user == null) return ApiResponse<UserDetailExpandedDto>.FailureResponse("User not found.");

        if (user.Role != UserRole.Learner)
        {
            var adminDto = new UserDetailExpandedDto(
                user.Id,
                user.Username,
                user.Email,
                user.FullName,
                user.Role.ToString(),
                user.IsLocked,
                user.CreatedAt,
                null,
                null
            );
            return ApiResponse<UserDetailExpandedDto>.SuccessResponse(adminDto);
        }

        var profiles = await _profileRepository.FindAsync(p => p.UserId == user.Id);
        var profile = profiles.FirstOrDefault();
        if (profile == null)
        {
            return ApiResponse<UserDetailExpandedDto>.FailureResponse("Learner profile not found for this user.");
        }

        var attempts = await _attemptRepository.FindAsync(a => a.LearnerProfileId == profile.Id);
        var quizzes = await _quizRepository.GetAllAsync();
        var quizDict = quizzes.ToDictionary(q => q.Id);

        var quizHistory = attempts.Select(a =>
        {
            var quizTitle = quizDict.TryGetValue(a.QuizId, out var q) ? q.Title : "Unknown Quiz";
            var quizDuration = q?.DurationMinutes ?? 0;
            var maxScore = q?.MaxScore ?? 10.0;
            return new QuizAttemptHistoryDto(
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

                return new SkillProgressDto(
                    skillName,
                    Math.Round(averageScorePercent, 1),
                    correctQuestions,
                    totalQuestions
                );
            }).ToList();

        var detailDto = new UserDetailExpandedDto(
            user.Id,
            user.Username,
            user.Email,
            user.FullName,
            user.Role.ToString(),
            user.IsLocked,
            user.CreatedAt,
            quizHistory,
            skillStats
        );

        return ApiResponse<UserDetailExpandedDto>.SuccessResponse(detailDto);
    }
}

// CREATE
public record CreateUserCommand(string Username, string Email, string Password, string FullName, UserRole Role) : IRequest<ApiResponse<UserDto>>;

public class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, ApiResponse<UserDto>>
{
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<LearnerProfile> _profileRepository;
    private readonly ISignalRService _signalRService;

    public CreateUserCommandHandler(IRepository<User> userRepository, IRepository<LearnerProfile> profileRepository, ISignalRService signalRService)
    {
        _userRepository = userRepository;
        _profileRepository = profileRepository;
        _signalRService = signalRService;
    }

    public async Task<ApiResponse<UserDto>> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        var existing = await _userRepository.FindAsync(u => u.Username == request.Username);
        if (existing.Any()) return ApiResponse<UserDto>.FailureResponse("Username already exists.");

        var user = new User
        {
            Username = request.Username,
            Email = request.Email,
            FullName = request.FullName,
            PasswordHash = BCryptMock.HashPassword(request.Password),
            Role = request.Role,
            CreatedAt = DateTime.UtcNow
        };

        await _userRepository.AddAsync(user);
        await _userRepository.SaveChangesAsync();

        if (request.Role == UserRole.Learner)
        {
            var profile = new LearnerProfile
            {
                UserId = user.Id,
                Level = EnglishLevel.None,
                ActivityStatus = ActivityStatus.Active,
                LastActiveAt = DateTime.UtcNow
            };
            await _profileRepository.AddAsync(profile);
            await _profileRepository.SaveChangesAsync();
            user.LearnerProfile = profile;
        }

        var progressDto = user.Role == UserRole.Learner ? new LearnerProgressDto("Chưa làm bài đánh giá", "Tích cực", 0.0, 0.0) : null;
        var activityStatusText = user.Role == UserRole.Learner ? "Tích cực" : "";
        var dto = new UserDto(user.Id, user.Username, user.Email, user.FullName, user.Role.ToString(), user.IsLocked, user.CreatedAt, user.LastLoginDate, activityStatusText, progressDto);
        dto.Level = user.Role == UserRole.Learner ? "Chưa làm bài đánh giá" : "A1";

        try
        {
            await _signalRService.SendCrudUpdateAsync("User", "Create", dto);
        }
        catch (Exception) { }

        return ApiResponse<UserDto>.SuccessResponse(dto, "User created successfully.");
    }
}

// UPDATE
public record UpdateUserCommand(int Id, string Email, string FullName, UserRole Role, bool IsLocked) : IRequest<ApiResponse<UserDto>>;

public class UpdateUserCommandHandler : IRequestHandler<UpdateUserCommand, ApiResponse<UserDto>>
{
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<LearnerProfile> _profileRepository;
    private readonly IRepository<PlacementTestResult> _placementResultRepository;
    private readonly ISignalRService _signalRService;

    public UpdateUserCommandHandler(
        IRepository<User> userRepository, 
        IRepository<LearnerProfile> profileRepository,
        IRepository<PlacementTestResult> placementResultRepository,
        ISignalRService signalRService)
    {
        _userRepository = userRepository;
        _profileRepository = profileRepository;
        _placementResultRepository = placementResultRepository;
        _signalRService = signalRService;
    }

    public async Task<ApiResponse<UserDto>> Handle(UpdateUserCommand request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(request.Id);
        if (user == null) return ApiResponse<UserDto>.FailureResponse("User not found.");

        var profiles = await _profileRepository.FindAsync(p => p.UserId == user.Id);
        user.LearnerProfile = profiles.FirstOrDefault();

        user.Email = request.Email;
        user.FullName = request.FullName;
        user.Role = request.Role;
        user.IsLocked = request.IsLocked;
        user.UpdatedAt = DateTime.UtcNow;

        await _userRepository.UpdateAsync(user);
        await _userRepository.SaveChangesAsync();

        var hasTaken = user.LearnerProfile != null && (await _placementResultRepository.FindAsync(pr => pr.LearnerProfileId == user.LearnerProfile.Id)).Any();
        var level = (user.LearnerProfile != null && hasTaken && user.LearnerProfile.Level != EnglishLevel.None) 
            ? user.LearnerProfile.Level.ToString() 
            : "Chưa làm bài đánh giá";

        var dto = new UserDto(user.Id, user.Username, user.Email, user.FullName, user.Role.ToString(), user.IsLocked, user.CreatedAt, user.LastLoginDate, "", null);
        dto.Level = level;

        try
        {
            await _signalRService.SendCrudUpdateAsync("User", "Update", dto);
        }
        catch (Exception) { }

        return ApiResponse<UserDto>.SuccessResponse(dto, "User updated successfully.");
    }
}

// DELETE
public record DeleteUserCommand(int Id) : IRequest<ApiResponse<bool>>;

public class DeleteUserCommandHandler : IRequestHandler<DeleteUserCommand, ApiResponse<bool>>
{
    private readonly IRepository<User> _userRepository;
    private readonly ISignalRService _signalRService;

    public DeleteUserCommandHandler(IRepository<User> userRepository, ISignalRService signalRService)
    {
        _userRepository = userRepository;
        _signalRService = signalRService;
    }

    public async Task<ApiResponse<bool>> Handle(DeleteUserCommand request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(request.Id);
        if (user == null) return ApiResponse<bool>.FailureResponse("User not found.");

        await _userRepository.DeleteAsync(user);
        await _userRepository.SaveChangesAsync();

        try
        {
            await _signalRService.SendCrudUpdateAsync("User", "Delete", new { Id = request.Id });
        }
        catch (Exception) { }

        return ApiResponse<bool>.SuccessResponse(true, "User deleted successfully.");
    }
}
