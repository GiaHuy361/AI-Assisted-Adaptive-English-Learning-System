using System;
using System.Threading.Tasks;

namespace AdaptiveLearning.Worker.Services;

public interface IProcessedEventStore
{
    Task<bool> HasBeenProcessedAsync(Guid eventId);
    Task MarkAsProcessedAsync(Guid eventId, TimeSpan? ttl = null);
}
