#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BettingApp.Models;

namespace BettingApp.Services;

public partial class AutoApprovalService
{
    public static readonly string[] AllSupportedBookmakers =
    {
        "pinnacle+30",
        "unibet.se",
        "coolbet",
        "bet365",
        "betsson"
    };

    public class AutoApprovalEvaluationResult
    {
        public bool CanAutoApprove { get; set; }
        public int MatchedTier { get; set; }
        public string MatchedTierName { get; set; } = "";
        public string Reason { get; set; } = "";
        public bool MetadataUpdated { get; set; }
        public bool DetectedLiveFromOp { get; set; }
        public AiVisionExtractionResult? UpdatedVisionResult { get; set; }
        public Dictionary<int, EvaluatedLegOdds> LegResults { get; set; } = new();
    }

    public class EvaluatedLegOdds
    {
        public string? ErrorMessage { get; set; }
        public string? WarningMessage { get; set; }
        public OddsPapiSearchResult? SearchResult { get; set; }
        public string? SelectedMarketId { get; set; }
        public string? HighlightedOutcome { get; set; }
        public DateTime LookedUpAt { get; set; } = DateTime.UtcNow;
        public Dictionary<string, OddsData> MatchedBookieOdds { get; set; } = new();
    }

    public static bool TryCalculateTier2BookieAverage(
        IReadOnlyDictionary<string, OddsData>? matchedBookieOdds,
        out decimal averageOdds,
        out int validBookieCount)
    {
        averageOdds = 0m;
        validBookieCount = 0;

        if (matchedBookieOdds == null || matchedBookieOdds.Count == 0)
        {
            return false;
        }

        var validPrices = new List<decimal>(AllSupportedBookmakers.Length);
        foreach (var bookieKey in AllSupportedBookmakers)
        {
            if (matchedBookieOdds.TryGetValue(bookieKey, out var data) &&
                data != null &&
                data.Price > 1.0)
            {
                validPrices.Add(Convert.ToDecimal(data.Price));
            }
        }

        validBookieCount = validPrices.Count;
        if (validBookieCount < 2)
        {
            return false;
        }

        averageOdds = validPrices.Average();
        return true;
    }

    public static bool TryParseOddsDecimal(string? rawOdds, out decimal oddsValue)
    {
        oddsValue = 0m;
        if (string.IsNullOrWhiteSpace(rawOdds)) return false;
        string clean = rawOdds.Trim().Replace(",", ".");
        return decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out oddsValue);
    }

    public static bool IsLegOddsWithinPinnacleTolerance(
        decimal slipLegOdds,
        decimal referenceOdds,
        decimal maxOverPercent,
        out decimal maxAllowedOdds,
        out decimal actualOverPercent)
    {
        maxAllowedOdds = referenceOdds * (1m + (maxOverPercent / 100m));
        actualOverPercent = referenceOdds > 0m ? ((slipLegOdds - referenceOdds) / referenceOdds) * 100m : 0m;
        return slipLegOdds <= maxAllowedOdds;
    }

    public static bool IsTotalOddsConsistentWithLegs(
        decimal totalBetOdds,
        IReadOnlyList<decimal> legOdds,
        out string? mismatchReason)
    {
        mismatchReason = null;
        if (legOdds == null || legOdds.Count == 0)
        {
            mismatchReason = "No leg odds provided.";
            return false;
        }

        if (legOdds.Count == 1)
        {
            // Single leg: dbBet.Odds is floored to 0.05 in Home.razor, plus allow +-0.01 rounding
            decimal expectedFloored = Math.Floor(legOdds[0] / 0.05m) * 0.05m;
            decimal minSingle = Math.Min(expectedFloored, legOdds[0]) - 0.06m;
            decimal maxSingle = Math.Max(expectedFloored, legOdds[0]) + 0.06m;
            if (totalBetOdds < minSingle || totalBetOdds > maxSingle)
            {
                mismatchReason = $"Total odds ({totalBetOdds:0.00}) does not match single leg odds ({legOdds[0]:0.00}).";
                return false;
            }
            return true;
        }

        // Multi-leg parlay: bookmakers often multiply unrounded 3-decimal odds (e.g. 1.515 * 1.725 = 2.613)
        // while displaying 2-decimal truncated/rounded leg odds (e.g. 1.51 and 1.72).
        decimal minProduct = 1m;
        decimal maxProduct = 1m;
        foreach (var l in legOdds)
        {
            minProduct *= Math.Max(1.001m, l - 0.01m);
            maxProduct *= (l + 0.01m);
        }

        decimal minAllowed = (Math.Floor(minProduct / 0.05m) * 0.05m) - 0.06m;
        decimal maxAllowed = maxProduct + 0.06m;

        if (totalBetOdds < minAllowed || totalBetOdds > maxAllowed)
        {
            decimal exactProduct = legOdds.Aggregate(1m, (acc, x) => acc * x);
            mismatchReason = $"Total odds ({totalBetOdds:0.00}) does not match product of legs ({exactProduct:0.00}, allowed {minAllowed:0.00}–{maxAllowed:0.00}).";
            return false;
        }

        return true;
    }

    public static EvaluatedLegOdds EvaluateLegOdds(
        AiVisionLeg leg,
        OddsPapiSearchResult? apiResult,
        string? error,
        MarketMappingService marketMapper)
    {
        var resultData = new EvaluatedLegOdds
        {
            LookedUpAt = DateTime.UtcNow
        };

        if (apiResult == null)
        {
            if (error != null && error.StartsWith("Castle doesn't yet pay for odds for"))
            {
                resultData.WarningMessage = error;
                resultData.SearchResult = new OddsPapiSearchResult { MatchName = leg.Match ?? leg.Selection ?? "" };
                return resultData;
            }

            resultData.ErrorMessage = error ?? "No OddsPapi matches found.";
            return resultData;
        }

        resultData.SearchResult = apiResult;

        string preSelectCategory = leg.Market ?? "";
        var bestMatch = OddsMatchingLogic.FindBestMarket(apiResult.Markets, preSelectCategory, leg.Match, leg.Selection, marketMapper, apiResult.MatchName);

        if (bestMatch != null)
        {
            resultData.SelectedMarketId = bestMatch.MarketId;

            if (apiResult.BookmakerOdds.TryGetValue(bestMatch.MarketId, out var bookiesDict))
            {
                string? bestOutcome = null;
                var ctx = new OddsMatchingLogic.OutcomeMatchingContext(leg, apiResult, bestMatch.MarketName);

                foreach (var kvp in bestMatch.OutcomeNames)
                {
                    string oName = kvp.Value;
                    string displayOName = OddsMatchingLogic.GetTranslatedOutcomeName(oName, apiResult.MatchName ?? "");

                    if (OddsMatchingLogic.IsOutcomeMatch(oName, displayOName, ctx))
                    {
                        bestOutcome = oName;
                        resultData.HighlightedOutcome = displayOName;
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(bestOutcome))
                {
                    foreach (var tb in AllSupportedBookmakers)
                    {
                        if (bookiesDict.TryGetValue(tb, out var bData) && bData.TryGetValue(bestOutcome, out var pd))
                        {
                            resultData.MatchedBookieOdds[tb] = pd;
                        }
                    }
                }
                else
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

        return resultData;
    }
}
