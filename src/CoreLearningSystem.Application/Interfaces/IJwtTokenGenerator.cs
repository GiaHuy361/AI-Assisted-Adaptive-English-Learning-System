using System;

namespace CoreLearningSystem.Application.Interfaces;

public record TokenGenerationResult(string Token, string JwtId, DateTime ExpiresAt);

public interface IJwtTokenGenerator
{
    TokenGenerationResult GenerateToken(int userId, string username, string role);
}
