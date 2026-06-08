using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Application.DTOs.Events;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Application.Interfaces;

namespace CoreLearningSystem.Application.Features.Feedback;

public record FeedbackDto(
    int Id,
    int LearnerId,
    string Username,
    string Subject,
    string Content,
    int Rating,
    string Status,
    string SubmittedAt,
    int? ReviewedByAdminId,
    string? ReviewComment,
    string? ReviewedAt
);

// SUBMIT
public record SubmitFeedbackCommand(int UserId, string Subject, string Content, int Rating) : IRequest<ApiResponse<FeedbackDto>>;

public class SubmitFeedbackCommandHandler : IRequestHandler<SubmitFeedbackCommand, ApiResponse<FeedbackDto>>
{
    private readonly IRepository<Domain.Entities.Feedback> _feedbackRepository;
    private readonly IRepository<LearnerProfile> _profileRepository;
    private readonly IKafkaPublisher _kafkaPublisher;

    public SubmitFeedbackCommandHandler(
        IRepository<Domain.Entities.Feedback> feedbackRepository,
        IRepository<LearnerProfile> profileRepository,
        IKafkaPublisher kafkaPublisher)
    {
        _feedbackRepository = feedbackRepository;
        _profileRepository = profileRepository;
        _kafkaPublisher = kafkaPublisher;
    }

    public async Task<ApiResponse<FeedbackDto>> Handle(SubmitFeedbackCommand request, CancellationToken cancellationToken)
    {
        var profiles = await _profileRepository.FindAsync(p => p.UserId == request.UserId);
        var profile = profiles.FirstOrDefault();
        if (profile == null) return ApiResponse<FeedbackDto>.FailureResponse("Learner profile not found.");

        var fb = new Domain.Entities.Feedback
        {
            LearnerProfileId = profile.Id,
            Subject = request.Subject,
            Content = request.Content,
            Rating = request.Rating,
            CreatedAt = DateTime.UtcNow
        };

        await _feedbackRepository.AddAsync(fb);
        await _feedbackRepository.SaveChangesAsync();

        // Fire event (Partial/Blocked for TargetType/TargetId due to database schema limitations)
        try
        {
            var ev = new FeedbackSubmittedEvent(
                profile.Id,
                string.Empty, // Blocked target type
                null,         // Blocked target ID
                fb.Rating,
                fb.Content,
                DateTime.UtcNow
            );
            await _kafkaPublisher.PublishFeedbackSubmittedAsync(ev);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error publishing FeedbackSubmittedEvent: {ex.Message}");
        }

        var dto = new FeedbackDto(
            fb.Id, 
            fb.LearnerProfileId, 
            "", 
            fb.Subject, 
            fb.Content, 
            fb.Rating, 
            "Pending", 
            fb.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"), 
            null, 
            null, 
            null
        );
        return ApiResponse<FeedbackDto>.SuccessResponse(dto, "Feedback submitted successfully.");
    }
}

// READ ALL
public record GetFeedbacksQuery() : IRequest<ApiResponse<IEnumerable<FeedbackDto>>>;

public class GetFeedbacksQueryHandler : IRequestHandler<GetFeedbacksQuery, ApiResponse<IEnumerable<FeedbackDto>>>
{
    private readonly IRepository<Domain.Entities.Feedback> _feedbackRepository;
    private readonly IRepository<LearnerProfile> _profileRepository;
    private readonly IRepository<User> _userRepository;

    public GetFeedbacksQueryHandler(
        IRepository<Domain.Entities.Feedback> feedbackRepository,
        IRepository<LearnerProfile> profileRepository,
        IRepository<User> userRepository)
    {
        _feedbackRepository = feedbackRepository;
        _profileRepository = profileRepository;
        _userRepository = userRepository;
    }

    public async Task<ApiResponse<IEnumerable<FeedbackDto>>> Handle(GetFeedbacksQuery request, CancellationToken cancellationToken)
    {
        var fbs = await _feedbackRepository.GetAllAsync();
        var profiles = await _profileRepository.GetAllAsync();
        var users = await _userRepository.GetAllAsync();

        var profileDict = profiles.ToDictionary(p => p.Id);
        var userDict = users.ToDictionary(u => u.Id);

        var dtos = fbs.Select(fb =>
        {
            string username = "Unknown";
            if (profileDict.TryGetValue(fb.LearnerProfileId, out var profile) && userDict.TryGetValue(profile.UserId, out var user))
            {
                username = user.Username;
            }

            return new FeedbackDto(
                fb.Id,
                fb.LearnerProfileId,
                username,
                fb.Subject,
                fb.Content,
                fb.Rating,
                fb.ReviewedByAdminId == null ? "Pending" : "Resolved",
                fb.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                fb.ReviewedByAdminId,
                fb.ReviewComment,
                fb.ReviewedAt?.ToString("yyyy-MM-dd HH:mm:ss")
            );
        }).OrderByDescending(f => f.SubmittedAt).ToList();

        return ApiResponse<IEnumerable<FeedbackDto>>.SuccessResponse(dtos);
    }
}

// READ MY
public record GetMyFeedbacksQuery(int UserId) : IRequest<ApiResponse<IEnumerable<FeedbackDto>>>;

public class GetMyFeedbacksQueryHandler : IRequestHandler<GetMyFeedbacksQuery, ApiResponse<IEnumerable<FeedbackDto>>>
{
    private readonly IRepository<Domain.Entities.Feedback> _feedbackRepository;
    private readonly IRepository<LearnerProfile> _profileRepository;
    private readonly IRepository<User> _userRepository;

    public GetMyFeedbacksQueryHandler(
        IRepository<Domain.Entities.Feedback> feedbackRepository,
        IRepository<LearnerProfile> profileRepository,
        IRepository<User> userRepository)
    {
        _feedbackRepository = feedbackRepository;
        _profileRepository = profileRepository;
        _userRepository = userRepository;
    }

    public async Task<ApiResponse<IEnumerable<FeedbackDto>>> Handle(GetMyFeedbacksQuery request, CancellationToken cancellationToken)
    {
        var profiles = await _profileRepository.FindAsync(p => p.UserId == request.UserId);
        var profile = profiles.FirstOrDefault();
        if (profile == null) return ApiResponse<IEnumerable<FeedbackDto>>.FailureResponse("Learner profile not found.");

        var fbs = await _feedbackRepository.FindAsync(fb => fb.LearnerProfileId == profile.Id);
        var user = await _userRepository.GetByIdAsync(request.UserId);
        string username = user?.Username ?? "Unknown";

        var dtos = fbs.Select(fb => new FeedbackDto(
            fb.Id,
            fb.LearnerProfileId,
            username,
            fb.Subject,
            fb.Content,
            fb.Rating,
            fb.ReviewedByAdminId == null ? "Pending" : "Resolved",
            fb.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
            fb.ReviewedByAdminId,
            fb.ReviewComment,
            fb.ReviewedAt?.ToString("yyyy-MM-dd HH:mm:ss")
        )).OrderByDescending(f => f.SubmittedAt).ToList();

        return ApiResponse<IEnumerable<FeedbackDto>>.SuccessResponse(dtos);
    }
}

// ADMIN REVIEW
public record ReviewFeedbackCommand(int FeedbackId, int AdminId, string Comment) : IRequest<ApiResponse<FeedbackDto>>;

public class ReviewFeedbackCommandHandler : IRequestHandler<ReviewFeedbackCommand, ApiResponse<FeedbackDto>>
{
    private readonly IRepository<Domain.Entities.Feedback> _feedbackRepository;

    public ReviewFeedbackCommandHandler(IRepository<Domain.Entities.Feedback> feedbackRepository)
    {
        _feedbackRepository = feedbackRepository;
    }

    public async Task<ApiResponse<FeedbackDto>> Handle(ReviewFeedbackCommand request, CancellationToken cancellationToken)
    {
        var fb = await _feedbackRepository.GetByIdAsync(request.FeedbackId);
        if (fb == null) return ApiResponse<FeedbackDto>.FailureResponse("Feedback not found.");

        fb.ReviewedByAdminId = request.AdminId;
        fb.ReviewComment = request.Comment;
        fb.ReviewedAt = DateTime.UtcNow;

        await _feedbackRepository.UpdateAsync(fb);
        await _feedbackRepository.SaveChangesAsync();

        var dto = new FeedbackDto(
            fb.Id,
            fb.LearnerProfileId,
            "",
            fb.Subject,
            fb.Content,
            fb.Rating,
            "Resolved",
            fb.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
            fb.ReviewedByAdminId,
            fb.ReviewComment,
            fb.ReviewedAt?.ToString("yyyy-MM-dd HH:mm:ss")
        );
        return ApiResponse<FeedbackDto>.SuccessResponse(dto, "Feedback reviewed successfully.");
    }
}

// ADMIN RESOLVE
public record ResolveFeedbackCommand(int FeedbackId, int AdminId) : IRequest<ApiResponse<FeedbackDto>>;

public class ResolveFeedbackCommandHandler : IRequestHandler<ResolveFeedbackCommand, ApiResponse<FeedbackDto>>
{
    private readonly IRepository<Domain.Entities.Feedback> _feedbackRepository;

    public ResolveFeedbackCommandHandler(IRepository<Domain.Entities.Feedback> feedbackRepository)
    {
        _feedbackRepository = feedbackRepository;
    }

    public async Task<ApiResponse<FeedbackDto>> Handle(ResolveFeedbackCommand request, CancellationToken cancellationToken)
    {
        var fb = await _feedbackRepository.GetByIdAsync(request.FeedbackId);
        if (fb == null) return ApiResponse<FeedbackDto>.FailureResponse("Feedback not found.");

        fb.ReviewedByAdminId = request.AdminId;
        fb.ReviewComment = "Đã xử lý";
        fb.ReviewedAt = DateTime.UtcNow;

        await _feedbackRepository.UpdateAsync(fb);
        await _feedbackRepository.SaveChangesAsync();

        var dto = new FeedbackDto(
            fb.Id,
            fb.LearnerProfileId,
            "",
            fb.Subject,
            fb.Content,
            fb.Rating,
            "Resolved",
            fb.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
            fb.ReviewedByAdminId,
            fb.ReviewComment,
            fb.ReviewedAt?.ToString("yyyy-MM-dd HH:mm:ss")
        );
        return ApiResponse<FeedbackDto>.SuccessResponse(dto, "Feedback resolved successfully.");
    }
}

// ADMIN DELETE
public record DeleteFeedbackCommand(int FeedbackId) : IRequest<ApiResponse<bool>>;

public class DeleteFeedbackCommandHandler : IRequestHandler<DeleteFeedbackCommand, ApiResponse<bool>>
{
    private readonly IRepository<Domain.Entities.Feedback> _feedbackRepository;

    public DeleteFeedbackCommandHandler(IRepository<Domain.Entities.Feedback> feedbackRepository)
    {
        _feedbackRepository = feedbackRepository;
    }

    public async Task<ApiResponse<bool>> Handle(DeleteFeedbackCommand request, CancellationToken cancellationToken)
    {
        var fb = await _feedbackRepository.GetByIdAsync(request.FeedbackId);
        if (fb == null) return ApiResponse<bool>.FailureResponse("Feedback not found.");

        await _feedbackRepository.DeleteAsync(fb);
        await _feedbackRepository.SaveChangesAsync();

        return ApiResponse<bool>.SuccessResponse(true, "Feedback deleted successfully.");
    }
}
