using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Application.Features.Feedback;

namespace CoreLearningSystem.API.Controllers;

public record SubmitFeedbackInput(string Subject, string Content, int Rating);

[Authorize]
public class FeedbackController : ApiControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ApiResponse<FeedbackDto>>> Submit([FromBody] SubmitFeedbackInput input)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
        {
            return Unauthorized(ApiResponse<FeedbackDto>.FailureResponse("Unauthorized. Please log in."));
        }

        var result = await Mediator.Send(new SubmitFeedbackCommand(userId, input.Subject, input.Content, input.Rating));
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }

    [HttpGet("my")]
    public async Task<ActionResult<ApiResponse<IEnumerable<FeedbackDto>>>> GetMyFeedbacks()
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
        {
            return Unauthorized(ApiResponse<IEnumerable<FeedbackDto>>.FailureResponse("Unauthorized. Please log in."));
        }

        var result = await Mediator.Send(new GetMyFeedbacksQuery(userId));
        return Ok(result);
    }

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApiResponse<IEnumerable<FeedbackDto>>>> GetAll()
    {
        var result = await Mediator.Send(new GetFeedbacksQuery());
        return Ok(result);
    }

    [HttpPost("{id}/review")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApiResponse<FeedbackDto>>> Review(int id, [FromBody] ReviewFeedbackCommand command)
    {
        if (id != command.FeedbackId) return BadRequest(ApiResponse<FeedbackDto>.FailureResponse("Mismatched Feedback ID."));
        var result = await Mediator.Send(command);
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }

    [HttpPut("{id}/resolve")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApiResponse<FeedbackDto>>> Resolve(int id)
    {
        var adminIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (adminIdClaim == null || !int.TryParse(adminIdClaim.Value, out var adminId))
        {
            return Unauthorized(ApiResponse<FeedbackDto>.FailureResponse("Unauthorized. Please log in."));
        }

        var result = await Mediator.Send(new ResolveFeedbackCommand(id, adminId));
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(int id)
    {
        var result = await Mediator.Send(new DeleteFeedbackCommand(id));
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }
}
