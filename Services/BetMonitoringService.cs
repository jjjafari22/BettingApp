using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using BettingApp.Data;
using System.Text.Json;
using BettingApp.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace BettingApp.Services
{
    public class BetMonitoringService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<BetMonitoringService> _logger;
        private readonly AiVisionService _aiVisionService;

        public BetMonitoringService(
            IServiceProvider serviceProvider, 
            ILogger<BetMonitoringService> logger,
            AiVisionService aiVisionService)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _aiVisionService = aiVisionService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("BetMonitoringService starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessDueBetsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in BetMonitoringService loop");
                }

                // Wait 1 minute before the next check
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        private async Task ProcessDueBetsAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
            using var context = dbFactory.CreateDbContext();

            // Find active bets where NextCheckTime has passed
            var dueBets = await context.Bets
                .Where(b => b.Status == "Approved" && b.NextCheckTime.HasValue && b.NextCheckTime.Value <= DateTime.UtcNow)
                .ToListAsync(stoppingToken);

            bool anyUpdates = false;

            // Execute in parallel (up to 5 concurrent checks)
            await Parallel.ForEachAsync(dueBets, new ParallelOptions { MaxDegreeOfParallelism = 5, CancellationToken = stoppingToken }, async (bet, ct) =>
            {
                // Refresh bet from DB using a newly scoped context (since DbContext is not thread-safe)
                using var taskContext = dbFactory.CreateDbContext();
                var dbBet = await taskContext.Bets.FindAsync(new object[] { bet.Id }, ct);
                if (dbBet == null || dbBet.Status != "Approved") return;

                if (string.IsNullOrEmpty(dbBet.AiVisionResultJson))
                {
                    if (string.IsNullOrEmpty(dbBet.ScreenshotUrl)) return;
                    
                    var (extractionResult, error) = await _aiVisionService.ExtractBetSlipDataAsync(dbBet.ScreenshotUrl, dbBet.Id);
                    if (error != null)
                    {
                        dbBet.AiVisionError = error;
                        // Use the standard check outcome scheduling interval if extraction fails
                        dbBet.NextCheckTime = DateTime.UtcNow.AddMinutes(60);
                        await taskContext.SaveChangesAsync(ct);
                        return;
                    }
                    
                    if (extractionResult != null)
                    {
                        dbBet.AiVisionResultJson = System.Text.Json.JsonSerializer.Serialize(extractionResult);
                        dbBet.AiVisionError = null;
                        await taskContext.SaveChangesAsync(ct);
                    }
                }

                if (string.IsNullOrEmpty(dbBet.AiVisionResultJson)) return;

                string? result = await _aiVisionService.ConfirmOutcomeAsync(dbBet.AiVisionResultJson, dbBet.CreatedAt, dbBet.Id);
                
                dbBet.AiOutcomeResult = result;
                
                try 
                {
                    if (string.IsNullOrEmpty(result))
                    {
                        dbBet.NextCheckTime = DateTime.UtcNow.AddMinutes(60);
                    }
                    else
                    {
                        var doc = JsonDocument.Parse(result);
                        if (doc.RootElement.TryGetProperty("overallStatus", out var statusElement))
                        {
                            var status = statusElement.GetString()?.Trim().ToUpperInvariant() ?? "";
                            var isFinished = 
                                status == "MATCH FINISHED - WON" || status == "MATCH WON" || status == "WON" ||
                                status == "MATCH FINISHED - LOST" || status == "MATCH LOST" || status == "LOST" ||
                                status == "MATCH FINISHED - VOID" || status == "MATCH VOID" || status == "VOID" ||
                                status == "UNKNOWN";

                            if (isFinished)
                            {
                                // Match is finished, stop checking
                                dbBet.NextCheckTime = null;
                            }
                            else 
                            {
                                // Match is still running or unknown.
                                // First check if the AI provided a precise kickoff time (e.g. OddsPapi failed earlier)
                                if (doc.RootElement.TryGetProperty("matchStartTimeIso", out var startTimeElement) && 
                                    !string.IsNullOrEmpty(startTimeElement.GetString()))
                                {
                                    if (DateTime.TryParse(startTimeElement.GetString(), null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsedStart))
                                    {
                                        dbBet.MatchStartTime = parsedStart;
                                        
                                        // If game hasn't started yet or just started, sleep until 2 hours after kickoff
                                        var nextCheck = parsedStart.AddHours(2);
                                        dbBet.NextCheckTime = nextCheck <= DateTime.UtcNow ? DateTime.UtcNow.AddMinutes(60) : nextCheck;
                                    }
                                    else
                                    {
                                        dbBet.NextCheckTime = DateTime.UtcNow.AddMinutes(60);
                                    }
                                }
                                else
                                {
                                    // Fallback: check again in 60 minutes
                                    dbBet.NextCheckTime = DateTime.UtcNow.AddMinutes(60);
                                }
                            }
                        }
                    }
                }
                catch 
                {
                    // If AI fails to return valid JSON, try again in 60 mins
                    dbBet.NextCheckTime = DateTime.UtcNow.AddMinutes(60);
                }

                await taskContext.SaveChangesAsync(ct);
                anyUpdates = true;
            });
            
            if (anyUpdates)
            {
                // Notify UI about the update once for all bets
                var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<BetHub>>();
                await hubContext.Clients.Group("Admins").SendAsync("ReceiveAdminNotification", "Update");
            }
        }
    }
}
