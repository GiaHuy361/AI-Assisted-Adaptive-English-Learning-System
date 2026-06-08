using System.Threading.Tasks;
using AdaptiveLearning.Contracts.Events;

namespace AdaptiveLearning.Worker;

public interface IEventHandler<in TEvent> where TEvent : BaseEvent
{
    Task HandleAsync(TEvent ev);
}
