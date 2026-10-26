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

// Table queries, pagination, sorting, filters, and CSV exports
public partial class Admin
{
    private HashSet<string> completedStatusFilters = new() { "Won", "Lost", "Void", "Rejected", "Cancelled" };
    private bool showOnlyMismatch = false;
    private bool showOnlyAutoApproved = false;
    private bool showOnlyAdminOverride = false;
    private string completedSearchTerm = "";
    private string approvedSearchTerm = "";
    
    private async Task ToggleFilter(string status)
    {
        if (completedStatusFilters.Contains(status))
            completedStatusFilters.Remove(status);
        else
            completedStatusFilters.Add(status);
            
        completedPage = 1;
        await LoadCompletedBets();
    }

    private async Task ToggleMismatchFilter()
    {
        showOnlyMismatch = !showOnlyMismatch;
        completedPage = 1;
        await LoadCompletedBets();
    }

    private async Task ToggleAutoApprovedFilter()
    {
        showOnlyAutoApproved = !showOnlyAutoApproved;
        completedPage = 1;
        await LoadCompletedBets();
    }

    private async Task ToggleAdminOverrideFilter()
    {
        showOnlyAdminOverride = !showOnlyAdminOverride;
        completedPage = 1;
        await LoadCompletedBets();
    }

    private async Task OnCompletedSearchChanged()
    {
        completedPage = 1;
        await LoadCompletedBets();
    }

    private async Task ClearCompletedSearch()
    {
        completedSearchTerm = "";
        completedPage = 1;
        await LoadCompletedBets();
    }

    private void OnApprovedSearchChanged()
    {
        approvedPage = 1;
        ApplyApprovedFilterAndSort();
    }

    private void ClearApprovedSearch()
    {
        approvedSearchTerm = "";
        approvedPage = 1;
        ApplyApprovedFilterAndSort();
    }

    private string GetStatusBadgeClass(string status)
    {
        return status switch
        {
            "Approved" => "bg-success",
            "Won" => "bg-success",
            "Pending" => "bg-warning text-dark",
            "Lost" => "bg-danger",
            "Rejected" => "bg-danger",
            "Cancelled" => "bg-dark",
            _ => "bg-secondary"
        };
    }

    private decimal GetNetProfit(Bet bet)
    {
        // Admin profit logic:
        // For normal bets, profit is (Stake - Payout).
        // For free bets, profit is (-Payout) because the stake was already "paid for" upon granting.
        // Stake portions that matter: Real portion = (Total - Free).
        
        decimal realStakePortion = (decimal)(bet.AmountNOK ?? 0) - bet.FreeBetAmount;

        if (bet.Status == "Lost") return realStakePortion;
        if (bet.Status == "Won") return realStakePortion - bet.PotentialPayout;
        return 0; // Void/Rejected/Cancelled don't affect profit
    }


    private void SortPendingBetsInPlace()
    {
        if (pendingBets == null || pendingBets.Count <= 1) return;
        pendingBets = pendingBets
            .OrderBy(b => b.MatchStartTime.HasValue ? 1 : 0)
            .ThenBy(b => b.MatchStartTime)
            .ThenByDescending(b => b.CreatedAt)
            .ToList();
    }

    private async Task LoadPendingBets()
    {
        try
        {
            using var context = DbFactory.CreateDbContext();
            var twoMinsAgo = DateTime.UtcNow.AddMinutes(-2);
            var allPendingBets = await context.Bets.Include(b => b.AiEvaluation).Include(b => b.Legs).ThenInclude(l => l.BookmakerOdds).Where(b => b.Status == "Pending" || (b.Status == "Processing" && b.CreatedAt < twoMinsAgo)).ToListAsync();
            totalPendingBets = allPendingBets.Count;

            var existingOrder = (reviewingBetModel != null && reviewContext == "Pending" && pendingBets.Count > 0)
                ? pendingBets.Select((b, idx) => (b.Id, idx)).ToDictionary(x => x.Id, x => x.idx)
                : null;

            if (existingOrder != null)
            {
                pendingBets = allPendingBets
                    .OrderBy(b => existingOrder.TryGetValue(b.Id, out _) ? 0 : 1)
                    .ThenBy(b => existingOrder.TryGetValue(b.Id, out int idx) ? idx : int.MaxValue)
                    .ThenBy(b => b.MatchStartTime.HasValue ? 1 : 0)
                    .ThenBy(b => b.MatchStartTime)
                    .ThenByDescending(b => b.CreatedAt)
                    .Skip((pendingPage - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();
            }
            else
            {
                pendingBets = allPendingBets
                    .OrderBy(b => b.MatchStartTime.HasValue ? 1 : 0)
                    .ThenBy(b => b.MatchStartTime)
                    .ThenByDescending(b => b.CreatedAt)
                    .Skip((pendingPage - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();
            }

            if (reviewingBetModel != null && reviewContext == "Pending")
            {
                currentReviewList = pendingBets;
                var newIdx = pendingBets.FindIndex(b => b.Id == reviewingBetModel.Id);
                if (newIdx >= 0) currentReviewIndex = newIdx;
            }
                
            hasMorePendingBets = totalPendingBets > (pendingPage * pageSize);
            await PopulateUserLookup(pendingBets.Select(b => b.UserId));
        }
        catch (Exception ex)
        {
            errorMessage = "Failed to load pending bets due to database error.";
            Console.WriteLine($"[Admin.razor] LoadPendingBets Error: {ex.Message}");
        }
    }

    private bool IsMissingAnyStartTime(Bet bet)
    {
        if (string.IsNullOrEmpty(bet.AiVisionResultJson)) return true;
        try {
            var res = System.Text.Json.JsonSerializer.Deserialize<BettingApp.Services.AiVisionExtractionResult>(bet.AiVisionResultJson);
            if (res?.Legs != null && res.Legs.Count > 0) {
                return res.Legs.Any(l => l.StartTime == null || l.StartTime <= DateTime.MinValue);
            }
            return true;
        } catch { return true; }
    }

    private async Task LoadApprovedBets()
    {
        try
        {
            using var context = DbFactory.CreateDbContext();
            allApprovedBets = await context.Bets
                .Include(b => b.AiEvaluation)
                .Include(b => b.Legs)
                .ThenInclude(l => l.BookmakerOdds)
                .Where(b => b.Status == "Approved")
                .ToListAsync();

            await PopulateUserLookup(allApprovedBets.Select(b => b.UserId));
            ApplyApprovedFilterAndSort();
        }
        catch (Exception ex)
        {
            errorMessage = "Failed to load approved bets due to database error.";
            Console.WriteLine($"[Admin.razor] LoadApprovedBets Error: {ex.Message}");
        }
    }

    private string GetApprovedDiscordName(Bet bet)
    {
        return userLookup.TryGetValue(bet.UserId, out var u) && !string.IsNullOrEmpty(u.DiscordName)
            ? u.DiscordName
            : (bet.UserName ?? "");
    }

    private string GetApprovedLastName(Bet bet)
    {
        return userLookup.TryGetValue(bet.UserId, out var u) ? (u.LastName ?? "") : "";
    }

    private int GetAiOutcomeSortRank(Bet bet)
    {
        if (IsCheckingOutcome(bet.Id)) return 0;
        var outcomeResult = bet.AiOutcomeResult;
        if (string.IsNullOrEmpty(outcomeResult)) return 8;
        if (outcomeResult == "Admin Override") return 5;

        BettingApp.Services.AiOutcomeResultData? parsed = null;
        if (_outcomeCache.TryGetValue(bet.Id, out var cached) && cached.Json == outcomeResult)
        {
            parsed = cached.Result;
        }
        else
        {
            try
            {
                parsed = System.Text.Json.JsonSerializer.Deserialize<BettingApp.Services.AiOutcomeResultData>(
                    outcomeResult,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch { }
            _outcomeCache[bet.Id] = (outcomeResult, parsed);
        }

        var lowerStatus = parsed?.OverallStatus?.ToLowerInvariant() ?? "";
        if (string.IsNullOrEmpty(lowerStatus)) return 8;
        if (lowerStatus.Contains("won")) return 1;
        if (lowerStatus.Contains("lost")) return 2;
        if (lowerStatus.Contains("void")) return 3;
        if (lowerStatus.Contains("unknown")) return 4;
        if (lowerStatus.Contains("in progress")) return 6;
        if (lowerStatus.Contains("not started") || lowerStatus.Contains("pending")) return 7;
        return 8;
    }

    private void ApplyApprovedFilterAndSort()
    {
        IEnumerable<Bet> filtered = allApprovedBets;

        if (!string.IsNullOrWhiteSpace(approvedSearchTerm))
        {
            var searchStr = approvedSearchTerm.Trim();
            if (int.TryParse(searchStr, out int searchId))
            {
                filtered = filtered.Where(b => b.Id == searchId || MatchesApprovedUserSearch(b, searchStr));
            }
            else
            {
                filtered = filtered.Where(b => MatchesApprovedUserSearch(b, searchStr));
            }
        }

        var filteredList = filtered.ToList();
        totalApprovedBets = filteredList.Count;

        int maxPage = Math.Max(1, (int)Math.Ceiling((double)totalApprovedBets / pageSize));
        if (approvedPage > maxPage) approvedPage = maxPage;

        IOrderedEnumerable<Bet> sorted = approvedSortColumn switch
        {
            "AiOutcome" => approvedSortDesc
                ? filteredList.OrderBy(b => GetAiOutcomeSortRank(b) == 8 ? 1 : 0).ThenByDescending(GetAiOutcomeSortRank)
                : filteredList.OrderBy(GetAiOutcomeSortRank),
            "NextCheckTime" => approvedSortDesc
                ? filteredList.OrderByDescending(b => b.NextCheckTime.HasValue).ThenByDescending(b => b.NextCheckTime)
                : filteredList.OrderBy(b => b.NextCheckTime.HasValue).ThenBy(b => b.NextCheckTime),
            "Id" => approvedSortDesc
                ? filteredList.OrderByDescending(b => b.Id)
                : filteredList.OrderBy(b => b.Id),
            "DiscordUsername" => approvedSortDesc
                ? filteredList.OrderByDescending(GetApprovedDiscordName, StringComparer.OrdinalIgnoreCase)
                : filteredList.OrderBy(GetApprovedDiscordName, StringComparer.OrdinalIgnoreCase),
            "LastName" => approvedSortDesc
                ? filteredList.OrderBy(b => string.IsNullOrWhiteSpace(GetApprovedLastName(b)) ? 1 : 0).ThenByDescending(GetApprovedLastName, StringComparer.OrdinalIgnoreCase)
                : filteredList.OrderBy(b => string.IsNullOrWhiteSpace(GetApprovedLastName(b)) ? 1 : 0).ThenBy(GetApprovedLastName, StringComparer.OrdinalIgnoreCase),
            "AmountNOK" => approvedSortDesc
                ? filteredList.OrderByDescending(b => b.AmountNOK ?? 0)
                : filteredList.OrderBy(b => b.AmountNOK ?? 0),
            "Odds" => approvedSortDesc
                ? filteredList.OrderByDescending(b => b.Odds)
                : filteredList.OrderBy(b => b.Odds),
            "PotentialPayout" => approvedSortDesc
                ? filteredList.OrderByDescending(b => b.PotentialPayout)
                : filteredList.OrderBy(b => b.PotentialPayout),
            "NetLiability" => approvedSortDesc
                ? filteredList.OrderByDescending(b => b.NetLiability)
                : filteredList.OrderBy(b => b.NetLiability),
            _ => approvedSortDesc
                ? filteredList.OrderByDescending(b => b.UpdatedAt)
                : filteredList.OrderBy(b => b.UpdatedAt)
        };

        approvedBets = sorted
            .ThenByDescending(b => b.Id)
            .Skip((approvedPage - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        hasMoreApprovedBets = totalApprovedBets > (approvedPage * pageSize);
    }

    private bool MatchesApprovedUserSearch(Bet bet, string searchStr)
    {
        if (!string.IsNullOrEmpty(bet.UserName) && bet.UserName.Contains(searchStr, StringComparison.OrdinalIgnoreCase))
            return true;
        if (userLookup.TryGetValue(bet.UserId, out var u))
        {
            if (!string.IsNullOrEmpty(u.DiscordName) && u.DiscordName.Contains(searchStr, StringComparison.OrdinalIgnoreCase))
                return true;
            if (!string.IsNullOrEmpty(u.LastName) && u.LastName.Contains(searchStr, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        if (userFirstNames.TryGetValue(bet.UserId, out var firstName) && !string.IsNullOrEmpty(firstName) && firstName.Contains(searchStr, StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }
    
    private async Task NextPendingPage() { pendingPage++; await LoadPendingBets(); }
    private async Task PrevPendingPage() { if (pendingPage > 1) { pendingPage--; await LoadPendingBets(); } }
    
    private Task NextApprovedPage() { approvedPage++; ApplyApprovedFilterAndSort(); return Task.CompletedTask; }
    private Task PrevApprovedPage() { if (approvedPage > 1) { approvedPage--; ApplyApprovedFilterAndSort(); } return Task.CompletedTask; }

    private void SortApproved(string column)
    {
        if (approvedSortColumn == column)
        {
            approvedSortDesc = !approvedSortDesc;
        }
        else
        {
            approvedSortColumn = column;
            approvedSortDesc = column is "Id" or "AmountNOK" or "Odds" or "PotentialPayout" or "NetLiability";
        }
        approvedPage = 1;
        ApplyApprovedFilterAndSort();
    }


    private async Task LoadCompletedBets()
    {
        try
        {
            using var context = DbFactory.CreateDbContext();
            
            var query = context.Bets
                .AsSplitQuery()
                .Include(b => b.AiEvaluation)
                .Include(b => b.Legs)
                .ThenInclude(l => l.BookmakerOdds)
                .AsQueryable();

            if (showOnlyMismatch)
            {
                // Pre-filter on SQL Server using Substring around "overallStatus" (and blank outcome checks)
                // so we never transfer 1.8M+ chars of full JSON blobs over the wire for rows that already match.
                var candidates = await context.Bets
                    .Where(b => b.AiEvaluation != null &&
                                b.AiEvaluation.AiOutcomeResult != "Admin Override" &&
                                (b.Status == "Won" || b.Status == "Lost" || b.Status == "Void") &&
                                (b.AiEvaluation.AiOutcomeResult == null ||
                                 b.AiEvaluation.AiOutcomeResult == "" ||
                                 !b.AiEvaluation.AiOutcomeResult.Contains("\"overallStatus\"") ||
                                 !((b.Status == "Won" && b.AiEvaluation.AiOutcomeResult.Substring(b.AiEvaluation.AiOutcomeResult.IndexOf("\"overallStatus\""), 42).Contains("WON")) ||
                                   (b.Status == "Lost" && b.AiEvaluation.AiOutcomeResult.Substring(b.AiEvaluation.AiOutcomeResult.IndexOf("\"overallStatus\""), 42).Contains("LOST")) ||
                                   (b.Status == "Void" && b.AiEvaluation.AiOutcomeResult.Substring(b.AiEvaluation.AiOutcomeResult.IndexOf("\"overallStatus\""), 42).Contains("VOID")))))
                    .Select(b => new
                    {
                        b.Id,
                        b.Status,
                        OutcomeSnippet = b.AiEvaluation!.AiOutcomeResult == null || b.AiEvaluation.AiOutcomeResult == ""
                            ? null
                            : (b.AiEvaluation.AiOutcomeResult.Contains("\"overallStatus\"")
                                ? b.AiEvaluation.AiOutcomeResult.Substring(b.AiEvaluation.AiOutcomeResult.IndexOf("\"overallStatus\""), 60)
                                : ""),
                        HasVision = b.AiEvaluation.AiVisionResultJson != null && b.AiEvaluation.AiVisionResultJson != ""
                    })
                    .ToListAsync();

                var mismatchedIds = candidates.Where(b => 
                {
                    if (string.IsNullOrWhiteSpace(b.OutcomeSnippet))
                    {
                        return b.HasVision;
                    }

                    string aiStatus = "";
                    int colonIdx = b.OutcomeSnippet.IndexOf(':', 15);
                    if (colonIdx >= 0)
                    {
                        int firstQuote = b.OutcomeSnippet.IndexOf('"', colonIdx + 1);
                        if (firstQuote >= 0)
                        {
                            int secondQuote = b.OutcomeSnippet.IndexOf('"', firstQuote + 1);
                            if (secondQuote > firstQuote)
                            {
                                aiStatus = b.OutcomeSnippet.Substring(firstQuote + 1, secondQuote - firstQuote - 1).Trim().ToUpperInvariant();
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(aiStatus)) return b.HasVision;
                    
                    if (aiStatus.Contains("WON")) aiStatus = "WON";
                    else if (aiStatus.Contains("LOST")) aiStatus = "LOST";
                    else if (aiStatus.Contains("VOID")) aiStatus = "VOID";
                    // UNKNOWN remains UNKNOWN

                    return b.Status.ToUpperInvariant() != aiStatus;
                }).Select(b => b.Id).ToList();

                if (mismatchedIds.Count == 0)
                {
                    hasMoreCompletedBets = false;
                    completedBets = new List<Bet>();
                    return;
                }

                query = query.Where(b => mismatchedIds.Contains(b.Id));
            }
            else if (showOnlyAutoApproved)
            {
                query = query.Where(b => b.IsAutoApproved && (b.Status == "Won" || b.Status == "Lost" || b.Status == "Void" || b.Status == "Rejected" || b.Status == "Cancelled"));
            }
            else if (showOnlyAdminOverride)
            {
                query = query.Where(b => b.AiEvaluation != null && b.AiEvaluation.AiOutcomeResult == "Admin Override");
            }
            else
            {
                // Base filter: only show bets that are in the completed statuses we currently have checked.
                query = query.Where(b => completedStatusFilters.Contains(b.Status));
            }

            if (!string.IsNullOrWhiteSpace(completedSearchTerm))
            {
                var searchStr = completedSearchTerm.Trim().ToLower();
                if (int.TryParse(searchStr, out int searchId))
                {
                    query = query.Where(b => b.Id == searchId);
                }
                else
                {
                    var matchingUserIds = await context.Users
                        .Where(u => (u.DiscordUsername != null && u.DiscordUsername.ToLower().Contains(searchStr)) 
                                 || (u.FirstName != null && u.FirstName.ToLower().Contains(searchStr))
                                 || (u.LastName != null && u.LastName.ToLower().Contains(searchStr)))
                        .Select(u => u.Id)
                        .ToListAsync();
                    query = query.Where(b => matchingUserIds.Contains(b.UserId));
                }
            }
            
            // Use Take(pageSize + 1) to determine if there is a next page, completely bypassing the massive CountAsync() scan!
            var fetchedBets = await query
                .OrderByDescending(b => b.UpdatedAt)
                .Skip((completedPage - 1) * pageSize)
                .Take(pageSize + 1)
                .ToListAsync();
                
            hasMoreCompletedBets = fetchedBets.Count > pageSize;
            completedBets = fetchedBets.Take(pageSize).ToList();
            await PopulateUserLookup(completedBets.Select(b => b.UserId));
        }
        catch (Exception ex)
        {
            errorMessage = "Failed to load completed bets due to database error.";
            Console.WriteLine($"[Admin.razor] LoadCompletedBets Error: {ex.Message}");
        }
    }

    private async Task NextPage() { completedPage++; await LoadCompletedBets(); }
    private async Task PrevPage() { if (completedPage > 1) { completedPage--; await LoadCompletedBets(); } }

    // --- CSV EXPORT FUNCTIONALITY ---
    private async Task ExportPendingBetsCsv()
    {
        using var context = DbFactory.CreateDbContext();
        var bets = await context.Bets.Include(b => b.AiEvaluation).Include(b => b.Legs).ThenInclude(l => l.BookmakerOdds).Where(b => b.Status == "Pending").OrderByDescending(b => b.UpdatedAt).ToListAsync();
        await GenerateAndDownloadCsv(bets, "PendingBets.csv", "Pending");
    }

    private async Task ExportActiveBetsCsv()
    {
        using var context = DbFactory.CreateDbContext();
        var bets = await context.Bets.Include(b => b.AiEvaluation).Include(b => b.Legs).ThenInclude(l => l.BookmakerOdds).Where(b => b.Status == "Approved").OrderByDescending(b => b.UpdatedAt).ToListAsync();
        await GenerateAndDownloadCsv(bets, "ActiveBets.csv", "Active");
    }

    private async Task ExportCompletedBetsCsv()
    {
        using var context = DbFactory.CreateDbContext();
        var completedStatuses = new[] { "Won", "Lost", "Void", "Rejected", "Cancelled" };
        var bets = await context.Bets.Include(b => b.AiEvaluation).Include(b => b.Legs).ThenInclude(l => l.BookmakerOdds).Where(b => completedStatuses.Contains(b.Status)).OrderByDescending(b => b.UpdatedAt).ToListAsync();
        await GenerateAndDownloadCsv(bets, "CompletedBets.csv", "Completed");
    }

    private async Task GenerateAndDownloadCsv(List<Bet> bets, string filename, string exportType)
    {
        await PopulateUserLookup(bets.Select(b => b.UserId));
        var sb = new System.Text.StringBuilder();

        string header = "ID,LastUpdatedLocal,LastUpdatedNorway,DiscordUsername,LastName,BetAmount,Odds,PotentialPayout,Status";
        if (exportType == "Active") header += ",NetLiability";
        if (exportType == "Completed") header += ",NetProfit";
        
        sb.AppendLine(header);

        foreach (var bet in bets)
        {
            var localTime = ToUserLocal(bet.UpdatedAt).ToString("g");
            var norwayTime = TimeHelpers.GetNorwayTime(bet.UpdatedAt).ToString("g");
            var discordName = userLookup.TryGetValue(bet.UserId, out var u) && !string.IsNullOrEmpty(u.DiscordName) ? u.DiscordName : bet.UserName;
            var lastName = userLookup.TryGetValue(bet.UserId, out var u2) ? u2.LastName : "";

            var line = $"{bet.Id},{EscapeCsv(localTime)},{EscapeCsv(norwayTime)},{EscapeCsv(discordName)},{EscapeCsv(lastName)},{bet.AmountNOK?.ToString()},{bet.Odds},{bet.PotentialPayout},{bet.Status}";

            if (exportType == "Active")
            {
                var netLiability = bet.PotentialPayout - ((decimal)(bet.AmountNOK ?? 0) - bet.FreeBetAmount);
                line += $",{netLiability}";
            }
            if (exportType == "Completed")
            {
                var netProfit = GetNetProfit(bet);
                line += $",{netProfit}";
            }

            sb.AppendLine(line);
        }

        await JSRuntime.InvokeVoidAsync("downloadCsv", filename, sb.ToString());
    }

    private string EscapeCsv(string field)
    {
        if (string.IsNullOrEmpty(field)) return "";
        // Escape existing quotes
        field = field.Replace("\"", "\"\"");
        
        // Wrap in quotes if it contains commas, quotes, or newlines
        if (field.Contains(",") || field.Contains("\"") || field.Contains("\n") || field.Contains("\r"))
        {
            return $"\"{field}\"";
        }
        
        return field;
    }

    private MarkupString GetStartTimeDisplay(Bet bet)
    {
        if (string.IsNullOrEmpty(bet.AiVisionResultJson)) 
            return new MarkupString("<span class='text-muted small'>Pending...</span>");
            
        BettingApp.Services.AiVisionExtractionResult? res = null;
        if (_visionCache.TryGetValue(bet.Id, out var cached) && cached.Json == bet.AiVisionResultJson) {
            res = cached.Result;
        } else {
            try { res = System.Text.Json.JsonSerializer.Deserialize<BettingApp.Services.AiVisionExtractionResult>(bet.AiVisionResultJson, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }); } catch {}
            _visionCache[bet.Id] = (bet.AiVisionResultJson, res);
        }
            
        try 
        {
            if (res?.Legs != null && res.Legs.Count > 0) 
            {
                int total = res.Legs.Count;
                int withTime = res.Legs.Count(l => l.StartTime > DateTime.MinValue);
                
                if (withTime == total) 
                {
                    var earliest = res.Legs.Min(l => l.StartTime);
                    var localTime = ToUserLocal(earliest ?? DateTime.MinValue);
                    return new MarkupString($"<span class='badge bg-light text-dark border shadow-sm px-2 py-1' style='font-size: 0.85rem;'><i class='bi bi-calendar-event text-primary me-1'></i> {localTime:MMM d, HH:mm}</span>");
                } 
                else if (withTime == 0)
                {
                    return new MarkupString($"<span class='badge bg-warning-subtle text-warning-emphasis border border-warning-subtle shadow-sm px-2 py-1' style='font-size: 0.85rem;'><i class='bi bi-search me-1'></i> Requires Lookup</span>");
                }
                else 
                {
                    return new MarkupString($"<span class='badge bg-warning-subtle text-warning-emphasis border border-warning-subtle shadow-sm px-2 py-1' style='font-size: 0.85rem;'><i class='bi bi-exclamation-triangle-fill text-warning me-1'></i> Found {withTime}/{total} Legs</span>");
                }
            }
            return new MarkupString("<span class='text-muted'>-</span>");
        } 
        catch 
        { 
            return new MarkupString("<span class='text-danger'>Error</span>"); 
        }
    }

    private RenderFragment GetAiOutcomeBadge(Bet bet) => builder =>
    {
        var outcomeResult = bet.AiOutcomeResult;
        if (string.IsNullOrEmpty(outcomeResult)) return;

        if (outcomeResult == "Admin Override")
        {
            builder.OpenElement(0, "span");
            builder.AddAttribute(1, "class", "badge bg-primary text-white shadow-sm d-inline-flex align-items-center");
            builder.OpenElement(2, "i");
            builder.AddAttribute(3, "class", "bi bi-shield-check me-1");
            builder.CloseElement();
            builder.AddContent(4, "Admin Override");
            builder.CloseElement();
            return;
        }

        try
        {
            BettingApp.Services.AiOutcomeResultData? parsed = null;
            if (_outcomeCache.TryGetValue(bet.Id, out var cached) && cached.Json == outcomeResult) {
                parsed = cached.Result;
            } else {
                try { parsed = System.Text.Json.JsonSerializer.Deserialize<BettingApp.Services.AiOutcomeResultData>(outcomeResult, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }); } catch {}
                _outcomeCache[bet.Id] = (outcomeResult, parsed);
            }

            if (parsed != null && !string.IsNullOrEmpty(parsed.OverallStatus))
            {
                var lowerStatus = parsed.OverallStatus.ToLower();
                
                if (lowerStatus.Contains("won")) {
                    builder.OpenElement(0, "span");
                    builder.AddAttribute(1, "class", "badge bg-success text-white shadow-sm d-inline-flex align-items-center");
                    builder.OpenElement(2, "i");
                    builder.AddAttribute(3, "class", "bi bi-check-lg me-1");
                    builder.CloseElement();
                    builder.AddContent(4, "Won");
                    builder.CloseElement();
                } else if (lowerStatus.Contains("lost")) {
                    builder.OpenElement(0, "span");
                    builder.AddAttribute(1, "class", "badge bg-danger text-white shadow-sm d-inline-flex align-items-center");
                    builder.OpenElement(2, "i");
                    builder.AddAttribute(3, "class", "bi bi-x-lg me-1");
                    builder.CloseElement();
                    builder.AddContent(4, "Lost");
                    builder.CloseElement();
                } else if (lowerStatus.Contains("void")) {
                    builder.OpenElement(0, "span");
                    builder.AddAttribute(1, "class", "badge bg-secondary text-white shadow-sm d-inline-flex align-items-center");
                    builder.OpenElement(2, "i");
                    builder.AddAttribute(3, "class", "bi bi-dash-lg me-1");
                    builder.CloseElement();
                    builder.AddContent(4, "Void");
                    builder.CloseElement();
                } else if (lowerStatus.Contains("unknown")) {
                    builder.OpenElement(0, "span");
                    builder.AddAttribute(1, "class", "badge bg-warning text-dark shadow-sm d-inline-flex align-items-center");
                    builder.OpenElement(2, "i");
                    builder.AddAttribute(3, "class", "bi bi-question-lg me-1");
                    builder.CloseElement();
                    builder.AddContent(4, "Unknown");
                    builder.CloseElement();
                }
                return;
            }
        }
        catch { }
    };

}
