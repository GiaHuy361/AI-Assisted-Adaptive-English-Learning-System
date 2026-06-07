using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace AdaptiveLearning.GrpcService.Services;

public class RecommendationGrpcService : RecommendationService.RecommendationServiceBase
{
    private readonly ILogger<RecommendationGrpcService> _logger;

    public RecommendationGrpcService(ILogger<RecommendationGrpcService> logger)
    {
        _logger = logger;
    }

    public override Task<RecommendationResponse> GetRecommendations(RecommendationRequest request, ServerCallContext context)
    {
        _logger.LogInformation("gRPC GetRecommendations request received for User: {UserId}", request.UserId);

        var response = new RecommendationResponse
        {
            UserId = request.UserId,
            Explanation = "Placeholder recommendation explanation (Phase 1 Skeleton)."
        };

        // Return empty or dummy recommendations
        response.RecommendedLessonIds.Add("dummy-lesson-1");
        response.RecommendedLessonIds.Add("dummy-lesson-2");

        return Task.FromResult(response);
    }

    public override Task<WeaknessAnalysisResponse> GetWeaknessAnalysis(WeaknessAnalysisRequest request, ServerCallContext context)
    {
        _logger.LogInformation("gRPC GetWeaknessAnalysis request received for User: {UserId}", request.UserId);

        var response = new WeaknessAnalysisResponse
        {
            UserId = request.UserId,
            Details = "Placeholder weakness analysis details (Phase 1 Skeleton)."
        };

        response.WeakSkills.Add("Listening");
        response.WeakSkills.Add("Grammar");

        return Task.FromResult(response);
    }

    public override Task<StatusResponse> GetServiceStatus(StatusRequest request, ServerCallContext context)
    {
        _logger.LogInformation("gRPC GetServiceStatus request received.");

        return Task.FromResult(new StatusResponse
        {
            Status = "HEALTHY",
            Version = "1.0.0-skeleton"
        });
    }
}
