using Microsoft.AspNetCore.SignalR;

namespace BettingApp.Hubs
{
    public class BetHub : Hub
    {
        public async Task SendUpdate(string userId, string message)
        {
            await Clients.User(userId).SendAsync("ReceiveUpdate", message);
        }
    }
}