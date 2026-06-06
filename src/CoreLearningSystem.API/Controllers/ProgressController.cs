using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Application.Features.Progress;

namespace CoreLearningSystem.API.Controllers;

[Authorize]
public class ProgressController : ApiControllerBase
{
    [HttpGet("{learnerId}")]
    public async Task<ActionResult<ApiResponse<ProgressSummaryDto>>> GetSummary(int learnerId)
    {
        var result = await Mediator.Send(new GetProgressSummaryQuery(learnerId));
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }

    [HttpGet("details")]
    public async Task<ActionResult<ApiResponse<LearnerProgressDetailsDto>>> GetMyDetails()
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
        {
            return Unauthorized(ApiResponse<LearnerProgressDetailsDto>.FailureResponse("Unauthorized. Please log in."));
        }

        var result = await Mediator.Send(new GetLearnerProgressDetailsQuery(userId));
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }
}
