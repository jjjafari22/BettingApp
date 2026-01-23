using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using BettingApp.Data;
using Microsoft.EntityFrameworkCore;

namespace BettingApp.Services;

public class PendingBetsNotificationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DiscordNotificationService _discordService;
    private readonly ILogger<PendingBetsNotificationService> _logger;
    // Check every 15 minutes
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(15);

    public PendingBetsNotificationService(
        IServiceScopeFactory scopeFactory,
        DiscordNotificationService discordService,
        ILogger<PendingBetsNotificationService> logger)
    {
        _scopeFactory = scopeFactory;
        _discordService = discordService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Pending Bets Notification Service starting...");

        // Use PeriodicTimer for a non-blocking delay loop
        using var timer = new PeriodicTimer(_checkInterval);

        // Wait for the next tick to prevent immediate spam on restart
        while (await timer.WaitForNextTickAsync(stoppingToken) && !stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndNotifyAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while checking for pending bets.");
            }
        }
    }

    private async Task CheckAndNotifyAsync()
    {
        // We must create a scope to access the Database (Scoped) from a Background Service (Singleton)
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Efficiently aggregate data in the database
        var pendingStats = await context.Bets
            .Where(b => b.Status == "Pending")
            .GroupBy(b => 1) // Dummy group to aggregate all
            .Select(g => new
            {
                Count = g.Count(),
                OldestSubmission = g.Min(b => b.CreatedAt)
            })
            .FirstOrDefaultAsync();

        // Only notify if there are pending bets
        if (pendingStats != null && pendingStats.Count > 0)
        {
            var duration = DateTime.UtcNow - pendingStats.OldestSubmission;
            
            await _discordService.SendPendingBetsReminderAsync(pendingStats.Count, duration);
            
            _logger.LogInformation($"Sent reminder: {pendingStats.Count} bets pending. Oldest: {duration.TotalMinutes:N0} mins.");
        }
    }
}