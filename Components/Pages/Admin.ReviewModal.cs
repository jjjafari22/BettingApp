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


    private void StartReview(Bet bet)
    {
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
            ScreenshotUrl = bet.ScreenshotUrl, CreatedAt = bet.CreatedAt,
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
            IsBetBuilder = bet.IsBetBuilder,
            Bookmaker = bet.Bookmaker,
            MatchStartTime = bet.MatchStartTime,
            NextCheckTime = bet.NextCheckTime,
            Legs = bet.Legs
        };
        if (reviewingBetModel.Status == "Pending" || reviewingBetModel.Status == "Processing")
        {
            reviewingBetModel.Odds = 0;
        }

        showModalDropdown = false;
        successMessage = null;
        errorMessage = null; errorMessage = null;
        AiVisionResult = null;
        AiVisionError = bet.AiVisionError;
        ParsedAiOutcome = null;
        LegOddsCache.Clear();
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

        if (!string.IsNullOrEmpty(reviewingBetModel.AiOutcomeResult) && !reviewingBetModel.AiOutcomeResult.StartsWith("Error") && !reviewingBetModel.AiOutcomeResult.StartsWith("Exception"))
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
        
        resolvedScreenshotUrl = "";
        _ = LoadResolvedScreenshotUrlAsync(bet.ScreenshotUrl);
        _ = LoadReviewUserStatsAsync(bet.UserId);
    }

    private async Task LoadReviewUserStatsAsync(string userId)
    {
        try {
            using var context = DbFactory.CreateDbContext();
            var stats = await context.Users.Where(u => u.Id == userId).Select(u => new { u.FreeBetBalance, u.LifetimeProfit }).FirstOrDefaultAsync();
            if (stats != null) {
                currentReviewUserFreeBalance = stats.FreeBetBalance;
                currentReviewUserLifetimeProfit = stats.LifetimeProfit;
                await InvokeAsync(StateHasChanged);
            }
        } catch { }
    }

    private async Task CancelReview() 
    { 
        if (reviewingBetModel != null && originalBetModel != null)
        {
            if (originalBetModel.Status != "Pending" && (reviewingBetModel.Odds != originalBetModel.Odds || reviewingBetModel.AmountNOK != originalBetModel.AmountNOK))
            {
                 bool confirmed = await DialogService.ConfirmAsync( "You have unsaved changes to Odds or Stake.\n\nPlease click a status button (Won, Lost, Void) to save them, or click OK to discard changes and close.");
                 if (!confirmed) return;
            }
        }
        
        reviewingBetModel = null; 
        originalBetModel = null;
        errorMessage = null; 
        successMessage = null; 
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
            successMessage = null;
            AiVisionResult = null;
            AiVisionError = null;
            ShowOddsLookup = false;
            
            var nextBet = currentReviewList[newIndex];
            StartReview(nextBet);
        }
        try { await reviewModalRef.FocusAsync(); } catch { }
    }

    private async Task OnReviewModalKeyDown(KeyboardEventArgs e) 
    { 
        if (e.Key == "ArrowRight") await NavigateReview(1);
        else if (e.Key == "ArrowLeft") await NavigateReview(-1);
    }

    private async Task ProcessBet(Bet bet, string newStatus, bool isAutoSettled = false, string? customAuditDetails = null, bool showOverlay = true)
    {
        if (showOverlay && isSavingBet) return;
        try
        {
            if (showOverlay)
            {
                isSavingBet = true;
                StateHasChanged();
                await Task.Delay(1); // Give Blazor Server time to push UI state to client
            }

            errorMessage = null;
            successMessage = null; 
            string originalStatus = bet.Status;
            bool isUndo = originalStatus != "Pending" && newStatus == "Approved";
            int originalBetId = bet.Id;
            if (newStatus != "Rejected" && newStatus != "Cancelled")
        {
            if (bet.AmountNOK == null || bet.AmountNOK <= 0) { errorMessage = "Invalid amount";
                return; }
            
            if (bet.Odds <= 1) { errorMessage = "Invalid odds";
                return; }
            
            if (Math.Round(bet.Odds, 2) != bet.Odds) { errorMessage = "Odds max 2 decimals";
                return; }

            bool isNewOrModified = originalStatus == "Pending" || (originalBetModel != null && (bet.AmountNOK != originalBetModel.AmountNOK || bet.Odds != originalBetModel.Odds));

            if (isNewOrModified)
            {
                using var validationContext = DbFactory.CreateDbContext();
                var settings = await validationContext.Settings.OrderBy(s => s.Id).FirstOrDefaultAsync();
                var betUser = await validationContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == bet.UserId);
                if (settings != null)
                {
                     if ((decimal)(bet.AmountNOK ?? 0) < settings.MinBetAmount) { 
                         errorMessage = $"Amount below limit ({settings.MinBetAmount:N0})";
                         return; 
                     }
                }
                
                if (betUser != null)
                {
                     decimal currentAmount = (decimal)(bet.AmountNOK ?? 0);
                     decimal netLiability = Math.Floor(currentAmount * (bet.Odds - 1));
                     decimal userLimit = betUser.MaxNetLiability;

                     if (netLiability > userLimit) { 
                         decimal maxBetAmount = Math.Floor(userLimit / (bet.Odds - 1));
                         errorMessage = $"Net liability exceeds limit ({userLimit:N0} kr). Max bet for {bet.Odds} odds is {maxBetAmount:N0} kr.";
                         return; 
                     }
                }
            }
        }

        if (!isAutoSettled && new[] { "Won", "Lost", "Void", "Completed", "Cancelled" }.Contains(originalStatus))
        {
             bool confirmed = await DialogService.ConfirmAsync( 
                $"Update Bet #{bet.Id}?\n\n{originalStatus} -> {newStatus}\nAmount: {bet.AmountNOK} kr\nOdds: {bet.Odds}");
             if (!confirmed) return;
        }


        if (newStatus == "Approved" && LegOddsCache.Count > 0)
        {
            try
            {
                using var oddsContext = DbFactory.CreateDbContext();
                var dbBetToUpdate = await oddsContext.Bets.Include(b => b.Legs).ThenInclude(l => l.BookmakerOdds).FirstOrDefaultAsync(b => b.Id == bet.Id);
                if (dbBetToUpdate != null)
                {
                    for (int i = 0; i < dbBetToUpdate.Legs.Count; i++)
                    {
                        if (LegOddsCache.TryGetValue(i, out var legResult) && legResult.MatchedBookieOdds.Count > 0)
                        {
                            dbBetToUpdate.Legs[i].BookmakerOdds.Clear();
                            foreach (var kvp in legResult.MatchedBookieOdds)
                            {
                                if (kvp.Value.Price > 0)
                                {
                                    dbBetToUpdate.Legs[i].BookmakerOdds.Add(new BetLegBookmakerOdds 
                                    {
                                        BookmakerName = kvp.Key,
                                        OddsValue = (decimal)kvp.Value.Price,
                                        Limit = (decimal?)kvp.Value.Limit,
                                        LookedUpAt = legResult.LookedUpAt
                                    });
                                }
                            }
                        }
                    }
                    await oddsContext.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Admin.razor] Failed to save Bookmaker Odds: {ex.Message}");
            }
        }
        
        var result = await BetProcessor.ProcessBetAsync(
            betId: bet.Id,
            newStatus: newStatus,
            adminName: currentAdminName ?? "Unknown",
            expectedOriginalStatus: originalStatus,
            newAmount: (decimal)(bet.AmountNOK ?? 0),
            newOdds: bet.Odds,
            customAuditDetails: customAuditDetails,
            isAutoSettled: isAutoSettled
        );

        if (!result.Success)
        {
            if (result.ErrorMessage.Contains("concurrently"))
            {
                // Simple retry for concurrency (matches previous behavior for ApplicationUser)
                if (showOverlay) isSavingBet = false;
                await ProcessBet(bet, newStatus, isAutoSettled, customAuditDetails, showOverlay);
                return;
            }

            errorMessage = result.ErrorMessage;
            _ = InvokeAsync(async () => {
                await LoadPendingBets();
                await LoadApprovedBets();
                await LoadCompletedBets();
                StateHasChanged();
            });
            if (!isAutoSettled)
            {
                reviewingBetModel = null;
            }
            StateHasChanged();
            return;
        }

        // Do not block the UI waiting for 3 heavy table scans! 
        // Run them sequentially in the background to avoid spiking DB DTU, and update UI when done.
        _ = InvokeAsync(async () => {
            await LoadPendingBets();
            await LoadApprovedBets();
            await LoadCompletedBets();
            StateHasChanged();
        });
        UiNotifier.NotifyAdmin("Update");
        
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

        successMessage = $"Bet #{bet.Id} updated.";
        
        // Remove the processed bet from the current UI list immediately if its status changed,
        // so the queue automatically advances to the next bet without waiting for the background DB reload!
        if (reviewContext == "Pending" && newStatus != "Pending" && newStatus != "Processing")
            pendingBets.RemoveAll(b => b.Id == bet.Id);
        else if (reviewContext == "Approved" && newStatus != "Approved")
            approvedBets.RemoveAll(b => b.Id == bet.Id);
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
                successMessage = $"Finished processing. Queue empty.";
            }
            else
            {
                var actualNextBet = currentReviewList[currentReviewIndex];
                StartReview(actualNextBet);
                successMessage = null;
            }
        }
        else
        {
            reviewingBetModel = null;
            successMessage = null; 
            successMessage = $"Bet #{bet.Id} processed. Queue empty.";
        }
        StateHasChanged();
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
                isSavingBet = false;
                StateHasChanged();
            }
        }
    }

}
