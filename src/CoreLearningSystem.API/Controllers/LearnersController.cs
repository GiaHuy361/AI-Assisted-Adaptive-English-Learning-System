using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Application.Features.Learners;

namespace CoreLearningSystem.API.Controllers;

[Authorize(Roles = "Admin")]
public class LearnersController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IEnumerable<LearnerProfileDto>>>> GetAll()
    {
        var result = await Mediator.Send(new GetLearnersQuery());
        return Ok(result);
    }
}
