using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Application.Features.Goals;

namespace CoreLearningSystem.API.Controllers;

[Authorize]
public class GoalsController : ApiControllerBase
{
    [HttpGet("{learnerId}")]
    public async Task<ActionResult<ApiResponse<IEnumerable<GoalDto>>>> GetByLearner(int learnerId)
    {
        var result = await Mediator.Send(new GetGoalsQuery(learnerId));
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<GoalDto>>> Create([FromBody] CreateGoalCommand command)
    {
        var result = await Mediator.Send(command);
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }

    [HttpPut("{id}/progress")]
    public async Task<ActionResult<ApiResponse<GoalDto>>> UpdateProgress(int id, [FromBody] UpdateGoalProgressCommand command)
    {
        if (id != command.GoalId) return BadRequest(ApiResponse<GoalDto>.FailureResponse("Mismatched Goal ID."));
        var result = await Mediator.Send(command);
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }
}
