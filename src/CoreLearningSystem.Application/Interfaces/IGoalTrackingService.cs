using System.Threading;
using System.Threading.Tasks;
using CoreLearningSystem.Application.DTOs.Common;

namespace CoreLearningSystem.Application.Interfaces;

public interface IGoalTrackingService
{
    Task<GoalProgressResult> UpdateGoalProgressAsync(GoalProgressRequest request, CancellationToken cancellationToken = default);
}
