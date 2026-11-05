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
    private static readonly ConcurrentDictionary<int, bool> _oneHourAlarmsSent = new();

    private const double MatchStartWindowMinutes = 60.0;
    private const double PendingGracePeriodMinutes = 15.0;

    /// <summary>
    /// Marks a bet as already seen/handled by an admin when it is already within the 1-hour window,
    /// preventing a redundant Discord wake-up alarm while the admin is actively reviewing it.
    /// </summary>
    public static void SuppressAlarmForBet(int betId)
    {
        _oneHourAlarmsSent[betId] = true;
    }

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
            // Have we already processed this bet for the alarm?
            if (_oneHourAlarmsSent.ContainsKey(bet.Id))
            {
                continue;
            }

            // Calculate time until match starts
            var timeUntilStart = bet.MatchStartTime!.Value - now;
            var minutesUntilStart = timeUntilStart.TotalMinutes;

            // If the match has already started, mark it as processed so we don't send a pre-match alarm
            if (minutesUntilStart <= 0)
            {
                _oneHourAlarmsSent[bet.Id] = true;
                continue;
            }

            // Never alert while an Auto-Approval countdown timer is actively running
            if (bet.AutoApproveAt != null)
            {
                continue;
            }

            // Only consider bets whose match starts within 60 minutes
            if (minutesUntilStart > MatchStartWindowMinutes)
            {
                continue;
            }

            // Enforce a 15-minute grace period from when the bet arrived in the Pending queue
            var pendingSince = bet.ArrivedPendingAt ?? bet.CreatedAt;
            var minutesInPending = (now - pendingSince).TotalMinutes;
            if (minutesInPending < PendingGracePeriodMinutes)
            {
                continue;
            }

            // Both conditions are met: match starts in <= 60 minutes AND bet has been pending for >= 15 minutes.
            // Mark as processed so this alarm only ever triggers ONCE per bet.
            _oneHourAlarmsSent[bet.Id] = true;

            // Send the alert (but skip on the very first run after app startup to prevent restart notification spam)
            if (!_isFirstRun)
            {
                await _discordService.SendWakeUpAlarmAsync(bet);
            }
        }

        if (_isFirstRun)
        {
            _isFirstRun = false;
        }
    }
}