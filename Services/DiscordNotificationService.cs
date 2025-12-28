using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace BettingApp.Services;

public class DiscordNotificationService : IHostedService
{
    private readonly DiscordSocketClient _client;
    private readonly IConfiguration _config;
    private readonly ILogger<DiscordNotificationService> _logger;
    private readonly string _botToken;

    public DiscordNotificationService(IConfiguration config, ILogger<DiscordNotificationService> logger)
    {
        _config = config;
        _logger = logger;
        // Ensure you add "Discord": { "BotToken": "..." } to your appsettings.json
        _botToken = _config["Discord:BotToken"] ?? "";

        var socketConfig = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.DirectMessages
        };
        _client = new DiscordSocketClient(socketConfig);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_botToken))
        {
            _logger.LogWarning("Discord Bot Token is missing. Notifications will not be sent.");
            return;
        }

        try 
        {
            _logger.LogInformation("Starting Discord Bot...");
            await _client.LoginAsync(TokenType.Bot, _botToken);
            await _client.StartAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start Discord Bot.");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Discord Bot...");
        await _client.StopAsync();
    }

    public async Task SendDmAsync(string? discordUserId, string message)
    {
        if (string.IsNullOrWhiteSpace(discordUserId) || _client.LoginState != LoginState.LoggedIn) return;

        try
        {
            if (!ulong.TryParse(discordUserId, out var id))
            {
                _logger.LogWarning($"Invalid Discord User ID format: {discordUserId}");
                return;
            }

            var user = await _client.Rest.GetUserAsync(id);
            
            if (user == null)
            {
                _logger.LogWarning($"Could not find Discord user with ID: {id}");
                return;
            }

            var dmChannel = await user.CreateDMChannelAsync();
            await dmChannel.SendMessageAsync(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to send Discord DM to {discordUserId}");
        }
    }
}