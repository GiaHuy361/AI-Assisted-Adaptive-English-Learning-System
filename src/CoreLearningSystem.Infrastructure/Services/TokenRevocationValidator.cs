using System;
using System.Linq;
using System.Threading.Tasks;
using CoreLearningSystem.Application.Interfaces;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;
using StackExchange.Redis;

namespace CoreLearningSystem.Infrastructure.Services;

public class TokenRevocationValidator : ITokenRevocationValidator
{
    private readonly IConnectionMultiplexer _mux;
    private readonly ICacheService _cacheService;
    private readonly IRepository<UserSession> _sessionRepo;

    public TokenRevocationValidator(
        IConnectionMultiplexer mux,
        ICacheService cacheService,
        IRepository<UserSession> sessionRepo)
    {
        _mux = mux;
        _cacheService = cacheService;
        _sessionRepo = sessionRepo;
    }

    public async Task<bool> IsTokenRevokedAsync(string jwtId)
    {
        if (string.IsNullOrEmpty(jwtId)) return true;

        // 1. Check Redis blacklist first if connected
        if (_mux.IsConnected)
        {
            try
            {
                var isBlacklisted = await _cacheService.ExistsAsync($"adaptive:v1:token-revoked:{jwtId}");
                if (isBlacklisted)
                {
                    return true;
                }
            }
            catch
            {
                // Graceful fallback if Redis calls throw
            }
        }

        // 2. Query DB UserSession
        var sessions = await _sessionRepo.FindAsync(s => s.JwtId == jwtId);
        var session = sessions.FirstOrDefault();
        if (session == null)
        {
            return true; // No session exists for this JWT -> Invalid/Revoked
        }

        if (session.Status == SessionStatus.Revoked ||
            session.RevokedAt.HasValue ||
            session.Status == SessionStatus.Expired ||
            session.ExpiresAt < DateTime.UtcNow)
        {
            return true;
        }

        return false;
    }

    public async Task RevokeTokenAsync(string jwtId, string token, DateTime expiresAt)
    {
        if (string.IsNullOrEmpty(jwtId)) return;

        // 1. Update database sessions
        var sessions = await _sessionRepo.FindAsync(s => s.JwtId == jwtId);
        var session = sessions.FirstOrDefault();
        if (session != null)
        {
            session.Status = SessionStatus.Revoked;
            session.RevokedAt = DateTime.UtcNow;
            await _sessionRepo.UpdateAsync(session);
            await _sessionRepo.SaveChangesAsync();
        }

        // 2. Add to Redis blacklist with TTL
        if (_mux.IsConnected)
        {
            try
            {
                var remainingTtl = expiresAt - DateTime.UtcNow;
                if (remainingTtl > TimeSpan.Zero)
                {
                    // Cache the JWT ID blacklist entry as string "1"
                    await _cacheService.SetAsync($"adaptive:v1:token-revoked:{jwtId}", "1", remainingTtl);
                }
            }
            catch
            {
                // Graceful failure
            }
        }
    }
}
