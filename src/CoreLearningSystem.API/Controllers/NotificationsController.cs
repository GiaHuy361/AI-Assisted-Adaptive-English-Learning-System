using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Application.Features.Notifications;

namespace CoreLearningSystem.API.Controllers;

[Authorize]
public class NotificationsController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IEnumerable<NotificationDto>>>> GetMyNotifications()
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
        {
            return Unauthorized(ApiResponse<IEnumerable<NotificationDto>>.FailureResponse("Unauthorized. Please log in."));
        }
        var result = await Mediator.Send(new GetNotificationsQuery(userId));
        return Ok(result);
    }

    [HttpGet("user/{userId}")]
    public async Task<ActionResult<ApiResponse<IEnumerable<NotificationDto>>>> GetByUserId(int userId)
    {
        var result = await Mediator.Send(new GetNotificationsQuery(userId));
        return Ok(result);
    }

    [HttpPut("{id}/read")]
    public async Task<ActionResult<ApiResponse<bool>>> MarkAsRead(int id)
    {
        var result = await Mediator.Send(new MarkNotificationAsReadCommand(id));
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }

    [HttpPost("read-all")]
    public async Task<ActionResult<ApiResponse<bool>>> MarkAllAsRead()
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
        {
            return Unauthorized(ApiResponse<bool>.FailureResponse("Unauthorized. Please log in."));
        }
        var result = await Mediator.Send(new MarkAllNotificationsAsReadCommand(userId));
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }

    [HttpDelete("clear-all")]
    public async Task<ActionResult<ApiResponse<bool>>> ClearAll()
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
        {
            return Unauthorized(ApiResponse<bool>.FailureResponse("Unauthorized. Please log in."));
        }
        var result = await Mediator.Send(new ClearAllNotificationsCommand(userId));
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }
}
