#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using BettingApp.Data;
using BettingApp.Services;
using BettingApp.Models;

namespace BettingApp.Components.Pages;

// Gemini AI vision slip extraction and outcome verification
public partial class Admin
{
    // --- AI Vision & Outcome State ---
    private bool showAiExtractionDetails = false;
    private bool showFullAnalysis = false;
    private HashSet<int> AnalyzingImageBetIds = new();
    private HashSet<int> ConfirmingOutcomeBetIds = new();
    private BettingApp.Services.AiVisionExtractionResult? AiVisionResult = null;
    private BettingApp.Services.AiOutcomeResultData? ParsedAiOutcome = null;
    private BettingApp.Services.AiVisionLeg? ActiveLookupLeg = null;
    private string? AiVisionError = null;

    private bool IsCheckingOutcome(int betId)
    {
        return ConfirmingOutcomeBetIds.Contains(betId) || BettingApp.Services.AiVisionService.ProcessingOutcomeBetIds.ContainsKey(betId);
    }

    private bool IsAnalyzingAutoRead(int betId)
    {
        return AnalyzingImageBetIds.Contains(betId) || BettingApp.Services.AiVisionService.ProcessingAutoReadBetIds.ContainsKey(betId);
    }

    private void SaveAiVisionResultToDatabase()
    {
        if (reviewingBetModel == null) return;
        
        var bId = reviewingBetModel.Id;
        var currentAiVisionResultJson = AiVisionResult != null ? System.Text.Json.JsonSerializer.Serialize(AiVisionResult) : null;
        var legStartTimes = AiVisionResult?.Legs?.Select(l => l.StartTime).ToList();
        var oddsSnapshot = LegOddsCache
            .Where(kvp => !kvp.Value.IsLoading)
            .ToDictionary(k => k.Key, v => v.Value);
        if (oddsSnapshot.Count > 0)
        {
            _oddsSavedForCurrentCache = true;
        }

        DateTime? matchStartTime = null;
        
        if (AiVisionResult != null)
        {
            bool allScheduled = AiVisionResult.Legs != null && AiVisionResult.Legs.Count > 0 && AiVisionResult.Legs.All(l => l.StartTime > DateTime.MinValue);
            if (allScheduled)
            {
                matchStartTime = AiVisionResult.Legs!.Min(l => l.StartTime);
            }
            reviewingBetModel.MatchStartTime = matchStartTime;
            if (originalBetModel != null) originalBetModel.MatchStartTime = matchStartTime;
            
            reviewingBetModel.AiVisionResultJson = currentAiVisionResultJson;
            if (originalBetModel != null) originalBetModel.AiVisionResultJson = currentAiVisionResultJson;

            if (AiVisionResult.Legs != null && reviewingBetModel.Legs != null && AiVisionResult.Legs.Count == reviewingBetModel.Legs.Count)
            {
                var orderedUiLegs = reviewingBetModel.Legs.OrderBy(l => l.Id).ToList();
                for (int i = 0; i < AiVisionResult.Legs.Count; i++)
                {
                    if (AiVisionResult.Legs[i].StartTime > DateTime.MinValue)
                    {
                        orderedUiLegs[i].StartTime = AiVisionResult.Legs[i].StartTime;
                    }
                    if (oddsSnapshot.TryGetValue(i, out var legResult))
                    {
                        if (!string.IsNullOrEmpty(legResult.SearchResult?.FixtureId))
                        {
                            orderedUiLegs[i].FixtureId = legResult.SearchResult.FixtureId;
                        }
                        if (legResult.MatchedBookieOdds.Count > 0)
                        {
                            orderedUiLegs[i].BookmakerOdds.Clear();
                            foreach (var kvp in legResult.MatchedBookieOdds)
                            {
                                if (kvp.Value.Price > 0)
                                {
                                    string? bookieUrl = kvp.Value.BetslipUrl;
                                    if (string.IsNullOrEmpty(bookieUrl) && legResult.SearchResult?.BookmakerUrls != null)
                                    {
                                        legResult.SearchResult.BookmakerUrls.TryGetValue(kvp.Key, out bookieUrl);
                                    }
                                    orderedUiLegs[i].BookmakerOdds.Add(new BetLegBookmakerOdds
                                    {
                                        BookmakerName = kvp.Key,
                                        OddsValue = (decimal)kvp.Value.Price,
                                        Limit = (decimal?)kvp.Value.Limit,
                                        LookedUpAt = legResult.LookedUpAt,
                                        BookmakerUrl = bookieUrl,
                                        MarketId = kvp.Value.MarketId,
                                        OutcomeId = kvp.Value.OutcomeId,
                                        PlayerId = kvp.Value.PlayerId
                                    });
                                }
                            }
                        }
                    }
                }
            }

            if (matchStartTime.HasValue && pendingBets != null && pendingBets.Any(b => b.Id == bId))
            {
                var pendingItem = pendingBets.FirstOrDefault(b => b.Id == bId);
                if (pendingItem != null) pendingItem.MatchStartTime = matchStartTime;
                pendingBets = pendingBets
                    .OrderBy(b => b.MatchStartTime.HasValue ? 1 : 0)
                    .ThenBy(b => b.MatchStartTime)
                    .ThenByDescending(b => b.CreatedAt)
                    .ToList();
                if (reviewContext == "Pending")
                {
                    currentReviewList = pendingBets;
                    var newIdx = pendingBets.FindIndex(b => b.Id == bId);
                    if (newIdx >= 0) currentReviewIndex = newIdx;
                }
            }
        }

        _ = Task.Run(async () => 
        {
            var betLock = _betOddsSaveLocks.GetOrAdd(bId, _ => new System.Threading.SemaphoreSlim(1, 1));
            await betLock.WaitAsync();
            try
            {
                using var context = DbFactory.CreateDbContext();
                var dbBet = await context.Bets.Include(b => b.AiEvaluation).Include(b => b.Legs).ThenInclude(l => l.BookmakerOdds).FirstOrDefaultAsync(b => b.Id == bId);
                if (dbBet != null)
                {
                    bool startTimeChanged = matchStartTime.HasValue && dbBet.MatchStartTime != matchStartTime;
                    if (currentAiVisionResultJson != null)
                    {
                        if (matchStartTime.HasValue) dbBet.MatchStartTime = matchStartTime;
                        if (dbBet.AiEvaluation == null)
                        {
                            var existingEval = await context.BetAiEvaluations.FirstOrDefaultAsync(a => a.BetId == dbBet.Id);
                            dbBet.AiEvaluation = existingEval ?? new BetAiEvaluation { BetId = dbBet.Id };
                        }
                        dbBet.AiVisionResultJson = currentAiVisionResultJson;
                        
                        // IMPORTANT: Sync the StartTime, FixtureId, and BookmakerOdds to the physical BetLegs
                        if (legStartTimes != null && dbBet.Legs != null && legStartTimes.Count == dbBet.Legs.Count)
                        {
                            var orderedDbLegs = dbBet.Legs.OrderBy(l => l.Id).ToList();
                            for (int i = 0; i < orderedDbLegs.Count; i++)
                            {
                                if (legStartTimes[i] > DateTime.MinValue)
                                {
                                    orderedDbLegs[i].StartTime = legStartTimes[i];
                                }
                                if (oddsSnapshot.TryGetValue(i, out var legResult))
                                {
                                    if (!string.IsNullOrEmpty(legResult.SearchResult?.FixtureId))
                                    {
                                        orderedDbLegs[i].FixtureId = legResult.SearchResult.FixtureId;
                                    }
                                    if (legResult.MatchedBookieOdds.Count > 0)
                                    {
                                        orderedDbLegs[i].BookmakerOdds.Clear();
                                        foreach (var kvp in legResult.MatchedBookieOdds)
                                        {
                                            if (kvp.Value.Price > 0)
                                            {
                                                string? bookieUrl = kvp.Value.BetslipUrl;
                                                if (string.IsNullOrEmpty(bookieUrl) && legResult.SearchResult?.BookmakerUrls != null)
                                                {
                                                    legResult.SearchResult.BookmakerUrls.TryGetValue(kvp.Key, out bookieUrl);
                                                }
                                                orderedDbLegs[i].BookmakerOdds.Add(new BetLegBookmakerOdds
                                                {
                                                    BookmakerName = kvp.Key,
                                                    OddsValue = (decimal)kvp.Value.Price,
                                                    Limit = (decimal?)kvp.Value.Limit,
                                                    LookedUpAt = legResult.LookedUpAt,
                                                    BookmakerUrl = bookieUrl,
                                                    MarketId = kvp.Value.MarketId,
                                                    OutcomeId = kvp.Value.OutcomeId,
                                                    PlayerId = kvp.Value.PlayerId
                                                });
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    await context.SaveChangesAsync();
                    if (startTimeChanged)
                    {
                        UiNotifier.NotifyAdmin("PendingUpdate");
                    }
                }
            }
            catch { /* Best effort */ }
            finally
            {
                betLock.Release();
            }
        });
    }

    private void ConfirmOutcomeAsync()
    {
        if (reviewingBetModel == null) return;
        
        var capturingBetId = reviewingBetModel.Id;
        var currentBet = reviewingBetModel;
        var currentOriginal = originalBetModel;

        if (!BettingApp.Services.AiVisionService.ProcessingOutcomeBetIds.TryAdd(capturingBetId, true))
        {
            return;
        }

        ConfirmingOutcomeBetIds.Add(capturingBetId);
        if (reviewingBetModel.Id == capturingBetId) errorMessage = null;
        StateHasChanged();
        try { _ = reviewModalRef.FocusAsync(); } catch { }

        _ = Task.Run(async () =>
        {
            try
            {
                // 1. If we don't have the AI extraction yet, we must read the image first!
                if (string.IsNullOrEmpty(currentBet.AiVisionResultJson))
                {
                    if (string.IsNullOrEmpty(currentBet.ScreenshotUrl))
                    {
                        await InvokeAsync(() =>
                        {
                            if (reviewingBetModel?.Id == capturingBetId) errorMessage = "No screenshot available to analyze.";
                        });
                        return;
                    }

                    var (result, error) = await AiVision.ExtractBetSlipDataAsync(currentBet.ScreenshotUrl, capturingBetId);
                    if (error != null)
                    {
                        await InvokeAsync(async () =>
                        {
                            if (reviewingBetModel?.Id == capturingBetId) 
                            {
                                errorMessage = $"Failed to extract bet data: {error}";
                                reviewingBetModel.AiVisionError = error;
                            }
                            var listM = pendingBets.FirstOrDefault(b => b.Id == capturingBetId) 
                                     ?? approvedBets.FirstOrDefault(b => b.Id == capturingBetId) 
                                     ?? allApprovedBets.FirstOrDefault(b => b.Id == capturingBetId) 
                                     ?? completedBets.FirstOrDefault(b => b.Id == capturingBetId);
                            if (listM != null) listM.AiVisionError = error;
                            currentBet.AiVisionError = error;
                            if (currentOriginal != null && currentOriginal.Id == capturingBetId) currentOriginal.AiVisionError = error;
                            using var errContext = DbFactory.CreateDbContext();
                            var errBet = await errContext.Bets.Include(b => b.AiEvaluation).FirstOrDefaultAsync(b => b.Id == capturingBetId);
                            if (errBet != null)
                            {
                                if (errBet.AiEvaluation == null)
                                {
                                    var existingEval = await errContext.BetAiEvaluations.FirstOrDefaultAsync(a => a.BetId == errBet.Id);
                                    errBet.AiEvaluation = existingEval ?? new BetAiEvaluation { BetId = errBet.Id };
                                }
                                errBet.AiVisionError = error;
                                await errContext.SaveChangesAsync();
                            }
                        });
                        return;
                    }
                    
                    var json = System.Text.Json.JsonSerializer.Serialize(result);
                    currentBet.AiVisionResultJson = json;
                    currentBet.AiVisionError = null;
                    if (currentOriginal != null && currentOriginal.Id == capturingBetId) 
                    {
                        currentOriginal.AiVisionResultJson = json;
                        currentOriginal.AiVisionError = null;
                    }
                    
                    using var extractContext = DbFactory.CreateDbContext();
                    var dbBetExtract = await extractContext.Bets.Include(b => b.AiEvaluation).Include(b => b.Legs).ThenInclude(l => l.BookmakerOdds).FirstOrDefaultAsync(b => b.Id == capturingBetId);
                    if (dbBetExtract != null)
                    {
                        if (dbBetExtract.AiEvaluation == null)
                        {
                            var existingEval = await extractContext.BetAiEvaluations.FirstOrDefaultAsync(a => a.BetId == dbBetExtract.Id);
                            dbBetExtract.AiEvaluation = existingEval ?? new BetAiEvaluation { BetId = dbBetExtract.Id };
                        }
                        dbBetExtract.AiVisionResultJson = json;
                        dbBetExtract.AiVisionError = null;
                        await extractContext.SaveChangesAsync();
                    }
                    
                    await InvokeAsync(() =>
                    {
                        if (reviewingBetModel?.Id == capturingBetId) 
                        {
                            reviewingBetModel.AiVisionResultJson = json;
                            reviewingBetModel.AiVisionError = null;
                            AiVisionResult = result;
                            AiVisionError = null;
                            StateHasChanged();
                        }
                    });
                }

                // 2. We now have the JSON payload for the legs. Ask Gemini to search Google for the outcome.
                var outcome = await AiVision.ConfirmOutcomeAsync(currentBet.AiVisionResultJson, currentBet.CreatedAt, currentBet.MatchStartTime, capturingBetId);
                
                if (!string.IsNullOrEmpty(outcome) && !outcome.StartsWith("Error") && !outcome.StartsWith("Exception"))
                {
                    BettingApp.Services.AiOutcomeResultData? parsedData = null;
                    try {
                        parsedData = System.Text.Json.JsonSerializer.Deserialize<BettingApp.Services.AiOutcomeResultData>(
                            outcome, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                        );
                    } catch { }

                    await InvokeAsync(async () =>
                    {
                        using var context = DbFactory.CreateDbContext();
                        var dbBet = await context.Bets.Include(b => b.AiEvaluation).Include(b => b.Legs).ThenInclude(l => l.BookmakerOdds).FirstOrDefaultAsync(b => b.Id == capturingBetId);
                        if (dbBet != null)
                        {
                            if (dbBet.AiEvaluation == null)
                            {
                                var existingEval = await context.BetAiEvaluations.FirstOrDefaultAsync(a => a.BetId == dbBet.Id);
                                dbBet.AiEvaluation = existingEval ?? new BetAiEvaluation { BetId = dbBet.Id };
                            }
                            dbBet.AiOutcomeResult = outcome;
                            string? targetAutoSettleStatus = null;

                            BettingApp.Services.AiVisionExtractionResult? currentAiVision = null;
                            if (parsedData != null)
                            {
                                var status = parsedData.OverallStatus?.Trim().ToUpperInvariant() ?? "";
                                var isFinished = 
                                    status == "MATCH FINISHED - WON" || status == "MATCH WON" || status == "WON" ||
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

                                if (!string.IsNullOrEmpty(bestStartTimeIso) && DateTime.TryParse(bestStartTimeIso, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out DateTime st))
                                {
                                    dbBet.MatchStartTime = st;
                                }

                                // Securely store start times found by Google into the actual extraction legs
                                bool aiVisionUpdated = false;
                                if (!string.IsNullOrEmpty(dbBet.AiVisionResultJson))
                                {
                                    try { currentAiVision = System.Text.Json.JsonSerializer.Deserialize<BettingApp.Services.AiVisionExtractionResult>(dbBet.AiVisionResultJson); } catch {}
                                }

                                if (parsedData.Legs != null && currentAiVision?.Legs != null)
                                {
                                    foreach (var parsedLeg in parsedData.Legs)
                                    {
                                        if (!string.IsNullOrEmpty(parsedLeg.Match) && !string.IsNullOrEmpty(parsedLeg.MatchStartTimeIso))
                                        {
                                            if (DateTime.TryParse(parsedLeg.MatchStartTimeIso, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out DateTime parsedUtc))
                                            {
                                                foreach (var vLeg in currentAiVision.Legs.Where(l => !string.IsNullOrEmpty(l.Match) && string.Equals(l.Match, parsedLeg.Match, StringComparison.OrdinalIgnoreCase)))
                                                {
                                                    if (vLeg.StartTime == null || vLeg.StartTime == DateTime.MinValue)
                                                    {
                                                        vLeg.StartTime = parsedUtc;
                                                        aiVisionUpdated = true;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }

                                if (aiVisionUpdated && currentAiVision != null)
                                {
                                    var updatedJson = System.Text.Json.JsonSerializer.Serialize(currentAiVision);
                                    dbBet.AiVisionResultJson = updatedJson;
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
                                            
                                            if (!string.IsNullOrEmpty(parsedLeg.MatchStartTimeIso) && DateTime.TryParse(parsedLeg.MatchStartTimeIso, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out DateTime legStartTime))
                                            {
                                                dbLeg.StartTime = legStartTime;
                                            }
                                        }
                                    }
                                }
                                // --------------------------

                                // --- GATEKEEPER AUTO-SETTLEMENT LOGIC ---
                                if (isFinished && dbBet.Status == "Approved")
                                {
                                    targetAutoSettleStatus = BettingApp.Services.BetSchedulingLogic.DetermineAutoSettleStatus(parsedData.OverallStatus, parsedData.Legs);
                                }
                                // ----------------------------------------
                            }

                            // --- UPDATE NEXT CHECK TIME ---
                            dbBet.NextCheckTime = (dbBet.Status == "Won" || dbBet.Status == "Lost" || dbBet.Status == "Void")
                                ? BettingApp.Services.BetSchedulingLogic.CalculateFollowUpCheckTimeForSettledBet(dbBet)
                                : BettingApp.Services.BetSchedulingLogic.CalculateNextCheckTime(dbBet);
                            
                            await context.SaveChangesAsync();
                            
                            // In-memory sync: update outcome cache so badges immediately get the result
                            if (parsedData != null)
                            {
                                _outcomeCache[capturingBetId] = (outcome, parsedData);
                            }

                            // In-memory sync: update list model so the table row displays the fresh badges immediately
                            var listModel = pendingBets.FirstOrDefault(b => b.Id == capturingBetId) 
                                         ?? approvedBets.FirstOrDefault(b => b.Id == capturingBetId) 
                                         ?? allApprovedBets.FirstOrDefault(b => b.Id == capturingBetId) 
                                         ?? completedBets.FirstOrDefault(b => b.Id == capturingBetId);
                            if (listModel != null)
                            {
                                listModel.AiOutcomeResult = outcome;
                                listModel.NextCheckTime = dbBet.NextCheckTime;
                                listModel.MatchStartTime = dbBet.MatchStartTime;
                                listModel.AiVisionResultJson = dbBet.AiVisionResultJson;
                                listModel.AiVisionError = null;
                                if (dbBet.Legs != null)
                                {
                                    listModel.Legs = dbBet.Legs.ToList();
                                }
                            }

                            if (currentOriginal != null && currentOriginal.Id == capturingBetId)
                            {
                                currentOriginal.AiOutcomeResult = outcome;
                                currentOriginal.NextCheckTime = dbBet.NextCheckTime;
                                currentOriginal.MatchStartTime = dbBet.MatchStartTime;
                                currentOriginal.AiVisionResultJson = dbBet.AiVisionResultJson;
                                currentOriginal.AiVisionError = null;
                                if (dbBet.Legs != null)
                                {
                                    currentOriginal.Legs = dbBet.Legs.ToList();
                                }
                            }

                            currentBet.AiOutcomeResult = outcome;
                            currentBet.NextCheckTime = dbBet.NextCheckTime;
                            currentBet.MatchStartTime = dbBet.MatchStartTime;
                            currentBet.AiVisionResultJson = dbBet.AiVisionResultJson;
                            currentBet.AiVisionError = null;
                            if (dbBet.Legs != null)
                            {
                                currentBet.Legs = dbBet.Legs.ToList();
                            }

                            // In-memory sync: update open modal if currently reviewing this bet
                            if (reviewingBetModel?.Id == capturingBetId)
                            {
                                reviewingBetModel.AiOutcomeResult = outcome;
                                reviewingBetModel.NextCheckTime = dbBet.NextCheckTime;
                                reviewingBetModel.MatchStartTime = dbBet.MatchStartTime;
                                reviewingBetModel.AiVisionResultJson = dbBet.AiVisionResultJson;
                                reviewingBetModel.AiVisionError = null;
                                ParsedAiOutcome = parsedData;
                                AiVisionError = null;
                                if (dbBet.Legs != null)
                                {
                                    reviewingBetModel.Legs = dbBet.Legs.ToList();
                                }
                                if (currentAiVision != null)
                                {
                                    AiVisionResult = currentAiVision;
                                }
                                else if (AiVisionResult == null && !string.IsNullOrEmpty(dbBet.AiVisionResultJson))
                                {
                                    try { AiVisionResult = System.Text.Json.JsonSerializer.Deserialize<BettingApp.Services.AiVisionExtractionResult>(dbBet.AiVisionResultJson); } catch {}
                                }
                            }

                            if (targetAutoSettleStatus != null)
                            {
                                await ProcessBet(currentBet, targetAutoSettleStatus, isAutoSettled: true, showOverlay: false);
                            }
                            else
                            {
                                await LoadPendingBets();
                                await LoadApprovedBets();
                                await LoadCompletedBets();
                                StateHasChanged();
                            }
                        }
                    });
                }
                else
                {
                    string errText = outcome ?? "Failed to check outcome.";
                    await InvokeAsync(async () =>
                    {
                        if (reviewingBetModel?.Id == capturingBetId) 
                        {
                            reviewingBetModel.AiVisionError = errText;
                            AiVisionError = errText;
                        }
                        var listModel = pendingBets.FirstOrDefault(b => b.Id == capturingBetId) 
                                     ?? approvedBets.FirstOrDefault(b => b.Id == capturingBetId) 
                                     ?? allApprovedBets.FirstOrDefault(b => b.Id == capturingBetId) 
                                     ?? completedBets.FirstOrDefault(b => b.Id == capturingBetId);
                        if (listModel != null) listModel.AiVisionError = errText;
                        if (currentOriginal != null && currentOriginal.Id == capturingBetId) currentOriginal.AiVisionError = errText;
                        currentBet.AiVisionError = errText;
                        
                        using var errContext = DbFactory.CreateDbContext();
                        var errBet = await errContext.Bets.Include(b => b.AiEvaluation).FirstOrDefaultAsync(b => b.Id == capturingBetId);
                        if (errBet != null)
                        {
                            if (errBet.AiEvaluation == null)
                            {
                                var existingEval = await errContext.BetAiEvaluations.FirstOrDefaultAsync(a => a.BetId == errBet.Id);
                                errBet.AiEvaluation = existingEval ?? new BetAiEvaluation { BetId = errBet.Id };
                            }
                            errBet.AiVisionError = errText;
                            await errContext.SaveChangesAsync();
                        }
                        StateHasChanged();
                    });
                }
            }
            finally
            {
                BettingApp.Services.AiVisionService.ProcessingOutcomeBetIds.TryRemove(capturingBetId, out _);
                try
                {
                    if (!_isDisposed)
                    {
                        await InvokeAsync(() =>
                        {
                            ConfirmingOutcomeBetIds.Remove(capturingBetId);
                            shouldFocusReviewModal = true;
                            StateHasChanged();
                        });
                    }
                }
                catch { }
                UiNotifier.NotifyAdmin("Update");
            }
        });
    }
    private async Task AnalyzeImageAsync()
    {
        if (reviewingBetModel == null || string.IsNullOrEmpty(reviewingBetModel.ScreenshotUrl)) return;
        
        var capturingBetId = reviewingBetModel.Id;
        var capturingScreenshot = reviewingBetModel.ScreenshotUrl;
        AnalyzingImageBetIds.Add(capturingBetId);
        AiVisionResult = null;
        ActiveLookupLeg = null;
        AiVisionError = null;
        StateHasChanged();
        try { await reviewModalRef.FocusAsync(); } catch { }

        try
        {
            var (result, error) = await AiVision.ExtractBetSlipDataAsync(capturingScreenshot, capturingBetId);
            
            bool isModalOpenForThisBet = reviewingBetModel != null && reviewingBetModel.Id == capturingBetId;
            var listModel = pendingBets.FirstOrDefault(b => b.Id == capturingBetId) ?? approvedBets.FirstOrDefault(b => b.Id == capturingBetId) ?? allApprovedBets.FirstOrDefault(b => b.Id == capturingBetId) ?? completedBets.FirstOrDefault(b => b.Id == capturingBetId);
            
            if (error != null)
            {
                if (isModalOpenForThisBet)
                {
                    AiVisionError = error;
                    reviewingBetModel!.AiVisionError = error;
                }
                if (listModel != null) listModel.AiVisionError = error;
                
                try 
                {
                    using var context = DbFactory.CreateDbContext();
                    var dbBet = await context.Bets.Include(b => b.AiEvaluation).Include(b => b.Legs).ThenInclude(l => l.BookmakerOdds).FirstOrDefaultAsync(b => b.Id == capturingBetId);
                    if (dbBet != null)
                    {
                        if (dbBet.AiEvaluation == null)
                        {
                            var existingEval = await context.BetAiEvaluations.FirstOrDefaultAsync(a => a.BetId == dbBet.Id);
                            dbBet.AiEvaluation = existingEval ?? new BetAiEvaluation { BetId = dbBet.Id };
                        }
                        dbBet.AiVisionError = error;
                        await context.SaveChangesAsync();
                    }
                } 
                catch { }
            }
            else if (result != null)
            {
                var json = System.Text.Json.JsonSerializer.Serialize(result);
                
                if (isModalOpenForThisBet)
                {
                    AiVisionResult = result;
                    reviewingBetModel!.AiVisionResultJson = json;
                    reviewingBetModel.AiVisionError = null;
                    AiVisionError = null;
                    
                    reviewingBetModel.IsLive = result.IsLive;
                    reviewingBetModel.IsBetBuilder = result.IsBetBuilder;
                    reviewingBetModel.Bookmaker = result.Bookmaker ?? "";
                    
                    if (!string.IsNullOrEmpty(result.TotalOdds))
                    {
                        var cleanOdds = result.TotalOdds.Replace(",", ".");
                        if (decimal.TryParse(cleanOdds, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal odds))
                        {
                            reviewingBetModel.Odds = Math.Floor(odds / 0.05m) * 0.05m;
                        }
                    }

                    if (!string.IsNullOrEmpty(result.Stake))
                    {
                        var rawStake = result.Stake.Trim().ToLower();
                        int multiplier = rawStake.EndsWith("k") ? 1000 : (rawStake.EndsWith("m") ? 1000000 : 1);
                        var cleanStake = new string(rawStake.Where(c => char.IsDigit(c) || c == '.' || c == ',').ToArray()).Replace(",", ".");
                        if (decimal.TryParse(cleanStake, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal stakeDecimal))
                        {
                            reviewingBetModel.AmountNOK = (int)(stakeDecimal * multiplier);
                        }
                    }
                }
                
                if (listModel != null)
                {
                    listModel.AiVisionResultJson = json;
                    listModel.AiVisionError = null;
                    
                    listModel.IsLive = result.IsLive;
                    listModel.IsBetBuilder = result.IsBetBuilder;
                    listModel.Bookmaker = result.Bookmaker ?? "";
                }
                
                try 
                {
                    using var context = DbFactory.CreateDbContext();
                    var dbBet = await context.Bets.Include(b => b.AiEvaluation).Include(b => b.Legs).ThenInclude(l => l.BookmakerOdds).FirstOrDefaultAsync(b => b.Id == capturingBetId);
                    if (dbBet != null)
                    {
                        if (dbBet.AiEvaluation == null)
                        {
                            var existingEval = await context.BetAiEvaluations.FirstOrDefaultAsync(a => a.BetId == dbBet.Id);
                            dbBet.AiEvaluation = existingEval ?? new BetAiEvaluation { BetId = dbBet.Id };
                        }
                        dbBet.AiVisionResultJson = json;
                        dbBet.AiVisionError = null;
                        
                        dbBet.IsLive = result.IsLive;
                        dbBet.IsBetBuilder = result.IsBetBuilder;
                        dbBet.Bookmaker = result.Bookmaker ?? "";
                        
                        if (result.Legs != null)
                        {
                            dbBet.Legs ??= new List<BettingApp.Data.BetLeg>();
                            dbBet.Legs.Clear();
                            foreach (var l in result.Legs)
                            {
                                dbBet.Legs.Add(new BettingApp.Data.BetLeg
                                {
                                    Match = l.Match ?? "",
                                    Sport = l.Sport ?? "",
                                    Market = l.Market ?? "",
                                    Selection = l.Selection ?? "",
                                    Odds = l.Odds ?? "",
                                    StartTime = l.StartTime,
                                    Outcome = "Pending",
                                    VerificationSource = "Unknown",
                                    Stats = ""
                                });
                            }
                        }
                        
                        if (isModalOpenForThisBet && reviewingBetModel != null)
                        {
                            reviewingBetModel.Legs = dbBet.Legs.ToList();
                        }
                        
                        await context.SaveChangesAsync();
                    }
                } 
                catch { }
            }

        // We explicitly do NOT auto-search odds here to prevent unnecessary API calls 
            // and background logs on startup. Only lookup odds when manually triggered!
        }
        finally
        {
            AnalyzingImageBetIds.Remove(capturingBetId);
            shouldFocusReviewModal = true;
            StateHasChanged();
        }
    }

}
