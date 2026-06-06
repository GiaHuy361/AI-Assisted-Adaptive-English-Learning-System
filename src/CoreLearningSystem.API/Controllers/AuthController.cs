using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Application.Features.Auth;

namespace CoreLearningSystem.API.Controllers;

public class AuthController : ApiControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<ApiResponse<AuthResponseDto>>> Register([FromBody] RegisterDto dto)
    {
        var result = await Mediator.Send(new RegisterCommand(dto));
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }

    [HttpPost("login")]
    public async Task<ActionResult<ApiResponse<AuthResponseDto>>> Login([FromBody] LoginDto dto)
    {
        var result = await Mediator.Send(new LoginCommand(dto));
        if (!result.Success) return Unauthorized(result);
        return Ok(result);
    }
}
