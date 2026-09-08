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

    // Track bets that have already triggered or bypassed the 1-hour alarm
    private readonly ConcurrentDictionary<int, bool> _oneHourAlarmsSent = new();

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

        // Fetch all currently pending bets that HAVE a MatchStartTime
        var scheduledPendingBets = await dbContext.Bets
            .Where(b => b.Status == "Pending" && b.MatchStartTime != null)
            .ToListAsync(stoppingToken);

        // Cleanup tracking memory for bets that are no longer pending or don't have a start time
        var currentBetIds = scheduledPendingBets.Select(b => b.Id).ToHashSet();
        var keysToRemove = _oneHourAlarmsSent.Keys.Where(k => !currentBetIds.Contains(k)).ToList();
        foreach (var key in keysToRemove)
        {
            _oneHourAlarmsSent.TryRemove(key, out _);
        }

        var now = DateTime.UtcNow;

        foreach (var bet in scheduledPendingBets)
        {
            // Calculate time until match starts
            var timeUntilStart = bet.MatchStartTime!.Value - now;
            var minutesUntilStart = timeUntilStart.TotalMinutes;

            // Have we already processed this bet for the alarm?
            if (_oneHourAlarmsSent.ContainsKey(bet.Id))
            {
                continue;
            }

            // Exactly 1 hour window: between 55 and 65 minutes
            if (minutesUntilStart <= 65 && minutesUntilStart >= 55)
            {
                // We mark it as processed
                _oneHourAlarmsSent[bet.Id] = true;

                // Send the alert (but only if it's not the first boot, to prevent spam on server restarts)
                if (!_isFirstRun)
                {
                    await _discordService.SendWakeUpAlarmAsync(bet);
                }
            }
            // 47-minute edge case: Admin clicks lookup and the match is ALREADY less than 55 minutes away.
            // In this case, we just silently mark it as processed and bypass sending an alert!
            else if (minutesUntilStart < 55)
            {
                _oneHourAlarmsSent[bet.Id] = true;
            }
            // If it's > 65 minutes away, we do nothing. It will be evaluated on future runs.
        }

        if (_isFirstRun)
        {
            _isFirstRun = false;
        }
    }
}