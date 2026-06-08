using System;
using CoreLearningSystem.Domain.Enums;

namespace CoreLearningSystem.Domain.Entities;

public class UserSession
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string SessionTokenHash { get; set; } = string.Empty;
    public string? RefreshTokenHash { get; set; }
    public string? JwtId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public SessionStatus Status { get; set; } = SessionStatus.Active;

    // Navigation Properties
    public User User { get; set; } = null!;
}
