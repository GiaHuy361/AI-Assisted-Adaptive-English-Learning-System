using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Application.Features.Users;

namespace CoreLearningSystem.API.Controllers;

[Authorize(Roles = "Admin")]
public class UsersController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IEnumerable<UserDto>>>> GetAll()
    {
        var result = await Mediator.Send(new GetUsersQuery());
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<UserDto>>> Create([FromBody] CreateUserCommand command)
    {
        var result = await Mediator.Send(command);
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<UserDto>>> Update(int id, [FromBody] UpdateUserCommand command)
    {
        if (id != command.Id) return BadRequest(ApiResponse<UserDto>.FailureResponse("Mismatched User ID."));
        var result = await Mediator.Send(command);
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(int id)
    {
        var result = await Mediator.Send(new DeleteUserCommand(id));
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }

    [HttpGet("{id}/details")]
    public async Task<ActionResult<ApiResponse<UserDetailExpandedDto>>> GetDetails(int id)
    {
        var result = await Mediator.Send(new GetUserDetailsExpandedQuery(id));
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }
}
