using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using CoreLearningSystem.Application.Interfaces;

namespace CoreLearningSystem.Infrastructure.Services;

public class JwtTokenGenerator : IJwtTokenGenerator
{
    private readonly IConfiguration _configuration;

    public JwtTokenGenerator(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string GenerateToken(int userId, string username, string role)
    {
        var secret = _configuration["JwtSettings:Secret"] ?? "A_SUPER_SECRET_KEY_FOR_ADAPTIVE_ENGLISH_LEARNING_SYSTEM_GRADUATION_PROJECT";
        var issuer = _configuration["JwtSettings:Issuer"] ?? "AdaptiveEnglishLearningCore";
        var audience = _configuration["JwtSettings:Audience"] ?? "AdaptiveEnglishLearningCoreUsers";
        var expiryString = _configuration["JwtSettings:ExpiryInMinutes"];
        
        double expiryInMinutes = double.TryParse(expiryString, out var parsed) ? parsed : 180;

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, role),
            new Claim(JwtRegisteredClaimNames.Sub, username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiryInMinutes),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
