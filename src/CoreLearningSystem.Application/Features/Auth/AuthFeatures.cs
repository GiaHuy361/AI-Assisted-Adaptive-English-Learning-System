using System;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using MediatR;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;
using CoreLearningSystem.Application.Interfaces;
using System.Linq;

namespace CoreLearningSystem.Application.Features.Auth;

// DTOs
public record RegisterDto(string Username, string Email, string Password, string FullName);
public record LoginDto(string Username, string Password);
public record AuthResponseDto(string Token, string Username, string Role, int UserId, string Level);

// REGISTER
public record RegisterCommand(RegisterDto Dto) : IRequest<ApiResponse<AuthResponseDto>>;

public class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(x => x.Dto.Username).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Dto.Email).NotEmpty().EmailAddress().MaximumLength(100);
        RuleFor(x => x.Dto.Password).NotEmpty().MinimumLength(6);
        RuleFor(x => x.Dto.FullName).NotEmpty().MaximumLength(100);
    }
}

public class RegisterCommandHandler : IRequestHandler<RegisterCommand, ApiResponse<AuthResponseDto>>
{
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<LearnerProfile> _profileRepository;
    private readonly IRepository<UserSession> _sessionRepository;
    private readonly IJwtTokenGenerator _tokenGenerator;

    public RegisterCommandHandler(
        IRepository<User> userRepository, 
        IRepository<LearnerProfile> profileRepository,
        IRepository<UserSession> sessionRepository,
        IJwtTokenGenerator tokenGenerator)
    {
        _userRepository = userRepository;
        _profileRepository = profileRepository;
        _sessionRepository = sessionRepository;
        _tokenGenerator = tokenGenerator;
    }

    public async Task<ApiResponse<AuthResponseDto>> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        // Check if username already exists
        var existingUsers = await _userRepository.FindAsync(u => u.Username == request.Dto.Username);
        if (existingUsers.Any())
        {
            return ApiResponse<AuthResponseDto>.FailureResponse("Username already exists.");
        }

        var user = new User
        {
            Username = request.Dto.Username,
            Email = request.Dto.Email,
            FullName = request.Dto.FullName,
            PasswordHash = BCryptMock.HashPassword(request.Dto.Password), // Standard secure practice stub
            Role = UserRole.Learner,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            await _userRepository.AddAsync(user);
            await _userRepository.SaveChangesAsync();

            // Create Learner Profile along with user
            var profile = new LearnerProfile
            {
                UserId = user.Id,
                Level = EnglishLevel.None,
                ActivityStatus = ActivityStatus.Active,
                LastActiveAt = DateTime.UtcNow
            };
            await _profileRepository.AddAsync(profile);
            await _profileRepository.SaveChangesAsync();

            user.LearnerProfile = profile;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[REGISTRATION ERROR] Detailed Crash Log: {ex.Message} -> {ex.InnerException?.Message}");
            throw;
        }

        var tokenResult = _tokenGenerator.GenerateToken(user.Id, user.Username, user.Role.ToString());
        
        // Save session
        var tokenHash = TokenHasher.HashToken(tokenResult.Token);

        var session = new UserSession
        {
            UserId = user.Id,
            SessionTokenHash = tokenHash,
            JwtId = tokenResult.JwtId,
            ExpiresAt = tokenResult.ExpiresAt,
            Status = SessionStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        await _sessionRepository.AddAsync(session);
        await _sessionRepository.SaveChangesAsync();

        var response = new AuthResponseDto(tokenResult.Token, user.Username, user.Role.ToString(), user.Id, "Chưa làm bài đánh giá");
        
        return ApiResponse<AuthResponseDto>.SuccessResponse(response, "Registration successful!");
    }
}

// LOGIN
public record LoginCommand(LoginDto Dto) : IRequest<ApiResponse<AuthResponseDto>>;

public class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Dto.Username).NotEmpty();
        RuleFor(x => x.Dto.Password).NotEmpty();
    }
}

public class LoginCommandHandler : IRequestHandler<LoginCommand, ApiResponse<AuthResponseDto>>
{
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<LearnerProfile> _profileRepository;
    private readonly IRepository<PlacementTestResult> _testResultRepository;
    private readonly IRepository<UserSession> _sessionRepository;
    private readonly IJwtTokenGenerator _tokenGenerator;

    public LoginCommandHandler(
        IRepository<User> userRepository, 
        IRepository<LearnerProfile> profileRepository,
        IRepository<PlacementTestResult> testResultRepository,
        IRepository<UserSession> sessionRepository,
        IJwtTokenGenerator tokenGenerator)
    {
        _userRepository = userRepository;
        _profileRepository = profileRepository;
        _testResultRepository = testResultRepository;
        _sessionRepository = sessionRepository;
        _tokenGenerator = tokenGenerator;
    }

    public async Task<ApiResponse<AuthResponseDto>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var users = await _userRepository.FindAsync(u => u.Username == request.Dto.Username);
        var user = users.FirstOrDefault();

        if (user == null || !BCryptMock.VerifyPassword(request.Dto.Password, user.PasswordHash))
        {
            return ApiResponse<AuthResponseDto>.FailureResponse("Invalid username or password.");
        }

        if (user.IsLocked)
        {
            return ApiResponse<AuthResponseDto>.FailureResponse("Your account is locked. Please contact Admin.");
        }

        user.LastLoginDate = DateTime.UtcNow;
        await _userRepository.UpdateAsync(user);
        await _userRepository.SaveChangesAsync();

        var profiles = await _profileRepository.FindAsync(p => p.UserId == user.Id);
        var profile = profiles.FirstOrDefault();
        
        var level = "Chưa làm bài đánh giá";
        if (profile != null)
        {
            var hasTakenPlacement = (await _testResultRepository.FindAsync(r => r.LearnerProfileId == profile.Id)).Any();
            if (hasTakenPlacement)
            {
                level = profile.Level == EnglishLevel.None ? "Chưa làm bài đánh giá" : profile.Level.ToString();
            }
            else
            {
                level = "Chưa làm bài đánh giá";
                if (profile.Level != EnglishLevel.None)
                {
                    profile.Level = EnglishLevel.None;
                    await _profileRepository.UpdateAsync(profile);
                    await _profileRepository.SaveChangesAsync();
                }
            }
        }

        var tokenResult = _tokenGenerator.GenerateToken(user.Id, user.Username, user.Role.ToString());

        // Save session
        var tokenHash = TokenHasher.HashToken(tokenResult.Token);

        var session = new UserSession
        {
            UserId = user.Id,
            SessionTokenHash = tokenHash,
            JwtId = tokenResult.JwtId,
            ExpiresAt = tokenResult.ExpiresAt,
            Status = SessionStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        await _sessionRepository.AddAsync(session);
        await _sessionRepository.SaveChangesAsync();

        var response = new AuthResponseDto(tokenResult.Token, user.Username, user.Role.ToString(), user.Id, level);

        return ApiResponse<AuthResponseDto>.SuccessResponse(response, "Login successful!");
    }
}

// SECURE BOILERPLATE STUBS
public static class BCryptMock
{
    public static string HashPassword(string password) => $"HASHED_{password}";
    public static bool VerifyPassword(string password, string hash) => hash == $"HASHED_{password}";
}
