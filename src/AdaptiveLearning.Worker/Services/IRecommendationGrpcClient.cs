using System.Threading;
using System.Threading.Tasks;
using AdaptiveLearning.Contracts.Events;

namespace AdaptiveLearning.Worker.Services;

public interface IRecommendationGrpcClient
{
    Task<QuizAnalysisResultModel> AnalyzeQuizSubmissionAsync(QuizSubmittedEvent ev, CancellationToken cancellationToken);
}
