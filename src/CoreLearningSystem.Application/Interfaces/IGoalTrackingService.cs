using System;
using System.Threading;
using System.Threading.Tasks;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Domain.Entities;

namespace CoreLearningSystem.Application.Interfaces;

public interface IGoalTrackingService
{
    Task<GoalProgressResult> UpdateGoalProgressAsync(GoalProgressRequest request, CancellationToken cancellationToken = default);
    GoalAdvisoryDto GetGoalAdvisory(GoalSetting goal, DateTime now);
}
