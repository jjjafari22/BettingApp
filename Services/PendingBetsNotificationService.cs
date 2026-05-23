using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

    // Track the highest notification interval (in minutes) sent for each individual bet ID
    private readonly ConcurrentDictionary<int, int> _betNotificationStages = new();

    // Flag to track the first run after application startup to prevent restart notification spam
    private bool _isFirstRun = true;

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
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckPendingBetsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while checking for pending bets.");
            }

            // Poll the database every 1 minute
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task CheckPendingBetsAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Fetch all currently pending bets
        var pendingBets = await dbContext.Bets
            .Where(b => b.Status == "Pending")
            .ToListAsync(stoppingToken);

        // If there are no pending bets, clear the tracking dictionary and return
        if (!pendingBets.Any())
        {
            _betNotificationStages.Clear();
            return;
        }

        var currentPendingBetIds = pendingBets.Select(b => b.Id).ToHashSet();
        
        // Cleanup tracking memory for bets that are no longer pending (approved, denied, etc.)
        var keysToRemove = _betNotificationStages.Keys.Where(k => !currentPendingBetIds.Contains(k)).ToList();
        foreach (var key in keysToRemove)
        {
            _betNotificationStages.TryRemove(key, out _);
        }

        bool shouldNotify = false;
        var now = DateTime.UtcNow;

        foreach (var bet in pendingBets)
        {
            var betAgeMinutes = (now - bet.CreatedAt).TotalMinutes;
            
            // Find the highest interval threshold this specific bet has crossed
            var applicableInterval = GetApplicableInterval(betAgeMinutes);
            
            if (applicableInterval > 0)
            {
                var lastSentInterval = _betNotificationStages.GetValueOrDefault(bet.Id, 0);
                
                // If we haven't sent a notification for this specific interval on this specific bet
                if (applicableInterval > lastSentInterval)
                {
                    if (!_isFirstRun)
                    {
                        shouldNotify = true;
                    }
                    
                    // Mark this interval as sent for this bet
                    _betNotificationStages[bet.Id] = applicableInterval;
                }
            }
        }

        // After processing all bets on the first run, clear the flag
        if (_isFirstRun)
        {
            _isFirstRun = false;
        }

        // If at least one bet crossed a new time threshold, send the reminder to Discord
        if (shouldNotify)
        {
            // Calculate the wait time of the oldest pending bet for the TimeSpan parameter
            var oldestBetCreatedAt = pendingBets.Min(b => b.CreatedAt);
            TimeSpan oldestWaitTime = now - oldestBetCreatedAt;

            // Call the existing method with both the count and the required oldestWaitTime
            await _discordService.SendPendingBetsReminderAsync(pendingBets.Count, oldestWaitTime);
        }
    }

    private int GetApplicableInterval(double betAgeMinutes)
    {
        if (betAgeMinutes < 15) return 0;
        if (betAgeMinutes < 30) return 15;
        if (betAgeMinutes < 60) return 30;
        if (betAgeMinutes < 120) return 60;
        if (betAgeMinutes < 240) return 120;
        if (betAgeMinutes < 480) return 240;
        if (betAgeMinutes < 960) return 480;
        if (betAgeMinutes < 1440) return 960;
        
        // 24 hours (1440 minutes) or more. Reminds every 24 hours.
        int days = (int)(betAgeMinutes / 1440);
        return days * 1440;
    }
}