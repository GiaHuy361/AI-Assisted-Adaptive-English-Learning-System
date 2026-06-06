using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Application.Features.Dashboard;

namespace CoreLearningSystem.API.Controllers;

[Authorize(Roles = "Admin")]
public class DashboardController : ApiControllerBase
{
    [HttpGet("stats")]
    public async Task<ActionResult<ApiResponse<DashboardStatsDto>>> GetStats()
    {
        var result = await Mediator.Send(new GetDashboardStatsQuery());
        return Ok(result);
    }

    [HttpGet("weak-learners")]
    public async Task<ActionResult<ApiResponse<IEnumerable<WeakLearnerDto>>>> GetWeakLearners()
    {
        var result = await Mediator.Send(new GetWeakLearnersQuery());
        return Ok(result);
    }
}
