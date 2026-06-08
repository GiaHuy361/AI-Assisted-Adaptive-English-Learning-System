using System.Threading;
using System.Threading.Tasks;
using CoreLearningSystem.Application.DTOs.Common;

namespace CoreLearningSystem.Application.Interfaces;

public interface IAchievementService
{
    Task<AchievementAwardResult> EvaluateAndAwardAsync(AchievementEvaluationRequest request, CancellationToken cancellationToken = default);
}
