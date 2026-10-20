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

namespace BettingApp.Services
{
    public class BetMonitoringService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<BetMonitoringService> _logger;
        private readonly AiVisionService _aiVisionService;

        private static readonly SemaphoreSlim _wakeSignal = new(0);

        public static void WakeUp()
        {
            if (_wakeSignal.CurrentCount == 0)
            {
                try { _wakeSignal.Release(); } catch { }
            }
        }

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

                // Wait up to 1 minute, or wake up immediately if signaled by bet approval or admin action
                try
                {
                    await _wakeSignal.WaitAsync(TimeSpan.FromMinutes(1), stoppingToken);
                }
                catch (OperationCanceledException) { }
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
            var uiNotifier = scope.ServiceProvider.GetService<UiNotificationService>();

            // Execute in parallel (up to 3 concurrent checks to prevent Gemini 429 Rate Limits)
            await Parallel.ForEachAsync(dueBets, new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = stoppingToken }, async (bet, ct) =>
            {
                try
                {
                    // Refresh bet from DB using a newly scoped context (since DbContext is not thread-safe)
                    using var taskContext = dbFactory.CreateDbContext();
                    var dbBet = await taskContext.Bets.Include(b => b.Legs).Include(b => b.AiEvaluation).FirstOrDefaultAsync(b => b.Id == bet.Id, ct);
                    if (dbBet == null || (dbBet.Status != "Approved" && dbBet.Status != "Won" && dbBet.Status != "Lost" && dbBet.Status != "Void")) return;
                    
                    if (dbBet.AiEvaluation == null)
                    {
                        var existingEval = await taskContext.BetAiEvaluations.FirstOrDefaultAsync(a => a.BetId == dbBet.Id, ct);
                        dbBet.AiEvaluation = existingEval ?? new BetAiEvaluation { BetId = dbBet.Id };
                    }
                    
                    if (dbBet.AiEvaluation.AiOutcomeResult == "Admin Override")
                    {
                        if (dbBet.NextCheckTime != null)
                        {
                            dbBet.NextCheckTime = null;
                            await taskContext.SaveChangesAsync(ct);
                        }
                        return;
                    }

                    if (!AiVisionService.ProcessingOutcomeBetIds.TryAdd(dbBet.Id, true))
                    {
                        return; // Already being checked by another thread or admin
                    }

                    try
                    {
                        uiNotifier?.NotifyAdmin("Update");

                if (string.IsNullOrEmpty(dbBet.AiEvaluation.AiVisionResultJson))
                {
                    if (string.IsNullOrEmpty(dbBet.ScreenshotUrl)) return;
                    
                    var (extractionResult, error) = await _aiVisionService.ExtractBetSlipDataAsync(dbBet.ScreenshotUrl, dbBet.Id);
                    if (error != null)
                    {
                        if (dbBet.AiEvaluation.Id == 0)
                        {
                            var existingEval = await taskContext.BetAiEvaluations.AsNoTracking().FirstOrDefaultAsync(a => a.BetId == dbBet.Id, ct);
                            if (existingEval != null)
                            {
                                dbBet.AiEvaluation.Id = existingEval.Id;
                                taskContext.Entry(dbBet.AiEvaluation).State = EntityState.Modified;
                            }
                        }

                        dbBet.AiEvaluation.AiVisionError = error;
                        // Use the standard check outcome scheduling interval if extraction fails
                        dbBet.NextCheckTime = DateTime.UtcNow.AddMinutes(60);
                        await taskContext.SaveChangesAsync(ct);
                        return;
                    }
                    
                    if (extractionResult != null)
                    {
                        dbBet.AiEvaluation.AiVisionResultJson = System.Text.Json.JsonSerializer.Serialize(extractionResult);
                        dbBet.AiEvaluation.AiVisionError = null;
                        dbBet.IsLive = extractionResult.IsLive;
                        dbBet.IsBetBuilder = extractionResult.IsBetBuilder;
                        dbBet.Bookmaker = extractionResult.Bookmaker ?? "";
                        
                        if (extractionResult.Legs != null)
                        {
                            dbBet.Legs.Clear();
                            foreach (var l in extractionResult.Legs)
                            {
                                dbBet.Legs.Add(new BetLeg
                                {
                                    Match = l.Match ?? "",
                                    Sport = l.Sport ?? "",
                                    Market = l.Market ?? "",
                                    Selection = l.Selection ?? "",
                                    Odds = l.Odds ?? "",
                                    StartTime = l.StartTime
                                });
                            }
                        }

                        if (dbBet.AiEvaluation.Id == 0)
                        {
                            var existingEval = await taskContext.BetAiEvaluations.AsNoTracking().FirstOrDefaultAsync(a => a.BetId == dbBet.Id, ct);
                            if (existingEval != null)
                            {
                                dbBet.AiEvaluation.Id = existingEval.Id;
                                taskContext.Entry(dbBet.AiEvaluation).State = EntityState.Modified;
                            }
                        }

                        await taskContext.SaveChangesAsync(ct);
                    }
                }

                if (string.IsNullOrEmpty(dbBet.AiEvaluation.AiVisionResultJson)) return;

                string? result = await _aiVisionService.ConfirmOutcomeAsync(dbBet.AiEvaluation.AiVisionResultJson, dbBet.CreatedAt, dbBet.MatchStartTime, dbBet.Id);
                
                // If AiEvaluation was newly instantiated in memory, verify if another process created it in DB during the AI call
                if (dbBet.AiEvaluation.Id == 0)
                {
                    var existingEval = await taskContext.BetAiEvaluations.AsNoTracking().FirstOrDefaultAsync(a => a.BetId == dbBet.Id, ct);
                    if (existingEval != null)
                    {
                        dbBet.AiEvaluation.Id = existingEval.Id;
                        taskContext.Entry(dbBet.AiEvaluation).State = EntityState.Modified;
                    }
                }

                try 
                {
                    BettingApp.Services.AiOutcomeResultData? parsedData = null;
                    if (!string.IsNullOrEmpty(result) && !result.StartsWith("Error", StringComparison.OrdinalIgnoreCase) && !result.StartsWith("Exception", StringComparison.OrdinalIgnoreCase))
                    {
                        try 
                        {
                            parsedData = System.Text.Json.JsonSerializer.Deserialize<BettingApp.Services.AiOutcomeResultData>(
                                result, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                            );
                        }
                        catch { }
                    }

                    if (parsedData == null)
                    {
                        if (string.IsNullOrEmpty(dbBet.AiEvaluation.AiOutcomeResult))
                        {
                            dbBet.AiEvaluation.AiOutcomeResult = result;
                        }
                        dbBet.NextCheckTime = DateTime.UtcNow.AddMinutes(15);
                        _logger.LogWarning($"[Bet #{dbBet.Id}] [Outcome Check] Outcome check did not return valid JSON. Scheduling next check in 15 minutes.");
                    }
                    else
                    {
                        dbBet.AiEvaluation.AiOutcomeResult = result;

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

                        // --- UPDATE DB BET LEGS ---
                        if (parsedData.Legs != null && dbBet.Legs != null && dbBet.Legs.Count > 0)
                        {
                            var dbLegsList = dbBet.Legs.OrderBy(l => l.Id).ToList();
                            for (int i = 0; i < dbLegsList.Count; i++)
                            {
                                var dbLeg = dbLegsList[i];
                                var parsedLeg = parsedData.Legs.Count > i ? parsedData.Legs[i] : parsedData.Legs.FirstOrDefault(l => string.Equals(l.Match, dbLeg.Match, StringComparison.OrdinalIgnoreCase));
                                
                                if (parsedLeg != null)
                                {
                                    dbLeg.Outcome = string.IsNullOrEmpty(parsedLeg.Outcome) ? "Pending" : parsedLeg.Outcome;
                                    dbLeg.Stats = parsedLeg.Stats ?? "";
                                    dbLeg.VerificationSource = string.IsNullOrEmpty(parsedLeg.VerificationSource) ? "Unknown" : parsedLeg.VerificationSource;
                                    
                                    if (!string.IsNullOrEmpty(parsedLeg.MatchStartTimeIso) && DateTime.TryParse(parsedLeg.MatchStartTimeIso, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out DateTime st))
                                    {
                                        dbLeg.StartTime = st;
                                    }
                                }
                            }
                        }
                        // --------------------------

                        if (isFinished)
                        {
                            dbBet.NextCheckTime = null;

                            // --- GATEKEEPER AUTO-SETTLEMENT LOGIC ---
                            if (dbBet.Status == "Approved")
                            {
                                string? targetStatus = BetSchedulingLogic.DetermineAutoSettleStatus(parsedData.OverallStatus, parsedData.Legs);
                                
                                if (targetStatus != null)
                                {
                                    // Save the extracted legs and outcome string first
                                    await taskContext.SaveChangesAsync(ct);

                                    var betProcessor = scope.ServiceProvider.GetRequiredService<BettingApp.Services.BetProcessingService>();
                                    var processResult = await betProcessor.ProcessBetAsync(
                                        betId: dbBet.Id,
                                        newStatus: targetStatus,
                                        adminName: "SYSTEM_AUTO",
                                        expectedOriginalStatus: "Approved",
                                        newAmount: (decimal)(dbBet.AmountNOK ?? 0),
                                        newOdds: dbBet.Odds,
                                        isAutoSettled: true
                                    );

                                    if (processResult.Success)
                                    {
                                        _logger.LogInformation($"Auto-settled Bet {dbBet.Id} to {targetStatus}");
                                    }
                                    else
                                    {
                                        _logger.LogWarning($"Failed to auto-settle Bet {dbBet.Id}: {processResult.ErrorMessage}");
                                    }

                                    anyUpdates = true;
                                    return; // We already saved and processed, skip the final SaveChangesAsync below
                                }
                            }
                            // ----------------------------------------
                        }
                        else
                        {
                            dbBet.NextCheckTime = BettingApp.Services.BetSchedulingLogic.CalculateNextCheckTime(dbBet);
                        }
                    }
                }
                catch 
                {
                    // If AI fails to return valid JSON, try again in 15 mins
                    dbBet.NextCheckTime = DateTime.UtcNow.AddMinutes(15);
                }

                await taskContext.SaveChangesAsync(ct);
                anyUpdates = true;
            }
            finally
            {
                AiVisionService.ProcessingOutcomeBetIds.TryRemove(dbBet.Id, out _);
                uiNotifier?.NotifyAdmin("Update");
            }
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    _logger.LogWarning($"[Bet #{bet.Id}] Concurrency conflict detected: bet was modified by an admin or another process while checking outcome ({ex.Message}). Skipping update; will re-evaluate on next cycle.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"[Bet #{bet.Id}] Unexpected error processing due bet in BetMonitoringService.");
                }
            });
            
            if (anyUpdates)
            {
                // Notify UI about the update once for all bets
                uiNotifier?.NotifyAdmin("Update");
            }
        }
    }
}
