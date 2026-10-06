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

            // Active bets (Approved) are always checked if due.
            // Completed bets (Won/Lost/Void) are only checked if they were updated in the last 3 days. 
            // This prevents the system from hammering the API with ancient, dormant bets.
            var cutoffForCompleted = DateTime.UtcNow.AddDays(-3);
            
            var dueBets = await context.Bets
                .Where(b => b.NextCheckTime.HasValue && b.NextCheckTime.Value <= DateTime.UtcNow)
                .Where(b => b.Status == "Approved" || 
                           ((b.Status == "Won" || b.Status == "Lost" || b.Status == "Void") && b.UpdatedAt >= cutoffForCompleted))
                .ToListAsync(stoppingToken);

            bool anyUpdates = false;

            // Execute in parallel (up to 3 concurrent checks to prevent Gemini 429 Rate Limits)
            await Parallel.ForEachAsync(dueBets, new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = stoppingToken }, async (bet, ct) =>
            {
                // Refresh bet from DB using a newly scoped context (since DbContext is not thread-safe)
                using var taskContext = dbFactory.CreateDbContext();
                var dbBet = await taskContext.Bets.FindAsync(new object[] { bet.Id }, ct);
                if (dbBet == null || (dbBet.Status != "Approved" && dbBet.Status != "Won" && dbBet.Status != "Lost" && dbBet.Status != "Void")) return;
                
                if (dbBet.AiOutcomeResult == "Admin Override")
                {
                    if (dbBet.NextCheckTime != null)
                    {
                        dbBet.NextCheckTime = null;
                        await taskContext.SaveChangesAsync(ct);
                    }
                    return;
                }

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

                string? result = await _aiVisionService.ConfirmOutcomeAsync(dbBet.AiVisionResultJson, dbBet.CreatedAt, dbBet.MatchStartTime, dbBet.Id);
                
                dbBet.AiOutcomeResult = result;
                
                try 
                {
                    if (string.IsNullOrEmpty(result))
                    {
                        dbBet.NextCheckTime = DateTime.UtcNow.AddMinutes(60);
                    }
                    else
                    {
                        BettingApp.Services.AiOutcomeResultData? parsedData = null;
                        try 
                        {
                            parsedData = System.Text.Json.JsonSerializer.Deserialize<BettingApp.Services.AiOutcomeResultData>(
                                result, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                            );
                        }
                        catch { }

                        if (parsedData != null)
                        {
                            var status = parsedData.OverallStatus?.Trim().ToUpperInvariant() ?? "";
                            var isFinished = status == "MATCH FINISHED - WON" || status == "MATCH WON" || status == "WON" ||
                                             status == "MATCH FINISHED - LOST" || status == "MATCH LOST" || status == "LOST" ||
                                             status == "MATCH FINISHED - VOID" || status == "MATCH VOID" || status == "VOID" ||
                                             status == "UNKNOWN";

                            string? bestStartTimeIso = parsedData.Legs?
                                .Where(l => l.Outcome?.ToUpperInvariant() == "PENDING" && !string.IsNullOrEmpty(l.MatchStartTimeIso))
                                .OrderBy(l => DateTime.TryParse(l.MatchStartTimeIso, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out DateTime d) ? d : DateTime.MaxValue)
                                .FirstOrDefault()?.MatchStartTimeIso;
                                
                            if (string.IsNullOrEmpty(bestStartTimeIso))
                            {
                                bestStartTimeIso = parsedData.Legs?
                                    .Where(l => !string.IsNullOrEmpty(l.MatchStartTimeIso))
                                    .OrderBy(l => DateTime.TryParse(l.MatchStartTimeIso, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out DateTime d) ? d : DateTime.MaxValue)
                                    .FirstOrDefault()?.MatchStartTimeIso;
                            }
                            if (!string.IsNullOrEmpty(bestStartTimeIso) && DateTime.TryParse(bestStartTimeIso, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out DateTime parsedStart))
                            {
                                dbBet.MatchStartTime = parsedStart;
                            }

                            if (isFinished)
                            {
                                dbBet.NextCheckTime = null;
                            }
                            else if (status == "MATCH NOT STARTED" && dbBet.MatchStartTime.HasValue)
                            {
                                var twoHoursAfter = dbBet.MatchStartTime.Value.AddHours(2);
                                dbBet.NextCheckTime = twoHoursAfter <= DateTime.UtcNow ? DateTime.UtcNow : twoHoursAfter;
                            }
                            else
                            {
                                dbBet.NextCheckTime = DateTime.UtcNow.AddMinutes(60);
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
