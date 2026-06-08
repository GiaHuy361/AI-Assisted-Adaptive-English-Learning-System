using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AdaptiveLearning.Contracts.Events;
using CoreLearningSystem.Application.Interfaces;
using CoreLearningSystem.Domain.Enums;

namespace AdaptiveLearning.Worker.Handlers;

public class FeedbackSubmittedEventHandler : IEventHandler<FeedbackSubmittedEvent>
{
    private readonly IFeedbackAnalysisService _feedbackAnalysisService;
    private readonly ILogger<FeedbackSubmittedEventHandler> _logger;

    public FeedbackSubmittedEventHandler(
        IFeedbackAnalysisService feedbackAnalysisService,
        ILogger<FeedbackSubmittedEventHandler> logger)
    {
        _feedbackAnalysisService = feedbackAnalysisService;
        _logger = logger;
    }

    public async Task HandleAsync(FeedbackSubmittedEvent ev)
    {
        _logger.LogInformation(
            "FeedbackSubmittedEvent received. EventId={EventId}, UserId={UserId}, Rating={Rating}, TargetType={TargetType}, TargetId={TargetId}",
            ev.EventId, ev.UserId, ev.Rating, ev.TargetType, ev.TargetId);

        // Validation
        if (ev.UserId <= 0)
            throw new ArgumentException("Invalid UserId in FeedbackSubmittedEvent.");
        if (ev.Rating < 1 || ev.Rating > 5)
            throw new ArgumentException("Invalid Rating in FeedbackSubmittedEvent. Must be 1–5.");

        // Parse TargetType — default to System if unknown
        var targetType = FeedbackTargetType.System;
        if (!string.IsNullOrEmpty(ev.TargetType) &&
            Enum.TryParse<FeedbackTargetType>(ev.TargetType, ignoreCase: true, out var parsedType))
        {
            targetType = parsedType;
        }

        // LearnerProfileId is not on the event (UserId is); pass UserId as proxy identifier.
        // FeedbackAnalysisService uses aggregateKey, so learnerProfileId is only for logging context.
        await _feedbackAnalysisService.ProcessFeedbackAsync(
            feedbackId: 0,          // event doesn't carry feedbackId; 0 is fine for logging
            learnerProfileId: ev.UserId,
            targetType: targetType,
            targetId: ev.TargetId,
            rating: ev.Rating);

        _logger.LogInformation(
            "FeedbackSubmittedEvent processed. EventId={EventId}, TargetType={TargetType}, TargetId={TargetId}",
            ev.EventId, targetType, ev.TargetId);
    }
}
