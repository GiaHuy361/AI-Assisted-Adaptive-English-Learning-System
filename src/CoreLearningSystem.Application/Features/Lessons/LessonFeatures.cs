using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Application.DTOs.Events;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;
using CoreLearningSystem.Application.Interfaces;

namespace CoreLearningSystem.Application.Features.Lessons;

public record LessonDto(int Id, string Title, string Content, string Skill, string Topic, string Level, int DurationInMinutes, string Status, int? QuizId = null, string? QuizTitle = null, int? QuizDurationMinutes = null, double? QuizMaxScore = null);

// READ
public record GetLessonsQuery(
    SkillType? Skill, 
    string? Topic, 
    EnglishLevel? Level, 
    string? SearchTerm, 
    int? CurrentUserId = null, 
    string? CurrentUserRole = null) : IRequest<ApiResponse<IEnumerable<LessonDto>>>;

public class GetLessonsQueryHandler : IRequestHandler<GetLessonsQuery, ApiResponse<IEnumerable<LessonDto>>>
{
    private readonly IRepository<Lesson> _lessonRepository;
    private readonly IRepository<LearnerProfile> _profileRepository;

    public GetLessonsQueryHandler(IRepository<Lesson> lessonRepository, IRepository<LearnerProfile> profileRepository)
    {
        _lessonRepository = lessonRepository;
        _profileRepository = profileRepository;
    }

    public async Task<ApiResponse<IEnumerable<LessonDto>>> Handle(GetLessonsQuery request, CancellationToken cancellationToken)
    {
        var lessons = await _lessonRepository.GetAllAsync();

        if (request.CurrentUserRole == "Learner" && request.CurrentUserId.HasValue)
        {
            var profiles = await _profileRepository.FindAsync(p => p.UserId == request.CurrentUserId.Value);
            var currentProfile = profiles.FirstOrDefault();
            if (currentProfile != null)
            {
                lessons = lessons.Where(lesson => lesson.Level == currentProfile.Level && lesson.Status == LessonStatus.Published);
            }
        }
        else
        {
            if (request.Level.HasValue)
            {
                lessons = lessons.Where(l => l.Level == request.Level.Value);
            }
        }

        if (request.Skill.HasValue)
        {
            lessons = lessons.Where(l => l.Skill == request.Skill.Value);
        }

        if (!string.IsNullOrEmpty(request.Topic))
        {
            lessons = lessons.Where(l => l.Topic.Contains(request.Topic, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(request.SearchTerm))
        {
            lessons = lessons.Where(l => 
                l.Title.Contains(request.SearchTerm, StringComparison.OrdinalIgnoreCase) || 
                l.Topic.Contains(request.SearchTerm, StringComparison.OrdinalIgnoreCase));
        }

        var dtos = lessons.Select(l => new LessonDto(
            l.Id,
            l.Title,
            l.Content,
            l.Skill.ToString(),
            l.Topic,
            l.Level.ToString(),
            l.DurationInMinutes,
            l.Status.ToString(),
            l.QuizId
        ));

        return ApiResponse<IEnumerable<LessonDto>>.SuccessResponse(dtos);
    }
}

// CREATE
public record CreateLessonCommand(string Title, string Content, SkillType Skill, string Topic, EnglishLevel Level, int DurationInMinutes, LessonStatus Status, int? QuizId = null) : IRequest<ApiResponse<LessonDto>>;

public class CreateLessonCommandHandler : IRequestHandler<CreateLessonCommand, ApiResponse<LessonDto>>
{
    private readonly IRepository<Lesson> _lessonRepository;
    private readonly IRepository<Quiz> _quizRepository;

    public CreateLessonCommandHandler(IRepository<Lesson> lessonRepository, IRepository<Quiz> quizRepository)
    {
        _lessonRepository = lessonRepository;
        _quizRepository = quizRepository;
    }

    public async Task<ApiResponse<LessonDto>> Handle(CreateLessonCommand request, CancellationToken cancellationToken)
    {
        if (request.QuizId.HasValue)
        {
            var quiz = await _quizRepository.GetByIdAsync(request.QuizId.Value);
            if (quiz == null)
            {
                return ApiResponse<LessonDto>.FailureResponse("Quiz not found.");
            }

            if (quiz.Level != request.Level)
            {
                return ApiResponse<LessonDto>.FailureResponse($"Cấp độ của bộ đề thi ({quiz.Level}) phải trùng khớp với cấp độ của bài học ({request.Level}).");
            }
        }

        var lesson = new Lesson
        {
            Title = request.Title,
            Content = request.Content,
            Skill = request.Skill,
            Topic = request.Topic,
            Level = request.Level,
            DurationInMinutes = request.DurationInMinutes,
            Status = request.Status,
            QuizId = request.QuizId,
            CreatedAt = DateTime.UtcNow
        };

        await _lessonRepository.AddAsync(lesson);
        await _lessonRepository.SaveChangesAsync();

        var dto = new LessonDto(lesson.Id, lesson.Title, lesson.Content, lesson.Skill.ToString(), lesson.Topic, lesson.Level.ToString(), lesson.DurationInMinutes, lesson.Status.ToString(), lesson.QuizId);
        return ApiResponse<LessonDto>.SuccessResponse(dto, "Lesson created successfully.");
    }
}

// UPDATE
public record UpdateLessonCommand(int Id, string Title, string Content, SkillType Skill, string Topic, EnglishLevel Level, int DurationInMinutes, LessonStatus Status, int? QuizId = null) : IRequest<ApiResponse<LessonDto>>;

public class UpdateLessonCommandHandler : IRequestHandler<UpdateLessonCommand, ApiResponse<LessonDto>>
{
    private readonly IRepository<Lesson> _lessonRepository;
    private readonly IRepository<Quiz> _quizRepository;

    public UpdateLessonCommandHandler(IRepository<Lesson> lessonRepository, IRepository<Quiz> quizRepository)
    {
        _lessonRepository = lessonRepository;
        _quizRepository = quizRepository;
    }

    public async Task<ApiResponse<LessonDto>> Handle(UpdateLessonCommand request, CancellationToken cancellationToken)
    {
        var lesson = await _lessonRepository.GetByIdAsync(request.Id);
        if (lesson == null) return ApiResponse<LessonDto>.FailureResponse("Lesson not found.");

        if (request.QuizId.HasValue)
        {
            var quiz = await _quizRepository.GetByIdAsync(request.QuizId.Value);
            if (quiz == null)
            {
                return ApiResponse<LessonDto>.FailureResponse("Quiz not found.");
            }

            if (quiz.Level != request.Level)
            {
                return ApiResponse<LessonDto>.FailureResponse($"Cấp độ của bộ đề thi ({quiz.Level}) phải trùng khớp với cấp độ của bài học ({request.Level}).");
            }
        }

        lesson.Title = request.Title;
        lesson.Content = request.Content;
        lesson.Skill = request.Skill;
        lesson.Topic = request.Topic;
        lesson.Level = request.Level;
        lesson.DurationInMinutes = request.DurationInMinutes;
        lesson.Status = request.Status;
        lesson.QuizId = request.QuizId;
        lesson.UpdatedAt = DateTime.UtcNow;

        await _lessonRepository.UpdateAsync(lesson);
        await _lessonRepository.SaveChangesAsync();

        var dto = new LessonDto(lesson.Id, lesson.Title, lesson.Content, lesson.Skill.ToString(), lesson.Topic, lesson.Level.ToString(), lesson.DurationInMinutes, lesson.Status.ToString(), lesson.QuizId);
        return ApiResponse<LessonDto>.SuccessResponse(dto, "Lesson updated successfully.");
    }
}

// DELETE
public record DeleteLessonCommand(int Id) : IRequest<ApiResponse<bool>>;

public class DeleteLessonCommandHandler : IRequestHandler<DeleteLessonCommand, ApiResponse<bool>>
{
    private readonly IRepository<Lesson> _lessonRepository;

    public DeleteLessonCommandHandler(IRepository<Lesson> lessonRepository)
    {
        _lessonRepository = lessonRepository;
    }

    public async Task<ApiResponse<bool>> Handle(DeleteLessonCommand request, CancellationToken cancellationToken)
    {
        var lesson = await _lessonRepository.GetByIdAsync(request.Id);
        if (lesson == null) return ApiResponse<bool>.FailureResponse("Lesson not found.");

        await _lessonRepository.DeleteAsync(lesson);
        await _lessonRepository.SaveChangesAsync();

        return ApiResponse<bool>.SuccessResponse(true, "Lesson deleted successfully.");
    }
}

// GET BY ID
public record GetLessonByIdQuery(
    int Id, 
    int? CurrentUserId = null, 
    string? CurrentUserRole = null) : IRequest<ApiResponse<LessonDto>>;

public class GetLessonByIdQueryHandler : IRequestHandler<GetLessonByIdQuery, ApiResponse<LessonDto>>
{
    private readonly IRepository<Lesson> _lessonRepository;
    private readonly IRepository<Quiz> _quizRepository;
    private readonly IRepository<LearnerProfile> _profileRepository;

    public GetLessonByIdQueryHandler(
        IRepository<Lesson> lessonRepository, 
        IRepository<Quiz> quizRepository,
        IRepository<LearnerProfile> profileRepository)
    {
        _lessonRepository = lessonRepository;
        _quizRepository = quizRepository;
        _profileRepository = profileRepository;
    }

    public async Task<ApiResponse<LessonDto>> Handle(GetLessonByIdQuery request, CancellationToken cancellationToken)
    {
        var lessons = await _lessonRepository.FindAsync(l => l.Id == request.Id);
        var lesson = lessons.FirstOrDefault();

        if (lesson == null)
        {
            Console.WriteLine($"Lesson with ID {request.Id} not found in database.");
            return ApiResponse<LessonDto>.FailureResponse("Lesson not found", $"Bài học số {request.Id} không tồn tại trên hệ thống.");
        }

        // Adaptive Protection Check
        if (request.CurrentUserRole == "Learner" && request.CurrentUserId.HasValue)
        {
            var profiles = await _profileRepository.FindAsync(p => p.UserId == request.CurrentUserId.Value);
            var currentProfile = profiles.FirstOrDefault();
            if (currentProfile != null && lesson.Level > currentProfile.Level)
            {
                return ApiResponse<LessonDto>.FailureResponse("Forbidden", "Bạn không có quyền truy cập vào bài học thuộc cấp độ cao hơn trình độ hiện tại.");
            }
        }

        string? quizTitle = null;
        int? quizDurationMinutes = null;
        double? quizMaxScore = null;

        if (lesson.QuizId.HasValue)
        {
            var quiz = await _quizRepository.GetByIdAsync(lesson.QuizId.Value);
            if (quiz != null)
            {
                quizTitle = quiz.Title;
                quizDurationMinutes = quiz.DurationMinutes;
                quizMaxScore = quiz.MaxScore;
            }
        }

        var dto = new LessonDto(
            lesson.Id,
            lesson.Title,
            lesson.Content,
            lesson.Skill.ToString(),
            lesson.Topic,
            lesson.Level.ToString(),
            lesson.DurationInMinutes,
            lesson.Status.ToString(),
            lesson.QuizId,
            quizTitle,
            quizDurationMinutes,
            quizMaxScore
        );

        return ApiResponse<LessonDto>.SuccessResponse(dto);
    }
}

// MARK COMPLETE
public record CompleteLessonCommand(int UserId, int LessonId) : IRequest<ApiResponse<bool>>;

public class CompleteLessonCommandHandler : IRequestHandler<CompleteLessonCommand, ApiResponse<bool>>
{
    private readonly IRepository<Lesson> _lessonRepository;
    private readonly IRepository<LearnerProfile> _profileRepository;
    private readonly IRepository<LearnerProgress> _progressRepository;
    private readonly IKafkaPublisher _kafkaPublisher;

    public CompleteLessonCommandHandler(
        IRepository<Lesson> lessonRepository,
        IRepository<LearnerProfile> profileRepository,
        IRepository<LearnerProgress> progressRepository,
        IKafkaPublisher kafkaPublisher)
    {
        _lessonRepository = lessonRepository;
        _profileRepository = profileRepository;
        _progressRepository = progressRepository;
        _kafkaPublisher = kafkaPublisher;
    }

    public async Task<ApiResponse<bool>> Handle(CompleteLessonCommand request, CancellationToken cancellationToken)
    {
        var lesson = await _lessonRepository.GetByIdAsync(request.LessonId);
        if (lesson == null) return ApiResponse<bool>.FailureResponse("Lesson not found.");

        var profiles = await _profileRepository.FindAsync(p => p.UserId == request.UserId);
        var profile = profiles.FirstOrDefault();
        if (profile == null) return ApiResponse<bool>.FailureResponse("Learner profile not found for this user.");

        var progresses = await _progressRepository.FindAsync(p => p.LearnerProfileId == profile.Id && p.LessonId == request.LessonId);
        var progress = progresses.FirstOrDefault();

        if (progress != null)
        {
            if (progress.IsCompleted)
            {
                return ApiResponse<bool>.SuccessResponse(true, "Lesson is already marked as completed.");
            }

            progress.IsCompleted = true;
            progress.CompletedAt = DateTime.UtcNow;
            progress.LastAccessedAt = DateTime.UtcNow;

            await _progressRepository.UpdateAsync(progress);
        }
        else
        {
            progress = new LearnerProgress
            {
                LearnerProfileId = profile.Id,
                LessonId = request.LessonId,
                IsCompleted = true,
                CompletedAt = DateTime.UtcNow,
                LastAccessedAt = DateTime.UtcNow
            };

            await _progressRepository.AddAsync(progress);
        }

        await _progressRepository.SaveChangesAsync();

        // Fire event
        try
        {
            var ev = new LessonCompletedEvent(
                profile.Id,
                request.LessonId,
                lesson.Skill.ToString(),
                lesson.Topic,
                lesson.Level.ToString(),
                DateTime.UtcNow
            );
            await _kafkaPublisher.PublishLessonCompletedAsync(ev);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error publishing LessonCompletedEvent: {ex.Message}");
        }

        return ApiResponse<bool>.SuccessResponse(true, "Lesson marked as completed successfully.");
    }
}
