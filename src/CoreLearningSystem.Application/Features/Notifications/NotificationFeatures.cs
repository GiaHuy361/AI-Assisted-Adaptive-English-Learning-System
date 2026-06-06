using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Application.Interfaces;

namespace CoreLearningSystem.Application.Features.Notifications;

public record NotificationDto(int Id, int UserId, string Title, string Message, bool IsRead, DateTime CreatedAt);

// GET USER NOTIFICATIONS
public record GetNotificationsQuery(int UserId) : IRequest<ApiResponse<IEnumerable<NotificationDto>>>;

public class GetNotificationsQueryHandler : IRequestHandler<GetNotificationsQuery, ApiResponse<IEnumerable<NotificationDto>>>
{
    private readonly IRepository<Notification> _notificationRepository;

    public GetNotificationsQueryHandler(IRepository<Notification> notificationRepository)
    {
        _notificationRepository = notificationRepository;
    }

    public async Task<ApiResponse<IEnumerable<NotificationDto>>> Handle(GetNotificationsQuery request, CancellationToken cancellationToken)
    {
        var notifications = await _notificationRepository.FindAsync(n => n.UserId == request.UserId);
        var dtos = notifications.Select(n => new NotificationDto(n.Id, n.UserId, n.Title, n.Message, n.IsRead, n.CreatedAt))
                                .OrderByDescending(n => n.CreatedAt);

        return ApiResponse<IEnumerable<NotificationDto>>.SuccessResponse(dtos);
    }
}

// MARK AS READ
public record MarkNotificationAsReadCommand(int NotificationId) : IRequest<ApiResponse<bool>>;

public class MarkNotificationAsReadCommandHandler : IRequestHandler<MarkNotificationAsReadCommand, ApiResponse<bool>>
{
    private readonly IRepository<Notification> _notificationRepository;

    public MarkNotificationAsReadCommandHandler(IRepository<Notification> notificationRepository)
    {
        _notificationRepository = notificationRepository;
    }

    public async Task<ApiResponse<bool>> Handle(MarkNotificationAsReadCommand request, CancellationToken cancellationToken)
    {
        var notification = await _notificationRepository.GetByIdAsync(request.NotificationId);
        if (notification == null) return ApiResponse<bool>.FailureResponse("Notification not found.");

        notification.IsRead = true;
        await _notificationRepository.UpdateAsync(notification);
        await _notificationRepository.SaveChangesAsync();

        return ApiResponse<bool>.SuccessResponse(true, "Notification marked as read.");
    }
}

// MARK ALL AS READ
public record MarkAllNotificationsAsReadCommand(int UserId) : IRequest<ApiResponse<bool>>;

public class MarkAllNotificationsAsReadCommandHandler : IRequestHandler<MarkAllNotificationsAsReadCommand, ApiResponse<bool>>
{
    private readonly IRepository<Notification> _notificationRepository;

    public MarkAllNotificationsAsReadCommandHandler(IRepository<Notification> notificationRepository)
    {
        _notificationRepository = notificationRepository;
    }

    public async Task<ApiResponse<bool>> Handle(MarkAllNotificationsAsReadCommand request, CancellationToken cancellationToken)
    {
        var notifications = await _notificationRepository.FindAsync(n => n.UserId == request.UserId && !n.IsRead);
        foreach (var n in notifications)
        {
            n.IsRead = true;
            await _notificationRepository.UpdateAsync(n);
        }
        await _notificationRepository.SaveChangesAsync();
        return ApiResponse<bool>.SuccessResponse(true, "All notifications marked as read.");
    }
}

// CLEAR ALL NOTIFICATIONS
public record ClearAllNotificationsCommand(int UserId) : IRequest<ApiResponse<bool>>;

public class ClearAllNotificationsCommandHandler : IRequestHandler<ClearAllNotificationsCommand, ApiResponse<bool>>
{
    private readonly IRepository<Notification> _notificationRepository;

    public ClearAllNotificationsCommandHandler(IRepository<Notification> notificationRepository)
    {
        _notificationRepository = notificationRepository;
    }

    public async Task<ApiResponse<bool>> Handle(ClearAllNotificationsCommand request, CancellationToken cancellationToken)
    {
        var notifications = await _notificationRepository.FindAsync(n => n.UserId == request.UserId);
        foreach (var n in notifications)
        {
            await _notificationRepository.DeleteAsync(n);
        }
        await _notificationRepository.SaveChangesAsync();
        return ApiResponse<bool>.SuccessResponse(true, "All notifications cleared.");
    }
}
