using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Application.Features.Adaptive;

namespace CoreLearningSystem.API.Controllers;

/// <summary>
/// Adaptive learning endpoints — rule-based tips and next actions.
/// Frontend calls: GET /api/adaptive/study-tip
/// No external AI API. No additional secrets required.
/// </summary>
[Authorize]
[Route("api/adaptive")]
public class AdaptiveController : ApiControllerBase
{
    /// <summary>
    /// Returns a personalized AI-like study tip for today based on the learner's
    /// Skill Matrix, Weakness history, Learning Path, and Goals.
    /// Falls back to a friendly generic tip if insufficient data exists.
    /// </summary>
    /// <remarks>
    /// Sample response:
    /// {
    ///   "learnerId": 1,
    ///   "tipText": "Bạn đang yếu Vocabulary. Hôm nay nên học bài Travel Vocabulary để cải thiện kỹ năng này.",
    ///   "weakSkill": "Vocabulary",
    ///   "weakTopic": "Travel",
    ///   "recommendedAction": "Start recommended lesson",
    ///   "recommendedLessonIds": [12],
    ///   "generatedAt": "2026-06-25T10:00:00Z"
    /// }
    /// </remarks>
    [HttpGet("study-tip")]
    public async Task<ActionResult<ApiResponse<StudyTipDto>>> GetStudyTip()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
        {
            return Unauthorized(ApiResponse<StudyTipDto>.FailureResponse("Unauthorized. Please log in."));
        }

        var result = await Mediator.Send(new GetStudyTipQuery(userId));
        return Ok(result);
    }
}
