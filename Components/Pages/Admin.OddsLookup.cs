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

// OddsPapi market comparisons, leg matching, and bookmaker deeplinks
public partial class Admin
{
    public class LegOddsResult
    {
        public bool IsLoading { get; set; }
        public string? ErrorMessage { get; set; }
        public string? WarningMessage { get; set; }
        public BettingApp.Models.OddsPapiSearchResult? SearchResult { get; set; }
        public string? SelectedMarketId { get; set; }
        public string? HighlightedOutcome { get; set; }
        public DateTime? LookedUpAt { get; set; }
        public Dictionary<string, BettingApp.Models.OddsData> MatchedBookieOdds { get; set; } = new();
    }

    // --- Odds Lookup & Deep Link State ---
    private Dictionary<int, LegOddsResult> LegOddsCache = new();
    private HashSet<int> ExpandedBetIds = new();
    private bool IsCheckingAllOdds = false;
    private int CheckedLegsCount = 0;

    private bool ShowOddsLookup = false;
    private bool IsSearchingOdds = false;
    private string OddsSearchTeam = "";    // OddsPapi UI State
    private BettingApp.Models.OddsPapiSearchResult? PapiSearchResult = null;
    private string SelectedPapiMarketId = "";
    private string? OddsLookupErrorMessage = null;
    private string? OddsLookupWarningMessage = null;

    private bool openInNativeApp = false;
    private bool isDesktop = false;
    private bool isAndroid = false;
    private string unibetPwaPackageName = "";
    private string pinnaclePwaPackageName = "";
    private string betssonPwaPackageName = "";
    private string bet365PwaPackageName = "";
    private string coolbetPwaPackageName = "";

    private async Task HideSavedOddsFromCache()
    {
        LegOddsCache.Clear();
        try { await reviewModalRef.FocusAsync(); } catch { }
    }

    private async Task LoadSavedOddsIntoCache()
    {
        if (reviewingBetModel == null) return;
        LegOddsCache.Clear();
        for (int i = 0; i < reviewingBetModel.Legs.Count; i++)
        {
            var leg = reviewingBetModel.Legs[i];
            var matched = new Dictionary<string, BettingApp.Models.OddsData>();
            
            if (leg.BookmakerOdds.Any())
            {
                foreach (var bo in leg.BookmakerOdds)
                {
                    matched[bo.BookmakerName] = new BettingApp.Models.OddsData 
                    { 
                        Price = (double)bo.OddsValue,
                        Limit = (double?)bo.Limit
                    };
                }
            }
            
            LegOddsCache[i] = new LegOddsResult 
            { 
                IsLoading = false,
                MatchedBookieOdds = matched,
                WarningMessage = leg.BookmakerOdds.Any() ? "Showing historical odds snapshot from time of approval." : "No historical odds saved for this leg.",
                LookedUpAt = leg.BookmakerOdds.FirstOrDefault()?.LookedUpAt
            };
        }
        
        try { await reviewModalRef.FocusAsync(); } catch { }
    }

    private void ToggleBetRow(int betId)
    {
        if (ExpandedBetIds.Contains(betId)) ExpandedBetIds.Remove(betId);
        else ExpandedBetIds.Add(betId);
    }

    private async Task CheckAllOddsAsync()
    {
        if (IsCheckingAllOdds) return;
        var legs = AiVisionResult?.Legs;
        if (legs == null || reviewingBetModel == null) return;
        
        int runningBetId = reviewingBetModel.Id;
        IsCheckingAllOdds = true;
        CheckedLegsCount = 0;
        LegOddsCache.Clear();
        
        for (int i = 0; i < legs.Count; i++)
        {
            LegOddsCache[i] = new LegOddsResult { IsLoading = true };
        }
        
        StateHasChanged();
        await Task.Delay(16);

        var fetchTasks = new List<Task<(BettingApp.Models.OddsPapiSearchResult? result, string? error)>>();
        for (int i = 0; i < legs.Count; i++)
        {
            var leg = legs[i];
            string teamToSearch = leg.Match ?? leg.Selection ?? "";
            bool isLive = AiVisionResult?.IsLive ?? false;
            
            // Kick off background processing concurrently but stagger them by 50ms
            // This mathematically guarantees that Leg 1 reaches the HTTP Throttle Lock first,
            // Leg 2 reaches it second, etc. Eliminating the "Throttle Lottery" where Leg 4 locks out Leg 1.
            int taskIndex = i;
            fetchTasks.Add(Task.Run(async () => {
                await Task.Delay(taskIndex * 50);
                if (reviewingBetModel == null || reviewingBetModel.Id != runningBetId) return ((BettingApp.Models.OddsPapiSearchResult?)null, (string?)null);
                return await OddsApi.SearchOddsComparisonAsync(teamToSearch, reviewingBetModel?.Id, isLive, leg.Sport);
            }));
        }
        
        // Await them sequentially to ensure the UI updates in strict order (1, 2, 3...)
        for (int i = 0; i < fetchTasks.Count; i++)
        {
            var leg = legs[i];
            var (apiResult, error) = await fetchTasks[i];
            
            if (reviewingBetModel == null || reviewingBetModel.Id != runningBetId) 
            {
                LegOddsCache.Clear();
                break;
            }

            ProcessLegOddsSync(leg, i, apiResult, error);

            CheckedLegsCount++;
            StateHasChanged();
            
            // Give UI thread a tiny slice to render the progress smoothly
            await Task.Delay(1);
        }
        
        if (reviewingBetModel == null || reviewingBetModel.Id != runningBetId) 
        {
            LegOddsCache.Clear();
        }

        if (reviewingBetModel != null && reviewingBetModel.Id == runningBetId)
        {
            IsCheckingAllOdds = false;

            // Save the newly found start times back to the DB!
            SaveAiVisionResultToDatabase();

            StateHasChanged();
            try { await reviewModalRef.FocusAsync(); } catch { }
        }
    }

    private void ProcessLegOddsSync(BettingApp.Services.AiVisionLeg leg, int index, BettingApp.Models.OddsPapiSearchResult? apiResult, string? error)
    {
        if (!LegOddsCache.TryGetValue(index, out var resultData))
        {
            resultData = new LegOddsResult();
            LegOddsCache[index] = resultData;
        }
        
        if (apiResult == null)
        {
            if (error != null && error.StartsWith("Castle doesn't yet pay for odds for"))
            {
                resultData.WarningMessage = error;
                resultData.SearchResult = new BettingApp.Models.OddsPapiSearchResult { MatchName = leg.Match ?? leg.Selection ?? "" };
                resultData.IsLoading = false;
                return;
            }

            resultData.ErrorMessage = error ?? "No OddsPapi matches found.";
            resultData.IsLoading = false;
            return;
        }

        resultData.SearchResult = apiResult;

        if (apiResult.StartTime > DateTime.MinValue)
        {
            leg.StartTime = apiResult.StartTime;
            if (AiVisionResult?.Legs != null)
            {
                foreach (var otherLeg in AiVisionResult.Legs)
                {
                    if (!string.IsNullOrEmpty(otherLeg.Match) && otherLeg.Match.Equals(leg.Match, StringComparison.OrdinalIgnoreCase))
                    {
                        otherLeg.StartTime = apiResult.StartTime;
                    }
                }
            }
        }

        // Auto Select Market
        string preSelectCategory = leg.Market ?? "";
        
        var bestMatch = BettingApp.Services.OddsMatchingLogic.FindBestMarket(apiResult.Markets, preSelectCategory, leg.Match, leg.Selection, MarketMapper);

        bool isCanonicalTranslation = true;
        if (bestMatch != null)
        {
            resultData.SelectedMarketId = bestMatch.MarketId;
            
            // Check line matching logic
            if (!isCanonicalTranslation)
            {
                var lineRegex = new System.Text.RegularExpressions.Regex(@"\d+(?:\.\d+)?");
                var lineMatch = lineRegex.Match(leg.Selection ?? "");
                if (lineMatch.Success && double.TryParse(lineMatch.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double aiVal))
                {
                    bool isTeamNumber = false;
                    bool isLineMarket = bestMatch.MarketName.IndexOf("Handicap", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        bestMatch.MarketName.IndexOf("Over", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        bestMatch.MarketName.IndexOf("Under", StringComparison.OrdinalIgnoreCase) >= 0;
                    
                    if (!isLineMarket) isTeamNumber = true;
                    
                    if (!string.IsNullOrEmpty(apiResult.MatchName) && !isTeamNumber)
                    {
                        var matchSplit = apiResult.MatchName.Split(new[] { " vs ", " v ", " - " }, StringSplitOptions.None);
                        if (matchSplit.Length >= 2)
                        {
                            string t1 = matchSplit[0].Trim();
                            string t2 = matchSplit[1].Trim();
                            string sel = leg.Selection ?? "";
                            if ((t1.Contains(lineMatch.Value) && sel.Contains(t1)) || 
                                (t2.Contains(lineMatch.Value) && sel.Contains(t2)) ||
                                sel.Equals(t1, StringComparison.OrdinalIgnoreCase) ||
                                sel.Equals(t2, StringComparison.OrdinalIgnoreCase))
                            {
                                isTeamNumber = true;
                            }
                        }
                    }

                    if (!isTeamNumber)
                    {
                        bool lineExists = bestMatch.OutcomeNames.Values.Any(v => {
                            string translatedV = BettingApp.Services.OddsMatchingLogic.GetTranslatedOutcomeName(v, apiResult.MatchName ?? "");
                            var vMatches = lineRegex.Matches(translatedV);
                            foreach (System.Text.RegularExpressions.Match match in vMatches)
                            {
                                if (double.TryParse(match.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double vVal))
                                {
                                    if (Math.Abs(aiVal - vVal) < 0.001) return true;
                                }
                            }
                            return false;
                        });
                        
                        if (!lineExists)
                        {
                            resultData.WarningMessage = $"The betslip is for '{preSelectCategory}'. Automatically showing '{bestMatch.MarketName}' instead, but verify if the specific line is available.";
                        }
                    }
                }
            }

            // Figure out the highlighted outcome!
            if (apiResult.BookmakerOdds.TryGetValue(bestMatch.MarketId, out var bookiesDict))
            {
                string? bestOutcome = null;

                foreach (var kvp in bestMatch.OutcomeNames)
                {
                    string oId = kvp.Key;
                    string oName = kvp.Value;
                    string displayOName = BettingApp.Services.OddsMatchingLogic.GetTranslatedOutcomeName(oName, apiResult.MatchName ?? "");
                    
                    bool isMatch = BettingApp.Services.OddsMatchingLogic.IsOutcomeMatch(oName, displayOName, leg, apiResult, bestMatch.MarketName);

                    if (isMatch)
                    {
                        bestOutcome = oName;
                        resultData.HighlightedOutcome = displayOName;
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(bestOutcome))
                {
                    var targets = new[] { "pinnacle+30", "unibet.se", "coolbet", "bet365", "betsson" };
                    foreach (var tb in targets)
                    {
                        if (bookiesDict.TryGetValue(tb, out var bData) && bData.TryGetValue(bestOutcome, out var pd))
                        {
                            resultData.MatchedBookieOdds[tb] = pd;
                        }
                    }
                }
                else if (string.IsNullOrEmpty(resultData.WarningMessage))
                {
                    resultData.WarningMessage = $"OddsPapi doesn't offer selection '{leg.Selection}' for '{bestMatch.MarketName}'.";
                }
            }
        }
        else
        {
            if (apiResult.StatusId == 2)
            {
                resultData.WarningMessage = "This match has already finished. Bookmakers do not offer odds on ended matches.";
            }
            else
            {
                resultData.WarningMessage = $"The betslip is for '{preSelectCategory}', but OddsPapi doesn't offer it for this match.";
            }
        }

        resultData.LookedUpAt = DateTime.UtcNow;
        resultData.IsLoading = false;
    }

    private string GetFallbackBookieUrl(string bookieKey, string matchName)
    {
        string searchTerm = "";
        if (!string.IsNullOrEmpty(matchName))
        {
            var tokens = matchName.Split(new[] { " vs ", " v ", " - " }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length > 0)
            {
                searchTerm = Uri.EscapeDataString(tokens[0].Trim());
            }
            else
            {
                searchTerm = Uri.EscapeDataString(matchName.Trim());
            }
        }

        switch (bookieKey.ToLower())
        {
            case "coolbet":
                return "https://www.coolbet.com/se/sports/recommendations";

            case "unibet.se":
            case "unibet":
                return "https://www.unibet.se/betting/sports/home";

            case "pinnacle+30":
            case "pinnacle":
                // Pinnacle's search isn't standard in URL, link to sports home
                return "https://www.pinnacle.com/en/";

            case "betsson":
                if (!string.IsNullOrEmpty(searchTerm))
                    return $"https://www.betsson.com/sv/odds/search?q={searchTerm}";
                return "https://www.betsson.com/sv/odds";

            case "bet365":
                // Bet365 uses complex hash routing for search, link to sports home
                return "https://www.bet365.com";

            default:
                return "";
        }
    }

    private string FormatBookieUrl(string url, string bookieKey = "")
    {
        if (openInNativeApp && isAndroid && !string.IsNullOrEmpty(url))
        {
            var strippedUrl = url.Replace("https://", "").Replace("http://", "");
            string pkgStr = "";
            string rawPkg = bookieKey switch
            {
                "unibet.se" => unibetPwaPackageName,
                "pinnacle" => pinnaclePwaPackageName,
                "betsson" => betssonPwaPackageName,
                "bet365" => bet365PwaPackageName,
                "coolbet" => coolbetPwaPackageName,
                _ => ""
            };

            if (!string.IsNullOrWhiteSpace(rawPkg))
            {
                var cleanPkg = rawPkg.Trim();
                if (cleanPkg.Contains("org.chromium.webapk."))
                {
                    var startIndex = cleanPkg.IndexOf("org.chromium.webapk.");
                    cleanPkg = cleanPkg.Substring(startIndex).Split(' ')[0];
                }
                pkgStr = $";package={cleanPkg}";
                
                if (bookieKey == "unibet.se" && strippedUrl.StartsWith("unibet.se"))
                {
                    strippedUrl = "www." + strippedUrl;
                }
            }
            return $"intent://{strippedUrl}#Intent;scheme=https;action=android.intent.action.VIEW;category=android.intent.category.BROWSABLE;category=android.intent.category.DEFAULT{pkgStr};end";
        }
        return url;
    }

    private void SearchOddsForLeg(BettingApp.Services.AiVisionLeg leg)
    {
        ActiveLookupLeg = leg;
        if (string.IsNullOrEmpty(leg.Match)) return;
        
        OddsSearchTeam = leg.Match;
        ShowOddsLookup = true;
        _ = SearchOdds(leg.Market);
    }

    private async Task ToggleOddsLookup()
    {
        ShowOddsLookup = !ShowOddsLookup;
    }



    private async Task SearchOdds(string? preSelectCategory = null)
    {
        if (string.IsNullOrWhiteSpace(OddsSearchTeam)) return;
        
        preSelectCategory ??= ActiveLookupLeg?.Market;

        IsSearchingOdds = true;
        OddsLookupErrorMessage = null;
        OddsLookupWarningMessage = null;
        PapiSearchResult = null;
        SelectedPapiMarketId = "";
        StateHasChanged();
        await Task.Delay(16);

        BettingApp.Models.OddsPapiSearchResult? result;
        string? error;
        bool isLive = AiVisionResult?.IsLive ?? false;
        (result, error) = await Task.Run(async () => await OddsApi.SearchOddsComparisonAsync(OddsSearchTeam, reviewingBetModel?.Id, isLive, ActiveLookupLeg?.Sport));
        
        if (result == null)
        {
            OddsLookupErrorMessage = error ?? "No OddsPapi matches found for that team, or error occurred.";
            Logger.LogInformation($" [Bet #{reviewingBetModel?.Id}] OddsPapi: {OddsLookupErrorMessage} (Searched: '{OddsSearchTeam}')");
        }
        else
        {
            PapiSearchResult = result;
            
            // Save the start time to the DB right away so we don't have to query AI for it later!
            if (reviewingBetModel != null && result.StartTime > DateTime.MinValue)
            {
                if (ActiveLookupLeg != null) 
                {
                    ActiveLookupLeg.StartTime = result.StartTime;
                    
                    if (AiVisionResult?.Legs != null)
                    {
                        foreach (var leg in AiVisionResult.Legs)
                        {
                            if (!string.IsNullOrEmpty(leg.Match) && leg.Match.Equals(ActiveLookupLeg.Match, StringComparison.OrdinalIgnoreCase))
                            {
                                leg.StartTime = result.StartTime;
                            }
                        }
                    }
                }
                
                SaveAiVisionResultToDatabase();
                
                // Instantly resort the pending bets list in memory so it reflects correctly on the UI
                if (pendingBets != null && pendingBets.Any(b => b.Id == reviewingBetModel.Id))
                {
                    pendingBets = pendingBets
                        .OrderBy(b => b.MatchStartTime.HasValue ? 1 : 0)
                        .ThenBy(b => b.MatchStartTime)
                        .ThenByDescending(b => b.CreatedAt)
                        .ToList();
                }
            }

            if (result.Markets.Count == 0)
            {
                OddsLookupWarningMessage = "Match found, but OddsPapi returned 0 markets for Unibet SE, Betsson, and Bet365.";
                Logger.LogInformation($" [Bet #{reviewingBetModel?.Id}] OddsPapi: {OddsLookupWarningMessage} (Match: {result.MatchName})");
            }
            else if (!string.IsNullOrEmpty(preSelectCategory))
            {
                var bestMatch = BettingApp.Services.OddsMatchingLogic.FindBestMarket(result.Markets, preSelectCategory, ActiveLookupLeg?.Match, ActiveLookupLeg?.Selection, MarketMapper);
                bool isCanonicalTranslation = true; // We abstract this away

                    
                if (bestMatch != null)
                {
                    SelectedPapiMarketId = bestMatch.MarketId;
                    
                    if (!isCanonicalTranslation)
                    {
                        var lineRegex = new System.Text.RegularExpressions.Regex(@"\d+(?:\.\d+)?");
                        var lineMatch = lineRegex.Match(ActiveLookupLeg?.Selection ?? "");
                        if (lineMatch.Success && double.TryParse(lineMatch.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double aiVal))
                        {
                            bool isTeamNumber = false;
                            
                            bool isLineMarket = bestMatch.MarketName.IndexOf("Handicap", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                bestMatch.MarketName.IndexOf("Over", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                bestMatch.MarketName.IndexOf("Under", StringComparison.OrdinalIgnoreCase) >= 0;
                            
                            if (!isLineMarket)
                            {
                                isTeamNumber = true;
                            }
                            
                            if (!string.IsNullOrEmpty(result.MatchName) && !isTeamNumber)
                            {
                                var matchSplit = result.MatchName.Split(new[] { " vs ", " v ", " - " }, StringSplitOptions.None);
                                if (matchSplit.Length >= 2)
                                {
                                    string t1 = matchSplit[0].Trim();
                                    string t2 = matchSplit[1].Trim();
                                    string sel = ActiveLookupLeg?.Selection ?? "";
                                    if ((t1.Contains(lineMatch.Value) && sel.Contains(t1)) || 
                                        (t2.Contains(lineMatch.Value) && sel.Contains(t2)) ||
                                        sel.Equals(t1, StringComparison.OrdinalIgnoreCase) ||
                                        sel.Equals(t2, StringComparison.OrdinalIgnoreCase))
                                    {
                                        isTeamNumber = true;
                                    }
                                }
                            }

                            if (!isTeamNumber)
                            {
                                bool lineExists = bestMatch.OutcomeNames.Values.Any(v => {
                                    string translatedV = BettingApp.Services.OddsMatchingLogic.GetTranslatedOutcomeName(v, result.MatchName ?? "");
                                    var vMatches = lineRegex.Matches(translatedV);
                                    foreach (System.Text.RegularExpressions.Match match in vMatches)
                                    {
                                        if (double.TryParse(match.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double vVal))
                                        {
                                            if (Math.Abs(aiVal - vVal) < 0.001) return true;
                                        }
                                    }
                                    return false;
                                });
                                
                                if (!lineExists)
                                {
                                    OddsLookupWarningMessage = $"The betslip is for '{preSelectCategory}'. Automatically showing '{bestMatch.MarketName}' instead, but verify if the specific line is available.";
                                    Logger.LogInformation($" [Bet #{reviewingBetModel?.Id}] OddsPapi: {OddsLookupWarningMessage} (Match: {result.MatchName})");
                                }
                            }
                        }
                    }
                }
                else
                {
                    OddsLookupWarningMessage = $"The betslip is for '{preSelectCategory}', but OddsPapi doesn't offer it for this match.";
                    Logger.LogInformation($" [Bet #{reviewingBetModel?.Id}] OddsPapi: {OddsLookupWarningMessage} (Match: {result.MatchName})");
                }
            }
            
            if (string.IsNullOrEmpty(SelectedPapiMarketId))
            {
                // Try to find a standard market
                var ftMatch = result.Markets.FirstOrDefault(m => m.MarketName.Contains("1x2", StringComparison.OrdinalIgnoreCase) || m.MarketName.Contains("Full Time", StringComparison.OrdinalIgnoreCase));
                if (ftMatch != null)
                {
                    SelectedPapiMarketId = ftMatch.MarketId;
                }
                else
                {
                    SelectedPapiMarketId = result.Markets.FirstOrDefault()?.MarketId ?? "";
                }
            }
        }

        IsSearchingOdds = false;
        try { await reviewModalRef.FocusAsync(); } catch { }
        StateHasChanged();
        
        if (ActiveLookupLeg != null && !string.IsNullOrEmpty(SelectedPapiMarketId))
        {
            // Give DOM a tiny moment to render the new table rows, then scroll
            _ = Task.Run(async () => {
                await Task.Delay(100);
                await InvokeAsync(async () => {
                    try {
                        await JSRuntime.InvokeVoidAsync("scrollToElementId", "highlighted-odds-row");
                    } catch { }
                });
            });
        }
    }



}
