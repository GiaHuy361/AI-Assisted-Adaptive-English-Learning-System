using System.Threading.Tasks;

namespace AdaptiveLearning.GrpcService.Services;

public interface IQuizWeaknessAnalyzer
{
    Task<QuizAnalysisResult> AnalyzeAsync(QuizAnalysisInput input);
}
