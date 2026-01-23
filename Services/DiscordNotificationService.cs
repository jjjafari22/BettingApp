using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Json; 
using BettingApp.Data;      

namespace BettingApp.Services;

public class DiscordNotificationService : IHostedService
{
    private readonly DiscordSocketClient _client;
    private readonly IConfiguration _config;
    private readonly ILogger<DiscordNotificationService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _botToken;

    public DiscordNotificationService(IConfiguration config, ILogger<DiscordNotificationService> logger, IHttpClientFactory httpClientFactory)
    {
        _config = config;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
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

    // --- GENERIC NOTIFICATION (Unchanged) ---
    public async Task SendNotificationAsync(string title, string message)
    {
        var webhookUrl = _config["Discord:WebhookUrl"];
        if (string.IsNullOrEmpty(webhookUrl)) return;

        string content = $"**{title}**\n" +
                         $"{message}\n" +
                         $"------------------------------\n";

        try
        {
            var client = _httpClientFactory.CreateClient();
            await client.PostAsJsonAsync(webhookUrl, new { content });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send generic Discord Webhook notification.");
        }
    }

    // --- USER DM LOGIC (Unchanged) ---
    public async Task NotifyUserBetAsync(string? discordUserId, Bet bet, string status, bool isUpdate)
    {
        if (string.IsNullOrWhiteSpace(discordUserId)) return;
        string message = BuildBetMessage(bet, status, isUpdate);
        await SendDmAsync(discordUserId, message);
    }

    public async Task NotifyUserWithdrawalAsync(string? discordUserId, Transaction transaction, string status)
    {
        if (string.IsNullOrWhiteSpace(discordUserId)) return;

        string header = "";
        string icon = "";
        string messageBody = "";

        if (status == "Completed")
        {
            header = "**Withdrawal Confirmed!**";
            icon = "✅";
            messageBody = $"Your withdrawal of **{transaction.AmountNOK:N0} NOK** to **{transaction.Platform}** has been processed.";
        }
        else if (status == "Rejected")
        {
            header = "**Withdrawal Rejected**";
            icon = "❌";
            messageBody = $"Your withdrawal request of **{transaction.AmountNOK:N0} NOK** has been rejected.\nFunds have been returned to your balance.";
        }
        else
        {
            return; 
        }

        string message = $"{icon} {header}\n" +
                         $"{messageBody}\n" +
                         $"------------------------------\n";

        await SendDmAsync(discordUserId, message);
    }

    // --- ADMIN WEBHOOK LOGIC (Unchanged) ---
    public async Task NotifyAdminNewPickAsync(Bet bet)
    {
        var webhookUrl = _config["Discord:WebhookUrl"];
        if (string.IsNullOrEmpty(webhookUrl)) return;

        string baseUrl = _config["BaseUrl"] ?? "https://localhost:7143"; 
        string adminUrl = $"{baseUrl}/admin?ReviewBetId={bet.Id}";

        string description = $"**New Pick Placed!**\n" +
                             $"**User:** {bet.UserName}\n" +
                             $"[Approve Pick Here]({adminUrl})\n" +
                             (string.IsNullOrEmpty(bet.ScreenshotUrl) ? "" : $"[View Screenshot]({bet.ScreenshotUrl})\n") +
                             $"------------------------------\n";

        try
        {
            var client = _httpClientFactory.CreateClient();

            var embed = new
            {
                description = description,
                color = 3066993, // Green
                image = string.IsNullOrEmpty(bet.ScreenshotUrl) ? null : new { url = bet.ScreenshotUrl }
            };

            var payload = new 
            { 
                embeds = new[] { embed } 
            };

            await client.PostAsJsonAsync(webhookUrl, payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Discord Webhook.");
        }
    }

    public async Task NotifyAdminWithdrawalAsync(Transaction transaction)
    {
        var webhookUrl = _config["Discord:WebhookUrl"];
        if (string.IsNullOrEmpty(webhookUrl)) return;

        string baseUrl = _config["BaseUrl"] ?? "https://localhost:7143";
        string adminUrl = $"{baseUrl}/admin/transactions";

        string description = $"**💸 New Withdrawal Request!**\n" +
                             $"**User:** {transaction.UserName}\n" +
                             $"**Amount:** {transaction.AmountNOK:N0} NOK\n" +
                             $"**Platform:** {transaction.Platform}\n" +
                             $"[Manage Transactions]({adminUrl})\n" +
                             $"------------------------------\n";

        try
        {
            var client = _httpClientFactory.CreateClient();
            var payload = new 
            { 
                embeds = new[] 
                { 
                    new 
                    { 
                        description = description,
                        color = 15158332 // Red/Warning Color
                    } 
                } 
            };
            await client.PostAsJsonAsync(webhookUrl, payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Discord Webhook for withdrawal.");
        }
    }

    // --- NEW METHOD: Pending Bets Reminder (Uses IHttpClientFactory like the rest of the class) ---
    public async Task SendPendingBetsReminderAsync(int count, TimeSpan oldestWaitTime)
    {
        var webhookUrl = _config["Discord:WebhookUrl"];
        if (string.IsNullOrEmpty(webhookUrl)) return;

        // Format: "1h 45m" or just "45m"
        string timeString = oldestWaitTime.TotalHours >= 1 
            ? $"{(int)oldestWaitTime.TotalHours}h {oldestWaitTime.Minutes}m" 
            : $"{oldestWaitTime.Minutes}m";

        string baseUrl = _config["BaseUrl"] ?? "https://localhost:7143"; 
        string adminUrl = $"{baseUrl}/admin";

        try 
        {
            var client = _httpClientFactory.CreateClient();

            var embed = new
            {
                title = "⏳ Pending Bets Review Needed",
                description = $"There are currently **{count}** bets waiting for approval.\n[Go to Admin Dashboard]({adminUrl})",
                color = 16753920, // Orange
                fields = new[]
                {
                    new { name = "Queue Size", value = $"{count} Bets", inline = true },
                    new { name = "Wait Time", value = $"{timeString} (Oldest)", inline = true }
                },
                footer = new { text = "BettingApp Admin Bot" },
                timestamp = DateTime.UtcNow.ToString("o")
            };

            var payload = new
            {
                username = "BettingApp AdminBot",
                embeds = new[] { embed }
            };

            await client.PostAsJsonAsync(webhookUrl, payload);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to send Discord admin reminder: {ex.Message}");
        }
    }

    // --- MESSAGE BUILDER (Unchanged) ---
    private string BuildBetMessage(Bet bet, string status, bool isUpdate)
    {
        string updateTag = isUpdate ? " (Updated)" : "";
        string header = "";
        string icon = "";

        switch (status)
        {
            case "Approved":
                header = $"**Bet Approved!{updateTag}**";
                icon = "✅";
                break;
            case "Rejected":
                header = $"**Bet Rejected{updateTag}**";
                icon = "❌";
                break;
            case "Won":
                header = $"**WINNER!{updateTag}**";
                icon = "🎉";
                break;
            case "Lost":
                header = $"**Bet Lost{updateTag}**";
                icon = "📉";
                break;
            case "Void":
                header = $"**Bet Voided{updateTag}**";
                icon = "↩️";
                break;
            case "Cancelled":
                header = $"**Bet Cancelled{updateTag}**";
                icon = "🚫";
                break;
            default:
                header = $"**Bet Update{updateTag}**";
                icon = "ℹ️";
                break;
        }

        string fullScreenshotUrl = bet.ScreenshotUrl ?? "";
        string screenshotLink = string.IsNullOrEmpty(fullScreenshotUrl) 
            ? "" 
            : $"\n[View Screenshot]({fullScreenshotUrl})";

        string outcomeLine = "";
        if (status == "Won" || status == "Lost" || status == "Void")
        {
             outcomeLine = $"\nResult: {status}";
        }

        return $"{icon} {header}\n" +
               $"Bet Amount: {bet.AmountNOK} NOK\n" +
               $"Odds: {bet.Odds}\n" +
               $"Potential Payout: {bet.PotentialPayout:N0} NOK" +
               $"{outcomeLine}" +
               $"{screenshotLink}\n" +
               $"------------------------------\n";
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