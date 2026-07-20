using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Application.Features.LearningPaths;

namespace CoreLearningSystem.API.Controllers;

[Authorize]
public class LearningPathsController : ApiControllerBase
{
    [HttpGet("current")]
    public async Task<ActionResult<ApiResponse<System.Collections.Generic.IEnumerable<PathStepDto>>>> GetCurrentPath()
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
        {
            return Unauthorized(ApiResponse<System.Collections.Generic.IEnumerable<PathStepDto>>.FailureResponse("Unauthorized access."));
        }

        var result = await Mediator.Send(new GetCurrentLearningPathQuery(userId));
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }

    [HttpGet("{learnerId}")]
    public async Task<ActionResult<ApiResponse<LearningPathDto>>> GetByLearner(int learnerId)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (userIdClaim != null && int.TryParse(userIdClaim.Value, out var userId))
        {
            var roleClaim = User.FindFirst(System.Security.Claims.ClaimTypes.Role);
            if (roleClaim != null && roleClaim.Value == "Learner")
            {
                var dbContext = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                    .GetRequiredService<CoreLearningSystem.Infrastructure.Persistence.AppDbContext>(HttpContext.RequestServices);
                var profile = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                    .FirstOrDefaultAsync(dbContext.LearnerProfiles, p => p.UserId == userId);
                if (profile != null)
                {
                    learnerId = profile.Id;
                }
            }
        }

        var result = await Mediator.Send(new GetLearningPathQuery(learnerId));
        if (!result.Success) return NotFound(result);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApiResponse<LearningPathDto>>> Create([FromBody] CreateLearningPathCommand command)
    {
        var result = await Mediator.Send(command);
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }
}
