namespace CoreLearningSystem.Application.Interfaces;

public interface IJwtTokenGenerator
{
    string GenerateToken(int userId, string username, string role);
}
