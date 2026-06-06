using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Application.Features.Placement;

namespace CoreLearningSystem.API.Controllers;

[Authorize]
public class PlacementController : ApiControllerBase
{
    [HttpGet("start")]
    public async Task<ActionResult<ApiResponse<List<PlacementQuestionDto>>>> StartTest()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
        {
            return Unauthorized(ApiResponse<List<PlacementQuestionDto>>.FailureResponse("Unauthorized. Please log in."));
        }

        var result = await Mediator.Send(new StartPlacementTestCommand(userId));
        if (!result.Success)
        {
            if (result.Message.Contains("not found") || result.Message.Contains("chưa có sẵn bài kiểm tra đầu vào"))
            {
                return NotFound(new { message = result.Message });
            }
            return BadRequest(new { message = result.Message });
        }
        return Ok(result);
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
        {
            return Unauthorized(new { message = "Unauthorized. Please log in." });
        }

        var result = await Mediator.Send(new GetPlacementStatusQuery(userId));
        return Ok(new { 
            hasTaken = result.HasTaken, 
            quizId = result.QuizId, 
            message = result.Message 
        });
    }

    [HttpPost("submit")]
    public async Task<ActionResult<ApiResponse<PlacementSubmitResponse>>> SubmitTest([FromBody] List<PlacementAnswerInput> answers)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
        {
            return Unauthorized(ApiResponse<PlacementSubmitResponse>.FailureResponse("Unauthorized. Please log in."));
        }

        var result = await Mediator.Send(new SubmitPlacementTestCommand(userId, answers ?? new List<PlacementAnswerInput>()));
        if (!result.Success) return BadRequest(new { message = result.Message });
        return Ok(result);
    }
}
