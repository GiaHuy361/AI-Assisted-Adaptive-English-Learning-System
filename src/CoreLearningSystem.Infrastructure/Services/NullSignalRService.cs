using System.Threading.Tasks;
using CoreLearningSystem.Application.Interfaces;

namespace CoreLearningSystem.Infrastructure.Services;

public class NullSignalRService : ISignalRService
{
    public Task SendNotificationAsync(int userId, object notification)
    {
        return Task.CompletedTask;
    }

    public Task SendCrudUpdateAsync(string entityName, string action, object data)
    {
        return Task.CompletedTask;
    }
}
