using System.Threading;
using System.Threading.Tasks;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;

namespace CoreLearningSystem.Application.Interfaces;

public interface IFeedbackAnalysisService
{
    /// <summary>
    /// Process a newly submitted feedback event: update the aggregate and potentially send admin alert.
    /// </summary>
    Task ProcessFeedbackAsync(int feedbackId, int learnerProfileId, FeedbackTargetType targetType, int? targetId, int rating, CancellationToken ct = default);

    /// <summary>
    /// Get feedback analysis aggregate by aggregate key.
    /// </summary>
    Task<FeedbackAnalysis?> GetAnalysisAsync(string aggregateKey, CancellationToken ct = default);

    /// <summary>
    /// Get all feedback analyses for the specified target type.
    /// </summary>
    Task<System.Collections.Generic.List<FeedbackAnalysis>> GetAnalysesForTypeAsync(FeedbackTargetType targetType, CancellationToken ct = default);
}
