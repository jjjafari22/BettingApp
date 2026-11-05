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

// Bet review modal state, keyboard navigation, user stats, and bet processing
public partial class Admin
{
    // --- Review Modal State ---
    [SupplyParameterFromQuery]
    public int? ReviewBetId { get; set; }

    private Bet? reviewingBetModel;
    private Bet? originalBetModel;
    private decimal currentReviewUserFreeBalance = 0;
    private decimal currentReviewUserLifetimeProfit = 0;

    private ElementReference reviewModalRef;
    private bool shouldFocusReviewModal = false;

    private List<Bet> currentReviewList = new();
    private int currentReviewIndex = -1;
    private string reviewContext = "";

    private bool showModalDropdown = false;
    private bool showMobileScreenshot = false;
    private string resolvedScreenshotUrl = "";
    private Dictionary<string, string> localImageCache = new();
    private int activeScreenshotIndex = 0;
    private List<string> reviewScreenshotUrls = new();
    private List<Bet> mergeCandidatePendingBets = new();
    private bool isMergingBet = false;

    private void ToggleModalDropdown()
    {
        showModalDropdown = !showModalDropdown;
        StateHasChanged();
    }
    
    private async Task ExecuteAutoReadFromDropdown()
    {
        showModalDropdown = false;
        await AnalyzeImageAsync();
    }

    private void ExecuteCheckOutcomeFromDropdown()
    {
        showModalDropdown = false;
        ConfirmOutcomeAsync();
    }

    private async Task ToggleAdminOverride()
    {
        showModalDropdown = false;
        if (reviewingBetModel != null)
        {
            using var context = DbFactory.CreateDbContext();
            var dbBet = await context.Bets.Include(b => b.AiEvaluation).Include(b => b.Legs).ThenInclude(l => l.BookmakerOdds).FirstOrDefaultAsync(b => b.Id == reviewingBetModel.Id);
            if (dbBet != null)
            {
                if (dbBet.AiEvaluation == null)
                {
                    var existingEval = await context.BetAiEvaluations.FirstOrDefaultAsync(a => a.BetId == dbBet.Id);
                    dbBet.AiEvaluation = existingEval ?? new BetAiEvaluation { BetId = dbBet.Id };
                }

                if (dbBet.AiOutcomeResult == "Admin Override")
                {
                    dbBet.AiOutcomeResult = ""; // Clear override
                    dbBet.NextCheckTime = DateTime.UtcNow; // Resume monitoring
                }
                else
                {
                    dbBet.AiOutcomeResult = "Admin Override";
                    dbBet.NextCheckTime = null; // Stop monitoring
                }
                await context.SaveChangesAsync();
                
                if (reviewingBetModel != null)
                {
                    reviewingBetModel.AiOutcomeResult = dbBet.AiOutcomeResult;
                    reviewingBetModel.NextCheckTime = dbBet.NextCheckTime;
                }
                
                await LoadCompletedBets();
                StateHasChanged();
            }
        }
    }

    private string GetScreenshotUrl(string? originalUrl)
    {
        return string.IsNullOrEmpty(resolvedScreenshotUrl) ? (originalUrl ?? "") : resolvedScreenshotUrl;
    }

    private string GetActiveScreenshotUrl()
    {
        string? rawUrl = (reviewScreenshotUrls.Count > 0 && activeScreenshotIndex >= 0 && activeScreenshotIndex < reviewScreenshotUrls.Count)
            ? reviewScreenshotUrls[activeScreenshotIndex]
            : reviewingBetModel?.ScreenshotUrl;
        return GetScreenshotUrl(rawUrl);
    }

    private void SelectScreenshot(int index)
    {
        if (index < 0 || index >= reviewScreenshotUrls.Count) return;
        if (activeScreenshotIndex == index && !string.IsNullOrEmpty(resolvedScreenshotUrl)) return;
        activeScreenshotIndex = index;
        resolvedScreenshotUrl = "";
        _ = LoadResolvedScreenshotUrlAsync(reviewScreenshotUrls[index]);
    }

    private void ShowMobileScreenshotAt(int index)
    {
        if (index >= 0 && index < reviewScreenshotUrls.Count && activeScreenshotIndex != index)
        {
            activeScreenshotIndex = index;
            resolvedScreenshotUrl = "";
            _ = LoadResolvedScreenshotUrlAsync(reviewScreenshotUrls[index]);
        }
        showMobileScreenshot = true;
    }

    private void RefreshMergeCandidatePendingBetsFromMemory()
    {
        if (reviewingBetModel == null || (reviewingBetModel.Status != "Pending" && reviewingBetModel.Status != "Processing"))
        {
            mergeCandidatePendingBets.Clear();
            return;
        }

        mergeCandidatePendingBets = pendingBets
            .Where(b => b.Id != reviewingBetModel.Id &&
                        b.UserId == reviewingBetModel.UserId &&
                        (b.Status == "Pending" || b.Status == "Processing"))
            .OrderBy(b => b.Id)
            .ToList();
    }

    private async Task LoadResolvedScreenshotUrlAsync(string? originalUrl)
    {
        if (string.IsNullOrEmpty(originalUrl)) 
        {
            resolvedScreenshotUrl = "";
            await InvokeAsync(StateHasChanged);
            return;
        }

        if (originalUrl.Contains("127.0.0.1:10000") || originalUrl.Contains("localhost:10000"))
        {
            if (localImageCache.TryGetValue(originalUrl, out var cachedData))
            {
                resolvedScreenshotUrl = cachedData;
                await InvokeAsync(StateHasChanged);
                return;
            }
            try 
            {
                using var client = new HttpClient();
                var bytes = await client.GetByteArrayAsync(originalUrl);
                var base64 = Convert.ToBase64String(bytes);
                var ext = originalUrl.EndsWith(".png") ? "png" : "jpeg";
                var dataUrl = $"data:image/{ext};base64,{base64}";
                localImageCache[originalUrl] = dataUrl;
                resolvedScreenshotUrl = dataUrl;
            } 
            catch 
            { 
                resolvedScreenshotUrl = originalUrl;
            }
        }
        else
        {
            resolvedScreenshotUrl = originalUrl;
        }
        await InvokeAsync(StateHasChanged);
    }


    private void StartReview(Bet bet, bool keepExpansionState = false)
    {
        if (!keepExpansionState)
        {
            showAiExtractionDetails = false;
            showSavedBookmakerOdds = false;
        }

        if (pendingBets.Any(b => b.Id == bet.Id))
        {
            currentReviewList = pendingBets;
            reviewContext = "Pending";
            currentReviewIndex = pendingBets.FindIndex(b => b.Id == bet.Id);
        }
        else if (approvedBets.Any(b => b.Id == bet.Id))
        {
            currentReviewList = approvedBets;
            reviewContext = "Approved";
            currentReviewIndex = approvedBets.FindIndex(b => b.Id == bet.Id);
        }
        else if (completedBets.Any(b => b.Id == bet.Id))
        {
             currentReviewList = completedBets;
             reviewContext = "Completed";
             currentReviewIndex = completedBets.FindIndex(b => b.Id == bet.Id);
        }
        else
        {
            currentReviewList = new List<Bet> { bet };
            reviewContext = "Single";
            currentReviewIndex = 0;
        }

        originalBetModel = bet;
        reviewingBetModel = new Bet 
        { 
            Id = bet.Id, UserId = bet.UserId, UserName = bet.UserName, 
            AmountNOK = bet.AmountNOK, Odds = bet.Odds, Status = bet.Status, 
            FreeBetAmount = bet.FreeBetAmount,
            ScreenshotUrl = bet.ScreenshotUrl, ExtraScreenshotUrls = bet.ExtraScreenshotUrls,
            CreatedAt = bet.CreatedAt, ArrivedPendingAt = bet.ArrivedPendingAt,
            AiEvaluation = bet.AiEvaluation != null ? new BetAiEvaluation
            {
                Id = bet.AiEvaluation.Id,
                BetId = bet.AiEvaluation.BetId,
                AiVisionResultJson = bet.AiEvaluation.AiVisionResultJson,
                AiVisionError = bet.AiEvaluation.AiVisionError,
                AiOutcomeResult = bet.AiEvaluation.AiOutcomeResult,
                CreatedAt = bet.AiEvaluation.CreatedAt
            } : null,
            IsLive = bet.IsLive,
            IsAutoSettled = bet.IsAutoSettled,
            IsAutoApproved = bet.IsAutoApproved,
            AutoApproveAt = bet.AutoApproveAt,
            AutoApproveReason = bet.AutoApproveReason,
            IsBetBuilder = bet.IsBetBuilder,
            Bookmaker = bet.Bookmaker,
            MatchStartTime = bet.MatchStartTime,
            NextCheckTime = bet.NextCheckTime,
            Legs = bet.Legs
        };
        if (reviewingBetModel.Status == "Pending" || reviewingBetModel.Status == "Processing")
        {
            reviewingBetModel.Odds = (bet.HasExtraScreenshots && bet.Odds > 1.0m) ? bet.Odds : 0;
        }

        showModalDropdown = false;
        successMessage = null;
        errorMessage = null; errorMessage = null;
        AiVisionResult = null;
        AiVisionError = bet.AiVisionError;
        ParsedAiOutcome = null;
        CancelCheckingAllOdds();
        LegOddsCache.Clear();
        _oddsSavedForCurrentCache = false;
        IsCheckingAllOdds = false;
        CheckedLegsCount = 0;
        
        if (!string.IsNullOrEmpty(reviewingBetModel.AiVisionResultJson))
        {
            try {
                AiVisionResult = System.Text.Json.JsonSerializer.Deserialize<BettingApp.Services.AiVisionExtractionResult>(reviewingBetModel.AiVisionResultJson);
                if ((reviewingBetModel.Status == "Pending" || reviewingBetModel.Status == "Processing") && AiVisionResult != null)
                {
                    if (reviewingBetModel.Odds == 0 && !string.IsNullOrEmpty(AiVisionResult.TotalOdds))
                    {
                        var cleanOdds = AiVisionResult.TotalOdds.Replace(",", ".");
                        if (decimal.TryParse(cleanOdds, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal odds))
                        {
                            reviewingBetModel.Odds = Math.Floor(odds / 0.05m) * 0.05m;
                        }
                    }
                    if ((reviewingBetModel.AmountNOK == null || reviewingBetModel.AmountNOK == 0) && !string.IsNullOrEmpty(AiVisionResult.Stake))
                    {
                        var rawStake = AiVisionResult.Stake.Trim().ToLower();
                        int multiplier = 1;
                        if (rawStake.EndsWith("k"))
                        {
                            multiplier = 1000;
                            rawStake = rawStake.Substring(0, rawStake.Length - 1);
                        }
                        if (decimal.TryParse(rawStake.Replace(",", "."), out var parsedStake))
                        {
                            reviewingBetModel.AmountNOK = (int)(parsedStake * multiplier);
                        }
                    }
                }
            } catch (Exception ex) { AiVisionError = "Failed to parse AI Vision data: " + ex.Message; }
        }

        if (showSavedBookmakerOdds && reviewingBetModel.Status != "Pending" && reviewingBetModel.Status != "Processing")
        {
            PopulateSavedOddsIntoCache();
        }

        if (_outcomeCache.TryGetValue(bet.Id, out var cachedOutcome) && !string.IsNullOrEmpty(cachedOutcome.Json))
        {
            reviewingBetModel.AiOutcomeResult = cachedOutcome.Json;
            ParsedAiOutcome = cachedOutcome.Result;
        }
        else if (!string.IsNullOrEmpty(reviewingBetModel.AiOutcomeResult) && !reviewingBetModel.AiOutcomeResult.StartsWith("Error") && !reviewingBetModel.AiOutcomeResult.StartsWith("Exception"))
        {
            try {
                ParsedAiOutcome = System.Text.Json.JsonSerializer.Deserialize<BettingApp.Services.AiOutcomeResultData>(
                    reviewingBetModel.AiOutcomeResult, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );
            } catch { }
        }

        StateHasChanged();

        ShowOddsLookup = false;
        shouldFocusReviewModal = true;
        
        reviewScreenshotUrls = reviewingBetModel.GetAllScreenshotUrls();
        activeScreenshotIndex = 0;
        RefreshMergeCandidatePendingBetsFromMemory();
        resolvedScreenshotUrl = "";
        _ = LoadResolvedScreenshotUrlAsync(reviewScreenshotUrls.FirstOrDefault());
        _ = LoadReviewUserStatsAsync(bet.UserId);
    }

    private async Task LoadReviewUserStatsAsync(string userId)
    {
        try {
            int? activeBetId = reviewingBetModel?.Id;
            string? activeStatus = reviewingBetModel?.Status;

            using var context = DbFactory.CreateDbContext();
            var stats = await context.Users.Where(u => u.Id == userId).Select(u => new { u.FreeBetBalance, u.LifetimeProfit }).FirstOrDefaultAsync();
            if (stats != null) {
                currentReviewUserFreeBalance = stats.FreeBetBalance;
                currentReviewUserLifetimeProfit = stats.LifetimeProfit;
            }

            if (activeBetId.HasValue && (activeStatus == "Pending" || activeStatus == "Processing"))
            {
                var dbCandidates = await context.Bets
                    .AsNoTracking()
                    .Where(b => b.UserId == userId &&
                                b.Id != activeBetId.Value &&
                                (b.Status == "Pending" || b.Status == "Processing"))
                    .OrderBy(b => b.Id)
                    .Select(b => new Bet
                    {
                        Id = b.Id,
                        UserId = b.UserId,
                        UserName = b.UserName,
                        Status = b.Status,
                        AmountNOK = b.AmountNOK,
                        Odds = b.Odds,
                        ExtraScreenshotUrls = b.ExtraScreenshotUrls,
                        CreatedAt = b.CreatedAt
                    })
                    .ToListAsync();

                if (reviewingBetModel?.Id == activeBetId.Value &&
                    (reviewingBetModel.Status == "Pending" || reviewingBetModel.Status == "Processing"))
                {
                    mergeCandidatePendingBets = dbCandidates;
                }
            }
            else if (activeBetId.HasValue)
            {
                var dbFreeBetAmount = await context.Bets
                    .AsNoTracking()
                    .Where(b => b.Id == activeBetId.Value)
                    .Select(b => (decimal?)b.FreeBetAmount)
                    .FirstOrDefaultAsync();
                if (dbFreeBetAmount.HasValue)
                {
                    if (reviewingBetModel?.Id == activeBetId.Value) reviewingBetModel.FreeBetAmount = dbFreeBetAmount.Value;
                    if (originalBetModel?.Id == activeBetId.Value) originalBetModel.FreeBetAmount = dbFreeBetAmount.Value;
                }
            }

            await InvokeAsync(StateHasChanged);
        } catch { }
    }

    private async Task CancelReview() 
    { 
        CancelCheckingAllOdds();

        if (reviewingBetModel != null && originalBetModel != null)
        {
            if (originalBetModel.Status != "Pending" && (reviewingBetModel.Odds != originalBetModel.Odds || reviewingBetModel.AmountNOK != originalBetModel.AmountNOK))
            {
                 bool confirmed = await DialogService.ConfirmAsync("You have unsaved changes to Odds or Stake.\n\nClick OK to discard changes and close, or Cancel to continue editing.");
                 if (!confirmed) return;
            }
        }
        
        reviewingBetModel = null; 
        originalBetModel = null;
        mergeCandidatePendingBets.Clear();
        reviewScreenshotUrls.Clear();
        activeScreenshotIndex = 0;
        SortPendingBetsInPlace();
        errorMessage = null; 
        successMessage = null; 
        showAiExtractionDetails = false;
        showSavedBookmakerOdds = false;
        LegOddsCache.Clear();
        IsCheckingAllOdds = false;
        CheckedLegsCount = 0; 
    }

    private bool CanNavigateReview(int direction)
    {
        if (currentReviewList == null || currentReviewList.Count == 0) return false;
        if (direction < 0) 
        {
            if (currentReviewIndex > 0) return true;
            if (reviewContext == "Approved") return approvedPage > 1;
            if (reviewContext == "Completed") return completedPage > 1;
            return false;
        }
        else 
        {
            if (currentReviewIndex < currentReviewList.Count - 1) return true;
            if (reviewContext == "Approved") return hasMoreApprovedBets;
            if (reviewContext == "Completed") return hasMoreCompletedBets;
            return false;
        }
    }

    private async Task NavigateReview(int direction)
    {
        if (currentReviewList == null || currentReviewList.Count == 0) return;

        if (reviewingBetModel != null && originalBetModel != null)
        {
            if (originalBetModel.Status != "Pending" && (reviewingBetModel.Odds != originalBetModel.Odds || reviewingBetModel.AmountNOK != originalBetModel.AmountNOK))
            {
                bool confirmed = await DialogService.ConfirmAsync("You have unsaved changes to Odds or Stake.\n\nClick OK to discard changes and switch bets, or Cancel to continue editing.");
                if (!confirmed) return;
            }
        }

        int newIndex = currentReviewIndex + direction;

        if (newIndex < 0) 
        {
            if (reviewContext == "Approved" && approvedPage > 1) {
                await PrevApprovedPage();
                currentReviewList = approvedBets;
                newIndex = currentReviewList.Count - 1;
            } 
            else if (reviewContext == "Completed" && completedPage > 1) {
                await PrevPage();
                currentReviewList = completedBets;
                newIndex = currentReviewList.Count - 1;
            }
            else {
                newIndex = 0; // Stick to 0 if we can't go back
            }
        }
        else if (newIndex >= currentReviewList.Count) 
        {
            if (reviewContext == "Approved" && hasMoreApprovedBets) {
                await NextApprovedPage();
                currentReviewList = approvedBets;
                newIndex = 0;
            }
            else if (reviewContext == "Completed" && hasMoreCompletedBets) {
                await NextPage();
                currentReviewList = completedBets;
                newIndex = 0;
            }
            else {
                newIndex = currentReviewList.Count - 1;
            }
        }

        if (newIndex != currentReviewIndex && newIndex >= 0 && newIndex < currentReviewList.Count)
        {
            CancelCheckingAllOdds();
            successMessage = null;
            AiVisionResult = null;
            AiVisionError = null;
            ShowOddsLookup = false;
            
            var nextBet = currentReviewList[newIndex];
            StartReview(nextBet, keepExpansionState: true);
        }
        try { await reviewModalRef.FocusAsync(); } catch { }
    }

    private async Task OnReviewModalKeyDown(KeyboardEventArgs e) 
    { 
        if (e.Key == "Escape") await CancelReview();
        else if (e.Key == "ArrowRight") await NavigateReview(1);
        else if (e.Key == "ArrowLeft") await NavigateReview(-1);
    }

    private async Task ProcessBet(Bet bet, string newStatus, bool isAutoSettled = false, string? customAuditDetails = null, bool showOverlay = true)
    {
        if (showOverlay && isSavingBet) return;
        CancelCheckingAllOdds();
        Task? minOverlayTask = null;
        bool isSuccess = false;
        try
        {
            errorMessage = null;
            successMessage = null; 
            var matchingOriginal = (originalBetModel != null && originalBetModel.Id == bet.Id) ? originalBetModel : null;
            string originalStatus = matchingOriginal?.Status ?? bet.Status;
            int originalBetId = bet.Id;
            if (newStatus != "Rejected" && newStatus != "Cancelled")
        {
            if (bet.AmountNOK == null || bet.AmountNOK <= 0) { errorMessage = "Invalid amount";
                return; }
            
            if (bet.Odds <= 1) { errorMessage = "Invalid odds";
                return; }
            
            if (Math.Round(bet.Odds, 2) != bet.Odds) { errorMessage = "Odds max 2 decimals";
                return; }
        }

        bool isCompletedStatus(string status) => 
            status == "Won" || status == "Lost" || status == "Void" || status == "Rejected" || status == "Cancelled" || status == "Completed";

        bool isDataChanged = matchingOriginal != null && (bet.AmountNOK != matchingOriginal.AmountNOK || bet.Odds != matchingOriginal.Odds);
        bool isStatusChanged = originalStatus != newStatus;

        bool requiresConfirmation = !isAutoSettled && (
            (originalStatus == "Approved" && isDataChanged) ||
            (isCompletedStatus(originalStatus) && (isStatusChanged || isDataChanged))
        );

        if (requiresConfirmation)
        {
            string confirmMsg;
            if (isStatusChanged && isDataChanged)
            {
                var changes = new List<string> { $"Status: {originalStatus} -> {newStatus}" };
                if (matchingOriginal != null && bet.AmountNOK != matchingOriginal.AmountNOK)
                    changes.Add($"Stake: {matchingOriginal.AmountNOK:N0} kr -> {bet.AmountNOK:N0} kr");
                if (matchingOriginal != null && bet.Odds != matchingOriginal.Odds)
                    changes.Add($"Odds: {matchingOriginal.Odds:N2} -> {bet.Odds:N2}");
                confirmMsg = $"Update Bet #{bet.Id}?\n\n{string.Join("\n", changes)}";
            }
            else if (isStatusChanged)
            {
                confirmMsg = $"Update Bet #{bet.Id}?\n\nStatus: {originalStatus} -> {newStatus}\nStake: {bet.AmountNOK:N0} kr\nOdds: {bet.Odds:N2}";
            }
            else
            {
                var changes = new List<string>();
                if (matchingOriginal != null && bet.AmountNOK != matchingOriginal.AmountNOK)
                    changes.Add($"Stake: {matchingOriginal.AmountNOK:N0} kr -> {bet.AmountNOK:N0} kr");
                if (matchingOriginal != null && bet.Odds != matchingOriginal.Odds)
                    changes.Add($"Odds: {matchingOriginal.Odds:N2} -> {bet.Odds:N2}");
                confirmMsg = $"Update Bet #{bet.Id}?\n\n{string.Join("\n", changes)}";
            }

            bool confirmed = await DialogService.ConfirmAsync(confirmMsg);
            if (!confirmed)
            {
                if (matchingOriginal != null)
                {
                    bet.AmountNOK = matchingOriginal.AmountNOK;
                    bet.Odds = matchingOriginal.Odds;
                }
                return;
            }
        }

        bool willBeStaked = newStatus == "Approved" || newStatus == "Won" || newStatus == "Lost";
        bool isFirstApproval = originalStatus == "Pending" || originalStatus == "Processing" || originalStatus == "Rejected" || originalStatus == "Cancelled";
        bool isOddsNewOrModified = willBeStaked && (isFirstApproval || (matchingOriginal != null && bet.Odds != matchingOriginal.Odds));

        bool allowOddsMismatchOverride = false;
        if (!isAutoSettled && isOddsNewOrModified &&
            !BettingApp.Services.AutoApprovalService.TryValidateBetLegsOddsConsistency(bet.Odds, bet.Legs, out string? oddsMismatchReason))
        {
            bool confirmedMismatch = await DialogService.ConfirmAsync(
                $"⚠️ Odds Mismatch Warning (Bet #{bet.Id})\n\n" +
                $"{oddsMismatchReason}\n\n" +
                $"If this bet spans multiple screenshots, merge them before approving! If you force-approve, Auto-Settle will be disabled for this bet.\n\n" +
                $"Are you sure you want to proceed with {bet.Odds:N2} total odds?");
            if (!confirmedMismatch)
            {
                return;
            }
            allowOddsMismatchOverride = true;
        }

        if (showOverlay)
        {
            isSavingBet = true;
            StateHasChanged();
            await Task.Delay(1); // Give Blazor Server time to push UI state to client
            minOverlayTask = UiExecutionHelper.StartDelay();
        }


        if (newStatus == "Approved" && !_oddsSavedForCurrentCache && LegOddsCache.Any(kvp => !kvp.Value.IsLoading))
        {
            // If CheckAllOdds was cancelled mid-way when the admin clicked Approve,
            // fire-and-forget save whatever legs had already completed without delaying Approve!
            SaveAiVisionResultToDatabase();
        }
        
        var result = await BetProcessor.ProcessBetAsync(
            betId: bet.Id,
            newStatus: newStatus,
            adminName: currentAdminName ?? "Unknown",
            expectedOriginalStatus: originalStatus,
            newAmount: (decimal)(bet.AmountNOK ?? 0),
            newOdds: bet.Odds,
            customAuditDetails: customAuditDetails,
            isAutoSettled: isAutoSettled,
            allowOddsMismatchOverride: allowOddsMismatchOverride
        );

        if (!result.Success)
        {
            errorMessage = result.ErrorMessage;
            if (!result.IsValidationError)
            {
                _ = InvokeAsync(async () => {
                    try {
                        await LoadPendingBets();
                        await LoadApprovedBets();
                        await LoadCompletedBets();
                        StateHasChanged();
                    } catch { }
                });
                if (!isAutoSettled)
                {
                    reviewingBetModel = null;
                }
            }
            StateHasChanged();
            return;
        }

        isSuccess = true;

        // Do not block the UI waiting for 3 heavy table scans! 
        // Run them sequentially in the background to avoid spiking DB DTU, and update UI when done.
        _ = InvokeAsync(async () => {
            try {
                await LoadPendingBets();
                await LoadApprovedBets();
                await LoadCompletedBets();
                StateHasChanged();
            } catch { }
        });
        UiNotifier.NotifyAdmin("Update");
        
        if (newStatus == "Approved")
        {
            if (!bet.MatchStartTime.HasValue || (bet.Legs != null && bet.Legs.Any(l => !l.StartTime.HasValue)) || bet.MatchStartTime.Value <= DateTime.UtcNow)
            {
                BettingApp.Services.BetMonitoringService.WakeUp();
            }
        }
        
        if (isAutoSettled)
        {
            // For auto-settle, do NOT advance the queue or swap the open modal to the next bet!
            // Update the current reviewing model in place so the admin stays on the modal and sees the outcome.
            if (reviewingBetModel?.Id == bet.Id)
            {
                reviewingBetModel.Status = newStatus;
                reviewingBetModel.IsAutoSettled = true;
            }
            if (originalBetModel?.Id == bet.Id)
            {
                originalBetModel.Status = newStatus;
                originalBetModel.IsAutoSettled = true;
            }
            StateHasChanged();
            return;
        }

        if (originalStatus == newStatus || (reviewContext == "Completed" && isCompletedStatus(newStatus)))
        {
            successMessage = isStatusChanged ? $"Bet #{bet.Id} updated to {newStatus}." : $"Bet #{bet.Id} updated.";
            
            var listModel = approvedBets.FirstOrDefault(b => b.Id == bet.Id) 
                         ?? allApprovedBets.FirstOrDefault(b => b.Id == bet.Id)
                         ?? completedBets.FirstOrDefault(b => b.Id == bet.Id) 
                         ?? pendingBets.FirstOrDefault(b => b.Id == bet.Id);
            if (listModel != null)
            {
                listModel.AmountNOK = bet.AmountNOK;
                listModel.Odds = bet.Odds;
                listModel.Status = newStatus;
            }
            if (originalBetModel != null && originalBetModel.Id == bet.Id)
            {
                originalBetModel.AmountNOK = bet.AmountNOK;
                originalBetModel.Odds = bet.Odds;
                originalBetModel.Status = newStatus;
            }
            if (reviewingBetModel != null && reviewingBetModel.Id == bet.Id)
            {
                reviewingBetModel.AmountNOK = bet.AmountNOK;
                reviewingBetModel.Odds = bet.Odds;
                reviewingBetModel.Status = newStatus;
            }
            _ = LoadReviewUserStatsAsync(bet.UserId);
            ApplyApprovedFilterAndSort();
            StateHasChanged();
            return;
        }

        successMessage = $"Bet #{bet.Id} updated.";
        
        // Remove the processed bet from the current UI list immediately if its status changed,
        // so the queue automatically advances to the next bet without waiting for the background DB reload!
        if (reviewContext == "Pending" && newStatus != "Pending" && newStatus != "Processing")
            pendingBets.RemoveAll(b => b.Id == bet.Id);
        else if (reviewContext == "Approved" && newStatus != "Approved")
        {
            allApprovedBets.RemoveAll(b => b.Id == bet.Id);
            ApplyApprovedFilterAndSort();
        }
        else if (reviewContext == "Completed" && (newStatus == "Pending" || newStatus == "Approved" || newStatus == "Processing"))
            completedBets.RemoveAll(b => b.Id == bet.Id);

        if (reviewContext == "Pending") currentReviewList = pendingBets;
        else if (reviewContext == "Approved") currentReviewList = approvedBets;
        else if (reviewContext == "Completed") currentReviewList = completedBets;
        else { }

        if (currentReviewList.Count > 0)
        {
            if (currentReviewIndex >= currentReviewList.Count) 
            {
                reviewingBetModel = null;
                SortPendingBetsInPlace();
                showAiExtractionDetails = false;
                showSavedBookmakerOdds = false;
                successMessage = $"Finished processing. Queue empty.";
            }
            else
            {
                var actualNextBet = currentReviewList[currentReviewIndex];
                StartReview(actualNextBet, keepExpansionState: true);
                successMessage = null;
            }
        }
        else
        {
            reviewingBetModel = null;
            SortPendingBetsInPlace();
            showAiExtractionDetails = false;
            showSavedBookmakerOdds = false;
            successMessage = null; 
            successMessage = $"Bet #{bet.Id} processed. Queue empty.";
        }
        StateHasChanged();
        }
        catch (Exception ex) when (ex is TaskCanceledException || ex is OperationCanceledException || ex is JSDisconnectedException)
        {
            // Operation cancelled or circuit disconnected normally (e.g. user dismissed dialog or navigated)
        }
        catch (Exception ex)
        {
            errorMessage = "Failed to process bet due to a database error. Please try again.";
            Console.WriteLine($"[Admin.razor] ProcessBet Error: {ex.Message}");
        }
        finally
        {
            if (showOverlay)
            {
                await UiExecutionHelper.AwaitMinDurationAsync(minOverlayTask, isSuccess);
                isSavingBet = false;
                StateHasChanged();
            }
        }
    }

    private static readonly System.Text.RegularExpressions.Regex VerificationHighlightRegex = new(
        @"(Verified (?:via|by) (?:ESPN|FotMob):?)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled
    );

    private MarkupString HighlightVerification(string? stats)
    {
        if (string.IsNullOrEmpty(stats)) return (MarkupString)"";

        string encoded = System.Net.WebUtility.HtmlEncode(stats);

        string highlighted = VerificationHighlightRegex.Replace(encoded, m =>
            $"<span style=\"border: 1px solid #000; padding: 0 3px; border-radius: 3px;\">{m.Value}</span>"
        );

        return (MarkupString)highlighted;
    }

    private bool IsMergeCandidateBusy(Bet candidate)
    {
        return IsAnalyzingAutoRead(candidate.Id) ||
               (candidate.Status == "Processing" && (DateTime.UtcNow - candidate.CreatedAt).TotalMinutes < 2);
    }

    private static AiVisionExtractionResult? ParseOrBuildVisionResult(Bet b)
    {
        if (!string.IsNullOrWhiteSpace(b.AiVisionResultJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<AiVisionExtractionResult>(b.AiVisionResultJson);
                if (parsed != null) return parsed;
            }
            catch { }
        }

        bool hasFallbackData = (b.Legs != null && b.Legs.Count > 0) ||
                               (b.AmountNOK.HasValue && b.AmountNOK.Value > 0) ||
                               b.Odds > 1.0m;
        if (!hasFallbackData) return null;

        return new AiVisionExtractionResult
        {
            Bookmaker = b.Bookmaker ?? "",
            IsBetBuilder = b.IsBetBuilder,
            IsLive = b.IsLive,
            Stake = (b.AmountNOK.HasValue && b.AmountNOK.Value > 0) ? b.AmountNOK.Value.ToString() : "",
            TotalOdds = b.Odds > 1.0m ? b.Odds.ToString(System.Globalization.CultureInfo.InvariantCulture) : "",
            Legs = b.Legs?.OrderBy(l => l.Id).Select(l => new AiVisionLeg
            {
                Match = l.Match ?? "",
                Sport = l.Sport ?? "",
                Market = l.Market ?? "",
                Selection = l.Selection ?? "",
                Odds = l.Odds ?? "",
                StartTime = l.StartTime
            }).ToList() ?? new List<AiVisionLeg>()
        };
    }

    private async Task MergeWithPendingBetAsync(int otherBetId)
    {
        if (reviewingBetModel == null || isMergingBet || isSavingBet) return;
        if (reviewingBetModel.Status != "Pending" && reviewingBetModel.Status != "Processing")
        {
            errorMessage = "Only pending bets can be merged.";
            return;
        }

        int targetBetId = reviewingBetModel.Id;
        if (targetBetId == otherBetId) return;
        showModalDropdown = false;

        if (IsAnalyzingAutoRead(targetBetId) || IsAnalyzingAutoRead(otherBetId) ||
            (reviewingBetModel.Status == "Processing" && (DateTime.UtcNow - reviewingBetModel.CreatedAt).TotalMinutes < 2))
        {
            errorMessage = "AI Auto-Read is still finishing on one of these screenshots. Please wait a few seconds and try again.";
            return;
        }

        int? manualStake = (reviewingBetModel.AmountNOK.HasValue && reviewingBetModel.AmountNOK.Value > 0)
            ? reviewingBetModel.AmountNOK
            : null;
        decimal? manualOdds = reviewingBetModel.Odds > 1.0m ? reviewingBetModel.Odds : null;

        bool confirmed = await DialogService.ConfirmAsync(
            $"Merge Bet #{targetBetId} and Bet #{otherBetId} into a single bet (keeping Bet #{targetBetId})?\n\n" +
            $"• Both screenshots and all unique legs will be combined.\n" +
            $"• Bet #{otherBetId} will be removed so the user is only charged/paid once.\n" +
            $"• Auto-settlement will be disabled (manual admin settlement).");
        if (!confirmed) return;

        CancelCheckingAllOdds();
        isMergingBet = true;
        isSavingBet = true;
        errorMessage = null;
        successMessage = null;
        StateHasChanged();
        await Task.Delay(1);
        var minOverlayTask = UiExecutionHelper.StartDelay();
        bool isSuccess = false;

        try
        {
            if (IsAnalyzingAutoRead(targetBetId) || IsAnalyzingAutoRead(otherBetId))
            {
                errorMessage = "AI Auto-Read is still running on one of these screenshots. Please wait a few seconds and try again.";
                return;
            }

            var adminName = currentAdminName ?? "Admin";
            await AutoApproval.CancelAutoApprovalForBetAsync(targetBetId, adminName);
            await AutoApproval.CancelAutoApprovalForBetAsync(otherBetId, adminName);

            using var context = DbFactory.CreateDbContext();
            var bothBets = await context.Bets
                .Include(b => b.AiEvaluation)
                .Include(b => b.Legs)
                .ThenInclude(l => l.BookmakerOdds)
                .Where(b => b.Id == targetBetId || b.Id == otherBetId)
                .ToListAsync();

            var targetDbBet = bothBets.FirstOrDefault(b => b.Id == targetBetId);
            var otherDbBet = bothBets.FirstOrDefault(b => b.Id == otherBetId);

            if (targetDbBet == null || otherDbBet == null)
            {
                errorMessage = "One of the bets no longer exists (it may have already been merged or cancelled).";
                await LoadPendingBets();
                RefreshMergeCandidatePendingBetsFromMemory();
                return;
            }

            if (targetDbBet.UserId != otherDbBet.UserId)
            {
                errorMessage = "Cannot merge bets belonging to different users.";
                return;
            }

            bool targetReady = targetDbBet.Status == "Pending" ||
                               (targetDbBet.Status == "Processing" && (DateTime.UtcNow - targetDbBet.CreatedAt).TotalMinutes >= 2);
            bool otherReady = otherDbBet.Status == "Pending" ||
                              (otherDbBet.Status == "Processing" && (DateTime.UtcNow - otherDbBet.CreatedAt).TotalMinutes >= 2);
            if (!targetReady || !otherReady)
            {
                errorMessage = (targetDbBet.Status == "Processing" || otherDbBet.Status == "Processing")
                    ? "One screenshot is still finishing AI Auto-Read. Please wait a few seconds and try again."
                    : "Both bets must still be Pending to merge (one may have just been approved, rejected, or cancelled).";
                await LoadPendingBets();
                RefreshMergeCandidatePendingBetsFromMemory();
                return;
            }

            // Order chronologically by Bet ID ascending so merging #1041->#1042 and #1042->#1041 produce identical screenshot & leg ordering
            var orderedBets = bothBets.OrderBy(b => b.Id).ToList();

            var combinedUrls = new List<string>();
            foreach (var url in orderedBets.SelectMany(b => b.GetAllScreenshotUrls()))
            {
                if (!combinedUrls.Contains(url, StringComparer.OrdinalIgnoreCase))
                {
                    combinedUrls.Add(url);
                }
            }

            if (combinedUrls.Count == 0)
            {
                errorMessage = "No screenshots found on the selected bets.";
                return;
            }

            var targetInitialVision = ParseOrBuildVisionResult(targetDbBet);
            int? targetInitialAiStake = AiVisionExtractionResult.TryParseStakeNok(targetInitialVision?.Stake);
            decimal targetInitialRawOdds = AiVisionExtractionResult.TryParseOddsValue(targetInitialVision?.TotalOdds);
            decimal targetInitialFlooredOdds = targetInitialRawOdds > 1.0m
                ? Math.Floor(targetInitialRawOdds / 0.05m) * 0.05m
                : 0m;

            bool adminEditedStake = manualStake.HasValue &&
                                    manualStake != targetInitialAiStake &&
                                    manualStake != targetDbBet.AmountNOK;
            bool adminEditedOdds = manualOdds.HasValue &&
                                   Math.Round(manualOdds.Value, 2) != Math.Round(targetInitialFlooredOdds, 2) &&
                                   !(targetDbBet.HasExtraScreenshots && Math.Round(manualOdds.Value, 2) == Math.Round(targetDbBet.Odds, 2));

            var perBetVision = orderedBets.Select(ParseOrBuildVisionResult).ToList();
            var combinedVision = AiVisionExtractionResult.CombineMultiScreenshotResults(perBetVision)
                                 ?? new AiVisionExtractionResult();

            int? combinedStake = adminEditedStake
                ? manualStake
                : (AiVisionExtractionResult.TryParseStakeNok(combinedVision.Stake) ?? manualStake);

            decimal combinedRawOdds = AiVisionExtractionResult.TryParseOddsValue(combinedVision.TotalOdds);
            decimal combinedFlooredOdds = combinedRawOdds > 1.0m
                ? Math.Floor(combinedRawOdds / 0.05m) * 0.05m
                : 0m;
            decimal finalOdds = adminEditedOdds
                ? Math.Round(manualOdds!.Value, 2)
                : (combinedFlooredOdds > 1.0m
                    ? combinedFlooredOdds
                    : (manualOdds.HasValue && manualOdds.Value > 1.0m ? Math.Round(manualOdds.Value, 2) : 1.0m));

            if (combinedStake.HasValue && combinedStake.Value > 0 && (adminEditedStake || string.IsNullOrWhiteSpace(combinedVision.Stake)))
            {
                combinedVision.Stake = combinedStake.Value.ToString();
            }
            if (finalOdds > 1.0m && (adminEditedOdds || string.IsNullOrWhiteSpace(combinedVision.TotalOdds)))
            {
                combinedVision.TotalOdds = finalOdds.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            }

            // Preserve any existing StartTime, FixtureId, and BookmakerOdds already looked up on either bet's legs
            var existingLegsByKey = new Dictionary<string, BetLeg>(StringComparer.Ordinal);
            foreach (var existingLeg in orderedBets.Where(b => b.Legs != null).SelectMany(b => b.Legs.OrderBy(l => l.Id)))
            {
                string key = AiVisionExtractionResult.GetLegDeduplicationKey(existingLeg.Match, existingLeg.Market, existingLeg.Selection, existingLeg.Odds);
                if (!existingLegsByKey.TryGetValue(key, out var prev) ||
                    (string.IsNullOrEmpty(prev.FixtureId) && !string.IsNullOrEmpty(existingLeg.FixtureId)) ||
                    ((prev.BookmakerOdds == null || prev.BookmakerOdds.Count == 0) && existingLeg.BookmakerOdds != null && existingLeg.BookmakerOdds.Count > 0))
                {
                    existingLegsByKey[key] = existingLeg;
                }
            }

            if (targetDbBet.Legs != null && targetDbBet.Legs.Count > 0)
            {
                context.BetLegs.RemoveRange(targetDbBet.Legs.ToList());
                targetDbBet.Legs.Clear();
            }
            else
            {
                targetDbBet.Legs = new List<BetLeg>();
            }

            if (combinedVision.Legs != null)
            {
                foreach (var vLeg in combinedVision.Legs)
                {
                    string key = AiVisionExtractionResult.GetLegDeduplicationKey(vLeg.Match, vLeg.Market, vLeg.Selection, vLeg.Odds);
                    existingLegsByKey.TryGetValue(key, out var matchedDbLeg);
                    DateTime? legStart = (vLeg.StartTime.HasValue && vLeg.StartTime.Value > DateTime.MinValue)
                        ? vLeg.StartTime
                        : matchedDbLeg?.StartTime;
                    if (legStart.HasValue && legStart.Value > DateTime.MinValue)
                    {
                        vLeg.StartTime = legStart;
                    }

                    targetDbBet.Legs.Add(new BetLeg
                    {
                        Match = vLeg.Match ?? "",
                        Sport = vLeg.Sport ?? "",
                        Market = vLeg.Market ?? "",
                        Selection = vLeg.Selection ?? "",
                        Odds = vLeg.Odds ?? "",
                        StartTime = legStart,
                        FixtureId = matchedDbLeg?.FixtureId,
                        Outcome = "Pending",
                        VerificationSource = "Unknown",
                        Stats = "",
                        BookmakerOdds = matchedDbLeg?.BookmakerOdds?.Select(bo => new BetLegBookmakerOdds
                        {
                            BookmakerName = bo.BookmakerName,
                            OddsValue = bo.OddsValue,
                            Limit = bo.Limit,
                            LookedUpAt = bo.LookedUpAt,
                            BookmakerUrl = bo.BookmakerUrl,
                            MarketId = bo.MarketId,
                            OutcomeId = bo.OutcomeId,
                            PlayerId = bo.PlayerId
                        }).ToList() ?? new List<BetLegBookmakerOdds>()
                    });
                }
            }

            targetDbBet.ScreenshotUrl = combinedUrls[0];
            targetDbBet.ExtraScreenshotUrls = combinedUrls.Count > 1
                ? string.Join("\n", combinedUrls.Skip(1))
                : null;
            targetDbBet.Status = "Pending";
            targetDbBet.ArrivedPendingAt ??= otherDbBet.ArrivedPendingAt ?? DateTime.UtcNow;
            targetDbBet.AmountNOK = combinedStake;
            targetDbBet.Odds = finalOdds;
            targetDbBet.IsLive = targetDbBet.IsLive || otherDbBet.IsLive || combinedVision.IsLive;
            targetDbBet.IsBetBuilder = targetDbBet.IsBetBuilder || otherDbBet.IsBetBuilder || combinedVision.IsBetBuilder;
            if (!string.IsNullOrWhiteSpace(combinedVision.Bookmaker))
            {
                targetDbBet.Bookmaker = combinedVision.Bookmaker;
            }
            else if (string.IsNullOrWhiteSpace(targetDbBet.Bookmaker) && !string.IsNullOrWhiteSpace(otherDbBet.Bookmaker))
            {
                targetDbBet.Bookmaker = otherDbBet.Bookmaker;
            }

            bool allLegsScheduled = combinedVision.Legs != null &&
                                    combinedVision.Legs.Count > 0 &&
                                    combinedVision.Legs.All(l => l.StartTime.HasValue && l.StartTime.Value > DateTime.MinValue);
            targetDbBet.MatchStartTime = allLegsScheduled
                ? combinedVision.Legs!.Min(l => l.StartTime)
                : null;

            if (targetDbBet.AiEvaluation == null)
            {
                var existingEval = await context.BetAiEvaluations.FirstOrDefaultAsync(a => a.BetId == targetDbBet.Id);
                targetDbBet.AiEvaluation = existingEval ?? new BetAiEvaluation { BetId = targetDbBet.Id };
            }
            targetDbBet.AiVisionResultJson = (combinedVision.Legs?.Count > 0 ||
                                              !string.IsNullOrWhiteSpace(combinedVision.Stake) ||
                                              !string.IsNullOrWhiteSpace(combinedVision.TotalOdds))
                ? JsonSerializer.Serialize(combinedVision)
                : null;
            targetDbBet.AiVisionError = null;
            targetDbBet.AutoApproveAt = null;
            targetDbBet.IsAutoApproved = false;
            targetDbBet.AutoApproveReason = $"🔗 Merged with Bet #{otherDbBet.Id} by {adminName} ({combinedUrls.Count} screenshots • Manual settle)";
            targetDbBet.UpdatedAt = DateTime.UtcNow;

            context.Bets.Remove(otherDbBet);
            context.AuditLogs.Add(new AuditLog
            {
                Action = "Merged Pending Bets",
                AdminUserName = adminName,
                TargetUserName = targetDbBet.UserName ?? targetDbBet.UserId,
                Details = $"Merged Pending Bet #{otherDbBet.Id} into Bet #{targetDbBet.Id} ({combinedUrls.Count} screenshots, {targetDbBet.Legs.Count} unique legs). Bet #{otherDbBet.Id} removed; auto-settlement disabled.",
                Timestamp = DateTime.UtcNow
            });

            await context.SaveChangesAsync();
            isSuccess = true;

            _visionCache.Remove(targetBetId);
            _visionCache.Remove(otherBetId);
            _outcomeCache.Remove(otherBetId);
            pendingBets.RemoveAll(b => b.Id == otherBetId);

            await LoadPendingBets();
            var refreshedTarget = pendingBets.FirstOrDefault(b => b.Id == targetBetId) ?? targetDbBet;
            StartReview(refreshedTarget, keepExpansionState: true);
            successMessage = $"Merged Bet #{otherBetId} into Bet #{targetBetId} ({combinedUrls.Count} screenshots, {targetDbBet.Legs.Count} legs). Auto-settle disabled.";

            UiNotifier.NotifyAdmin("PendingUpdate");
            UiNotifier.NotifyUser(targetDbBet.UserId, $"Your screenshots were combined into Pick #{targetBetId}.");
            StateHasChanged();
        }
        catch (Exception ex) when (ex is TaskCanceledException || ex is OperationCanceledException || ex is JSDisconnectedException)
        {
        }
        catch (Exception ex)
        {
            errorMessage = "Failed to merge bets due to a database error. Please try again.";
            Console.WriteLine($"[Admin.razor] MergeWithPendingBetAsync Error: {ex.Message}");
        }
        finally
        {
            await UiExecutionHelper.AwaitMinDurationAsync(minOverlayTask, isSuccess);
            isMergingBet = false;
            isSavingBet = false;
            StateHasChanged();
        }
    }

}
