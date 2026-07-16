using System.Threading.Tasks;

namespace CoreLearningSystem.Application.Interfaces;

public interface ISignalRService
{
    Task SendNotificationAsync(int userId, object notification);
    Task SendCrudUpdateAsync(string entityName, string action, object data);
}
