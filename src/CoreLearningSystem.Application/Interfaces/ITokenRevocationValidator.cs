using System;
using System.Threading.Tasks;

namespace CoreLearningSystem.Application.Interfaces;

public interface ITokenRevocationValidator
{
    Task<bool> IsTokenRevokedAsync(string jwtId);
    Task RevokeTokenAsync(string jwtId, string token, DateTime expiresAt);
}
