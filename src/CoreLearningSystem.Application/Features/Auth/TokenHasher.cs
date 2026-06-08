using System;
using System.Security.Cryptography;
using System.Text;

namespace CoreLearningSystem.Application.Features.Auth;

public static class TokenHasher
{
    public static string HashToken(string token)
    {
        if (string.IsNullOrEmpty(token)) return string.Empty;
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLower();
    }
}
