using Microsoft.AspNetCore.SignalR;

namespace BettingApp.Hubs
{
    public class BetHub : Hub
    {
        public async Task SendUpdate(string userId, string message)
        {
            await Clients.User(userId).SendAsync("ReceiveUpdate", message);
        }

        public async Task JoinAdminGroup()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "Admins");
        }

        public async Task JoinUserGroup(string userId)
        {
            // Allows the client (Home.razor) to subscribe to messages for this specific user
            await Groups.AddToGroupAsync(Context.ConnectionId, userId);
        }
    }
}