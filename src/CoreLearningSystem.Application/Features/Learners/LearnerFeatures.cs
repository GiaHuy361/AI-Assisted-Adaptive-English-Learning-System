using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;
using CoreLearningSystem.Application.Interfaces;

namespace CoreLearningSystem.Application.Features.Learners;

public record LearnerProfileDto(int Id, int UserId, string Username, string Level, string ActivityStatus, DateTime LastActiveAt, DateTime? LastLoginDate);

// READ ALL LEARNERS
public record GetLearnersQuery() : IRequest<ApiResponse<IEnumerable<LearnerProfileDto>>>;

public class GetLearnersQueryHandler : IRequestHandler<GetLearnersQuery, ApiResponse<IEnumerable<LearnerProfileDto>>>
{
    private readonly IRepository<LearnerProfile> _learnerRepository;
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<PlacementTestResult> _testResultRepository;

    public GetLearnersQueryHandler(
        IRepository<LearnerProfile> learnerRepository, 
        IRepository<User> userRepository,
        IRepository<PlacementTestResult> testResultRepository)
    {
        _learnerRepository = learnerRepository;
        _userRepository = userRepository;
        _testResultRepository = testResultRepository;
    }

    public async Task<ApiResponse<IEnumerable<LearnerProfileDto>>> Handle(GetLearnersQuery request, CancellationToken cancellationToken)
    {
        var learners = await _learnerRepository.GetAllAsync();
        var users = await _userRepository.GetAllAsync();
        var placementResults = await _testResultRepository.GetAllAsync();
        
        var userDict = users.ToDictionary(u => u.Id);
        var placementLearnerProfileIds = placementResults.Select(r => r.LearnerProfileId).ToHashSet();

        var dtos = learners.Select(l => 
        {
            var username = "Unknown";
            var activityStatus = l.ActivityStatus;

            if (userDict.TryGetValue(l.UserId, out var u))
            {
                username = u.Username;
                if (!u.LastLoginDate.HasValue)
                {
                    activityStatus = ActivityStatus.Inactive;
                }
                else
                {
                    var lastLoginUtc = DateTime.SpecifyKind(u.LastLoginDate.Value, DateTimeKind.Utc);
                    var daysDiff = (DateTime.UtcNow - lastLoginUtc).TotalDays;
                    activityStatus = daysDiff > 7 ? ActivityStatus.Inactive : ActivityStatus.Active;
                }
            }

            var hasTakenPlacement = placementLearnerProfileIds.Contains(l.Id);
            var levelStr = (hasTakenPlacement && l.Level != EnglishLevel.None)
                ? l.Level.ToString()
                : "Chưa làm bài đánh giá";

            return new LearnerProfileDto(
                l.Id,
                l.UserId,
                username,
                levelStr,
                activityStatus.ToString(),
                l.LastActiveAt,
                u?.LastLoginDate
            );
        }).ToList();

        return ApiResponse<IEnumerable<LearnerProfileDto>>.SuccessResponse(dtos);
    }
}
