using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using CoreLearningSystem.Application.Interfaces;

namespace CoreLearningSystem.API.Hubs;

public class SignalRService : ISignalRService
{
    private readonly IHubContext<AppHub> _hubContext;

    public SignalRService(IHubContext<AppHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task SendNotificationAsync(int userId, object notification)
    {
        await _hubContext.Clients.User(userId.ToString()).SendAsync("ReceiveNotification", notification);
    }

    public async Task SendCrudUpdateAsync(string entityName, string action, object data)
    {
        await _hubContext.Clients.All.SendAsync("ReceiveCrudUpdate", new
        {
            Entity = entityName,
            Action = action,
            Data = data
        });
    }
}
