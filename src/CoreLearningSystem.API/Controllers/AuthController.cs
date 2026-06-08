using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Application.Features.Auth;
using CoreLearningSystem.Application.Interfaces;

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

    [HttpPost("logout")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<ActionResult<ApiResponse<string>>> Logout()
    {
        var jwtId = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value;
        if (string.IsNullOrEmpty(jwtId))
        {
            return BadRequest(ApiResponse<string>.FailureResponse("Invalid token."));
        }

        var validator = HttpContext.RequestServices.GetRequiredService<ITokenRevocationValidator>();
        await validator.RevokeTokenAsync(jwtId, "", DateTime.UtcNow);

        return Ok(ApiResponse<string>.SuccessResponse("Logout successful."));
    }
}
