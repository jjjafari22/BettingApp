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
    
    // Check every 1 minute to catch the 15-minute mark accurately
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(1);

    // --- TRACKING STATE ---
    private int? _lastSeenNewestBetId = null;
    private DateTime _nextNotificationTime = DateTime.MinValue;
    private int _currentWaitMinutes = 15;

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

        using var timer = new PeriodicTimer(_checkInterval);

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
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Find the most recently created pending bet
        var newestPendingBet = await context.Bets
            .Where(b => b.Status == "Pending")
            .OrderByDescending(b => b.Id) // Use ID or CreatedAt for "newest"
            .FirstOrDefaultAsync();

        if (newestPendingBet == null)
        {
            _lastSeenNewestBetId = null;
            return;
        }

        var now = DateTime.UtcNow;

        // LOGIC: If a brand new bet has appeared at the top of the queue
        if (_lastSeenNewestBetId != newestPendingBet.Id)
        {
            _lastSeenNewestBetId = newestPendingBet.Id;
            _currentWaitMinutes = 15; // Reset backoff
            
            // Schedule the notification for 15 minutes after THIS new bet was created
            _nextNotificationTime = newestPendingBet.CreatedAt.AddMinutes(_currentWaitMinutes);
            
            _logger.LogInformation($"Newest bet detected (ID: {newestPendingBet.Id}). Next notification at: {_nextNotificationTime:HH:mm:ss} UTC");
        }

        // Check if it's time to notify
        if (now >= _nextNotificationTime)
        {
            // Gather aggregate data for the message
            var pendingBets = await context.Bets.Where(b => b.Status == "Pending").ToListAsync();
            var count = pendingBets.Count;
            var oldestTime = pendingBets.Min(b => b.CreatedAt);
            var durationSinceOldest = now - oldestTime;
            
            // Send the reminder
            await _discordService.SendPendingBetsReminderAsync(count, durationSinceOldest);
            
            // Calculate backoff for the NEXT notification
            _currentWaitMinutes *= 2;
            if (_currentWaitMinutes > 1440) _currentWaitMinutes = 1440; // Cap at 24h

            _nextNotificationTime = now.AddMinutes(_currentWaitMinutes);
            
            _logger.LogInformation($"Sent reminder. Backing off: next notify in {_currentWaitMinutes} mins.");
        }
    }
}