using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Json; 
using BettingApp.Data;      

using Microsoft.Extensions.DependencyInjection;

namespace BettingApp.Services;

public class DiscordNotificationService : IHostedService
{
    private readonly DiscordSocketClient _client;
    private readonly IConfiguration _config;
    private readonly ILogger<DiscordNotificationService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _botToken;

    public DiscordNotificationService(IConfiguration config, ILogger<DiscordNotificationService> logger, IHttpClientFactory httpClientFactory, IServiceScopeFactory scopeFactory)
    {
        _config = config;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _scopeFactory = scopeFactory;
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

    public async Task NotifyUserFreeBetAsync(string? discordUserId, int amount, string? note)
    {
        if (string.IsNullOrWhiteSpace(discordUserId)) return;

        string noteLine = string.IsNullOrWhiteSpace(note) ? "" : $"\nNote: *{note}*";
        string message = $"🎁 **Free Bet Received!**\n" +
                         $"You have been granted a **{amount:N0} NOK** free bet balance!\n" +
                         $"This will be automatically applied to your next approved pick(s).{noteLine}\n" +
                         $"Good luck! 🍀\n" +
                         $"------------------------------\n";

        await SendDmAsync(discordUserId, message);
    }

    public async Task NotifyUserDepositAsync(string? discordUserId, int amount)
    {
        if (string.IsNullOrWhiteSpace(discordUserId)) return;

        string message = $"💰 **Deposit Processed!**\n" +
                         $"Your account has been credited with **{amount:N0} NOK**.\n" +
                         $"Good luck! 🍀\n" +
                         $"------------------------------\n";

        await SendDmAsync(discordUserId, message);
    }

    public async Task NotifyUserReferralAddedAsync(string? discordUserId, string referredUserName)
    {
        if (string.IsNullOrWhiteSpace(discordUserId)) return;

        string message = $"🤝 **New Referral Confirmed!**\n" +
                         $"You have successfully referred **{referredUserName}**!\n" +
                         $"Your referral bonuses (Free Bets) will automatically unlock and be added to your balance as they meet the turnover requirements.\n" +
                         $"You can track the progress of this at any time under **My Referrals** on the website (if you're on a phone, tap the ☰ menu icon in the top corner to find it).\n" +
                         $"Thanks for inviting your friends! 💸\n" +
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

        string userDisplayName = bet.UserName;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.FindAsync(bet.UserId);
            if (user != null)
            {
                string discordName = !string.IsNullOrEmpty(user.DiscordUsername) ? user.DiscordUsername : bet.UserName;
                string fullName = $"{user.FirstName} {user.LastName}".Trim();
                
                if (!string.IsNullOrEmpty(fullName))
                {
                    userDisplayName = $"{discordName} ({fullName})";
                }
                else
                {
                    userDisplayName = discordName;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load user details for discord notification.");
        }

        string description = $"**New Pick Placed!**\n" +
                             $"**User:** {userDisplayName}\n" +
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

        string userDisplayName = transaction.UserName;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.FindAsync(transaction.UserId);
            if (user != null)
            {
                string discordName = !string.IsNullOrEmpty(user.DiscordUsername) ? user.DiscordUsername : transaction.UserName;
                string fullName = $"{user.FirstName} {user.LastName}".Trim();
                
                if (!string.IsNullOrEmpty(fullName))
                {
                    userDisplayName = $"{discordName} ({fullName})";
                }
                else
                {
                    userDisplayName = discordName;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load user for Discord notification.");
        }

        string description = $"**💸 New Withdrawal Request!**\n" +
                             $"**User:** {userDisplayName}\n" +
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

    public async Task<List<(ulong Id, string Name)>> GetGuildTextChannelsAsync()
    {
        if (_client.LoginState != LoginState.LoggedIn) return new();
        
        var guildIdString = _config["Discord:GuildId"];
        if (!ulong.TryParse(guildIdString, out var guildId))
        {
            _logger.LogWarning("Invalid or missing Discord:GuildId config.");
            return new();
        }

        try
        {
            var guild = await _client.Rest.GetGuildAsync(guildId);
            if (guild == null)
            {
                _logger.LogWarning($"Could not find Guild with ID {guildId}. Ensure bot is invited.");
                return new();
            }

            var channels = await guild.GetTextChannelsAsync();
            return channels
                .OrderBy(c => c.Name)
                .Select(c => (c.Id, c.Name))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to fetch text channels for guild {guildId}");
            return new();
        }
    }

    public async Task<int> BroadcastMessageAsync(List<ulong> channelIds, string title, string message, List<string>? attachmentPaths = null)
    {
        if (_client.LoginState != LoginState.LoggedIn) return 0;

        string header = string.IsNullOrWhiteSpace(title) ? "" : $"## {title}\n";
        string content = $"{header}{message}";

        int count = 0;
        int successfulSends = 0;
        foreach (var channelId in channelIds)
        {
            try
            {
                if (_client.GetChannel(channelId) is ITextChannel channel)
                {
                    if (attachmentPaths != null && attachmentPaths.Any())
                    {
                        var fileAttachments = attachmentPaths
                            .Where(p => System.IO.File.Exists(p))
                            .Select(p => new FileAttachment(p))
                            .ToList();

                        if (fileAttachments.Any())
                        {
                            await channel.SendFilesAsync(fileAttachments, text: content);
                        }
                        else
                        {
                            await channel.SendMessageAsync(content);
                        }
                    }
                    else
                    {
                        await channel.SendMessageAsync(content);
                    }
                    successfulSends++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to send broadcast to channel {channelId}");
            }

            count++;
            if (count % 30 == 0)
            {
                await Task.Delay(1500); // 1.5-second delay every 30 messages to respect rate limits
            }
            else
            {
                await Task.Delay(50); // slight delay between all messages
            }
        }
        return successfulSends;
    }
}