using System.Threading.Tasks;
using CoreLearningSystem.Application.DTOs.Common;

namespace CoreLearningSystem.Application.Interfaces;

public interface IRecommendationService
{
    Task<RecommendationResponse> GenerateRecommendationsAsync(RecommendationRequest request);
    Task HandleLessonCompletedAsync(int learnerProfileId, int lessonId, string sourceEventId);
}
