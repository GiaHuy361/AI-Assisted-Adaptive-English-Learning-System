using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AdaptiveLearning.Contracts.Events;

namespace AdaptiveLearning.Worker.Handlers;

public class BadgeAwardedEventHandler : IEventHandler<BadgeAwardedEvent>
{
    private readonly ILogger<BadgeAwardedEventHandler> _logger;

    public BadgeAwardedEventHandler(ILogger<BadgeAwardedEventHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(BadgeAwardedEvent ev)
    {
        if (ev == null) throw new ArgumentNullException(nameof(ev));

        _logger.LogInformation("BadgeAwardedEventHandler received event. EventId: {EventId}, UserId: {UserId}, LearnerProfileId: {ProfileId}, BadgeCode: {Code}, BadgeName: {Name}, MetricValue: {Metric}, Reason: {Reason}",
            ev.EventId, ev.UserId, ev.LearnerProfileId, ev.AchievementCode, ev.AchievementName, ev.ProgressValue, ev.Reason);

        if (ev.UserId <= 0 || ev.LearnerProfileId <= 0 || ev.AchievementId <= 0 || string.IsNullOrEmpty(ev.AchievementCode))
        {
            _logger.LogWarning("Invalid parameters in BadgeAwardedEvent: UserId={UserId}, ProfileId={ProfileId}, AchievementId={AchievementId}, Code={Code}",
                ev.UserId, ev.LearnerProfileId, ev.AchievementId, ev.AchievementCode);
        }

        return Task.CompletedTask;
    }
}
