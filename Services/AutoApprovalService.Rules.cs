#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BettingApp.Data;
using BettingApp.Models;

namespace BettingApp.Services;

public partial class AutoApprovalService
{
    public async Task<AutoApprovalEvaluationResult> CheckIfBetCanBeAutoApprovedAsync(
        Bet dbBet,
        ApplicationUser? dbUser,
        SystemSetting settings,
        bool isFinalCheck,
        CancellationToken ct = default)
    {
        var eval = new AutoApprovalEvaluationResult();

        AiVisionExtractionResult? aiVision = null;
        if (!string.IsNullOrEmpty(dbBet.AiVisionResultJson))
        {
            try
            {
                aiVision = JsonSerializer.Deserialize<AiVisionExtractionResult>(
                    dbBet.AiVisionResultJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                eval.UpdatedVisionResult = aiVision;
            }
            catch { }
        }

        if (!ValidatePreLookupGuardrails(
                dbBet,
                dbUser,
                settings,
                aiVision,
                out var legDecimalOddsList,
                out bool allLegsHaveValidOdds,
                out string? legOddsError,
                out string? guardrailFailureReason))
        {
            eval.CanAutoApprove = false;
            eval.Reason = guardrailFailureReason!;
            return eval;
        }

        bool isLiveBet = dbBet.IsLive || (aiVision?.IsLive ?? false);
        var matchLookupCache = new Dictionary<string, (OddsPapiSearchResult? ApiResult, string? ApiError)>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < aiVision!.Legs.Count; i++)
        {
            var leg = aiVision.Legs[i];
            string teamToSearch = !string.IsNullOrWhiteSpace(leg.Match) ? leg.Match : (leg.Selection ?? "");
            string cacheKey = $"{leg.Sport ?? ""}||{teamToSearch}";

            if (!matchLookupCache.TryGetValue(cacheKey, out var lookup))
            {
                lookup = await _oddsApi.SearchOddsComparisonAsync(
                    teamToSearch,
                    dbBet.Id,
                    isLiveRequest: isLiveBet,
                    sport: leg.Sport,
                    cancellationToken: ct,
                    bypassOddsCache: isFinalCheck);
                matchLookupCache[cacheKey] = lookup;
            }

            var (apiResult, apiError) = lookup;
            var legEval = EvaluateLegOdds(leg, apiResult, apiError, _marketMapper);
            eval.LegResults[i] = legEval;

            if (apiResult != null && apiResult.StartTime > DateTime.MinValue)
            {
                if (leg.StartTime != apiResult.StartTime)
                {
                    leg.StartTime = apiResult.StartTime;
                    eval.MetadataUpdated = true;
                }
            }

            if (apiResult != null && (apiResult.IsLive || apiResult.StatusId == 1 || (apiResult.StatusId == 0 && apiResult.StartTime > DateTime.MinValue && apiResult.StartTime <= DateTime.UtcNow && apiResult.StartTime >= DateTime.UtcNow.AddHours(-4))))
            {
                eval.DetectedLiveFromOp = true;
                eval.MetadataUpdated = true;
            }
        }

        EvaluateTierWaterfall(
            dbBet,
            settings,
            aiVision,
            legDecimalOddsList,
            allLegsHaveValidOdds,
            legOddsError,
            eval);

        return eval;
    }

    public static bool ValidatePreLookupGuardrails(
        Bet dbBet,
        ApplicationUser? dbUser,
        SystemSetting settings,
        AiVisionExtractionResult? aiVision,
        out List<decimal> legDecimalOddsList,
        out bool allLegsHaveValidOdds,
        out string? legOddsError,
        out string? failureReason)
    {
        legDecimalOddsList = new List<decimal>();
        allLegsHaveValidOdds = true;
        legOddsError = null;
        failureReason = null;

        if (dbBet.Status != "Pending")
        {
            failureReason = $"❌ Bet status is '{dbBet.Status}'.";
            return false;
        }

        if (!settings.IsAutoApprovalEnabled)
        {
            failureReason = "❌ Global Auto-Approval is turned OFF in Admin Settings.";
            return false;
        }

        if (!settings.IsTier1PinnacleEnabled && !settings.IsTier2SoftBookiesEnabled && !settings.IsTier3SmallStakeEnabled)
        {
            failureReason = "❌ All Auto-Approval tiers (Tier 1, 2, 3) are disabled in Admin Settings.";
            return false;
        }

        if (dbUser == null)
        {
            failureReason = "❌ User not found.";
            return false;
        }

        if (!dbUser.AllowAutoApproval)
        {
            failureReason = "❌ Auto-Approval is disabled for this user.";
            return false;
        }

        if (aiVision == null || aiVision.Legs == null || aiVision.Legs.Count == 0)
        {
            failureReason = "❌ No legs extracted by AI Vision.";
            return false;
        }

        if (!dbBet.AmountNOK.HasValue || dbBet.AmountNOK.Value <= 0)
        {
            failureReason = "❌ Missing or invalid stake amount.";
            return false;
        }

        if (dbBet.Odds <= 1m || Math.Round(dbBet.Odds, 2) != dbBet.Odds)
        {
            failureReason = $"❌ Invalid total odds ({dbBet.Odds:0.00}).";
            return false;
        }

        bool isBetBuilder = dbBet.IsBetBuilder || aiVision.IsBetBuilder;
        if (isBetBuilder && (!settings.IsTier3SmallStakeEnabled || !settings.AutoApprovalAllowBetBuilders))
        {
            failureReason = "❌ Bet Builder is not eligible for Auto-Approval (disabled in Tier 3 settings).";
            return false;
        }

        bool isLive = dbBet.IsLive || aiVision.IsLive;
        if (isLive && !settings.AutoApprovalAllowLiveBets)
        {
            failureReason = "❌ Bet is marked as a Live Bet (disabled in settings).";
            return false;
        }

        decimal stake = (decimal)dbBet.AmountNOK.Value;
        if (stake < settings.MinBetAmount)
        {
            failureReason = $"❌ Stake ({stake:N0} kr) is below minimum bet ({settings.MinBetAmount:N0} kr).";
            return false;
        }

        decimal netLiability = Math.Floor(stake * (dbBet.Odds - 1m));
        if (netLiability > dbUser.MaxNetLiability)
        {
            failureReason = $"❌ Net Liability ({netLiability:N0} kr) exceeds user Max Liability ({dbUser.MaxNetLiability:N0} kr).";
            return false;
        }

        decimal availableCredit = dbUser.Balance + dbUser.FreeBetBalance + (decimal)dbUser.CreditLimit;
        if (availableCredit < stake)
        {
            failureReason = $"❌ Insufficient funds (Available: {availableCredit:N0} kr, Required: {stake:N0} kr).";
            return false;
        }

        for (int i = 0; i < aiVision.Legs.Count; i++)
        {
            if (!TryParseOddsDecimal(aiVision.Legs[i].Odds, out decimal legOdds) || legOdds <= 1.0m)
            {
                allLegsHaveValidOdds = false;
                legOddsError ??= $"Leg {i + 1} is missing valid individual odds on the screenshot.";
                legDecimalOddsList.Add(0m);
            }
            else
            {
                legDecimalOddsList.Add(legOdds);
            }
        }

        if (!isBetBuilder)
        {
            if (!allLegsHaveValidOdds)
            {
                failureReason = $"❌ {legOddsError}";
                return false;
            }

            if (!IsTotalOddsConsistentWithLegs(dbBet.Odds, legDecimalOddsList, out string? totalOddsMismatchReason))
            {
                failureReason = $"❌ {totalOddsMismatchReason}";
                return false;
            }
        }

        return true;
    }

    public static void EvaluateTierWaterfall(
        Bet dbBet,
        SystemSetting settings,
        AiVisionExtractionResult aiVision,
        IReadOnlyList<decimal> legDecimalOddsList,
        bool allLegsHaveValidOdds,
        string? legOddsError,
        AutoApprovalEvaluationResult eval)
    {
        // Universal post-lookup match status checks (Finished / Live)
        for (int i = 0; i < aiVision.Legs.Count; i++)
        {
            if (!eval.LegResults.TryGetValue(i, out var legEval) || legEval.SearchResult == null)
            {
                continue;
            }

            var apiResult = legEval.SearchResult;
            if (string.IsNullOrEmpty(apiResult.FixtureId))
            {
                continue;
            }

            if (apiResult.StatusId == 2)
            {
                eval.CanAutoApprove = false;
                eval.Reason = $"❌ Leg {i + 1} ('{apiResult.MatchName}') has already finished.";
                return;
            }

            if (!settings.AutoApprovalAllowLiveBets)
            {
                if (apiResult.IsLive || apiResult.StatusId == 1)
                {
                    eval.CanAutoApprove = false;
                    eval.Reason = $"❌ Leg {i + 1} ('{apiResult.MatchName}') is LIVE in OddsPapi.";
                    return;
                }

                if (apiResult.StatusId != 0 || (apiResult.StartTime > DateTime.MinValue && apiResult.StartTime <= DateTime.UtcNow))
                {
                    eval.CanAutoApprove = false;
                    eval.Reason = $"❌ Leg {i + 1} ('{apiResult.MatchName}') has already started.";
                    return;
                }
            }
        }

        decimal stake = (decimal)(dbBet.AmountNOK ?? 0);
        decimal netLiability = Math.Floor(stake * (dbBet.Odds - 1m));
        bool isBetBuilder = dbBet.IsBetBuilder || aiVision.IsBetBuilder;

        // Tier 1: Pinnacle
        string? tier1FailReason = null;
        if (settings.IsTier1PinnacleEnabled)
        {
            if (TryEvaluateTier1Pinnacle(stake, netLiability, isBetBuilder, allLegsHaveValidOdds, legOddsError, aiVision.Legs, legDecimalOddsList, eval.LegResults, settings, out tier1FailReason))
            {
                eval.CanAutoApprove = true;
                eval.MatchedTier = 1;
                eval.MatchedTierName = "Tier 1: Pinnacle";
                eval.Reason = "✅ All Auto-Approval criteria met (Tier 1: Pinnacle).";
                return;
            }
        }

        // Tier 2: Soft Bookies (Min. 2 Bookmakers Average)
        string? tier2FailReason = null;
        if (settings.IsTier2SoftBookiesEnabled)
        {
            if (TryEvaluateTier2SoftBookies(stake, netLiability, isBetBuilder, allLegsHaveValidOdds, legOddsError, aiVision.Legs, legDecimalOddsList, eval.LegResults, settings, out tier2FailReason))
            {
                eval.CanAutoApprove = true;
                eval.MatchedTier = 2;
                eval.MatchedTierName = "Tier 2: Soft Bookies";
                eval.Reason = "✅ All Auto-Approval criteria met (Tier 2: Soft Bookies).";
                return;
            }
        }

        // Tier 3: Small Stake (No Bookmaker Odds Required)
        string? tier3FailReason = null;
        if (settings.IsTier3SmallStakeEnabled)
        {
            if (TryEvaluateTier3SmallStake(stake, netLiability, isBetBuilder, settings, out tier3FailReason))
            {
                eval.CanAutoApprove = true;
                eval.MatchedTier = 3;
                eval.MatchedTierName = "Tier 3: Small Stake";
                eval.Reason = "✅ All Auto-Approval criteria met (Tier 3: Small Stake).";
                return;
            }
        }

        eval.CanAutoApprove = false;
        eval.Reason = FormatCombinedFailureReason(settings, tier1FailReason, tier2FailReason, tier3FailReason);
    }

    private static bool TryEvaluateTier1Pinnacle(
        decimal stake,
        decimal netLiability,
        bool isBetBuilder,
        bool allLegsHaveValidOdds,
        string? legOddsError,
        IReadOnlyList<AiVisionLeg> legs,
        IReadOnlyList<decimal> legDecimalOddsList,
        IReadOnlyDictionary<int, EvaluatedLegOdds> legResults,
        SystemSetting settings,
        out string? failReason)
    {
        failReason = null;

        if (stake > settings.AutoApprovalMaxStakeNOK)
        {
            failReason = $"Stake ({stake:N0} kr) > {settings.AutoApprovalMaxStakeNOK:N0} kr max";
            return false;
        }

        if (netLiability > settings.AutoApprovalMaxNetLiabilityNOK)
        {
            failReason = $"Liability ({netLiability:N0} kr) > {settings.AutoApprovalMaxNetLiabilityNOK:N0} kr max";
            return false;
        }

        if (isBetBuilder)
        {
            failReason = "Bet Builder requires Tier 3 (no Pinnacle SGP odds)";
            return false;
        }

        if (!allLegsHaveValidOdds)
        {
            failReason = legOddsError ?? "Missing individual leg odds";
            return false;
        }

        for (int i = 0; i < legs.Count; i++)
        {
            if (!legResults.TryGetValue(i, out var legEval))
            {
                failReason = $"Leg {i + 1}: Not evaluated";
                return false;
            }

            if (!TryValidateLegForTier1Pinnacle(i, legs[i], legDecimalOddsList[i], legEval, settings, out failReason))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryValidateLegForTier1Pinnacle(
        int legIndex,
        AiVisionLeg leg,
        decimal slipLegOdds,
        EvaluatedLegOdds legEval,
        SystemSetting settings,
        out string? failReason)
    {
        failReason = null;
        string teamToSearch = !string.IsNullOrWhiteSpace(leg.Match) ? leg.Match : (leg.Selection ?? "");

        if (legEval.SearchResult == null || string.IsNullOrEmpty(legEval.SearchResult.FixtureId))
        {
            string err = legEval.WarningMessage ?? legEval.ErrorMessage ?? "Match not found in OddsPapi";
            failReason = $"Leg {legIndex + 1} ('{teamToSearch}'): {err}";
            return false;
        }

        if (!string.IsNullOrEmpty(legEval.ErrorMessage) || !string.IsNullOrEmpty(legEval.WarningMessage) || string.IsNullOrEmpty(legEval.HighlightedOutcome))
        {
            string detail = legEval.WarningMessage ?? legEval.ErrorMessage ?? "Could not match market/outcome in OddsPapi";
            failReason = $"Leg {legIndex + 1} ({leg.Market} - {leg.Selection}): {detail}";
            return false;
        }

        if (!legEval.MatchedBookieOdds.TryGetValue("pinnacle+30", out var pinData) || pinData.Price <= 1.0)
        {
            failReason = $"Leg {legIndex + 1} ('{leg.Selection}'): Pinnacle odds not available";
            return false;
        }

        if (pinData.IsSuspended)
        {
            failReason = $"Leg {legIndex + 1} ('{leg.Selection}'): Pinnacle market is suspended";
            return false;
        }

        decimal pinLimit = pinData.Limit.HasValue ? Convert.ToDecimal(pinData.Limit.Value) : 0m;
        if (!pinData.Limit.HasValue || pinLimit < settings.AutoApprovalMinPinnacleLimitEUR)
        {
            failReason = $"Leg {legIndex + 1} ('{leg.Selection}'): Pinnacle limit €{pinLimit:0} < €{settings.AutoApprovalMinPinnacleLimitEUR:0} min";
            return false;
        }

        decimal pinOdds = Convert.ToDecimal(pinData.Price);
        if (!IsLegOddsWithinPinnacleTolerance(slipLegOdds, pinOdds, settings.AutoApprovalMaxOverPinnaclePercent, out decimal maxAllowedOdds, out decimal overPercent))
        {
            failReason = $"Leg {legIndex + 1} ('{leg.Selection}'): Odds {slipLegOdds:0.00} > Pinnacle {pinOdds:0.00} (+{overPercent:0.0}% > {settings.AutoApprovalMaxOverPinnaclePercent:0.##}% max, limit {maxAllowedOdds:0.000})";
            return false;
        }

        return true;
    }

    private static bool TryEvaluateTier2SoftBookies(
        decimal stake,
        decimal netLiability,
        bool isBetBuilder,
        bool allLegsHaveValidOdds,
        string? legOddsError,
        IReadOnlyList<AiVisionLeg> legs,
        IReadOnlyList<decimal> legDecimalOddsList,
        IReadOnlyDictionary<int, EvaluatedLegOdds> legResults,
        SystemSetting settings,
        out string? failReason)
    {
        failReason = null;

        if (stake > settings.Tier2MaxStakeNOK)
        {
            failReason = $"Stake ({stake:N0} kr) > {settings.Tier2MaxStakeNOK:N0} kr max";
            return false;
        }

        if (netLiability > settings.Tier2MaxNetLiabilityNOK)
        {
            failReason = $"Liability ({netLiability:N0} kr) > {settings.Tier2MaxNetLiabilityNOK:N0} kr max";
            return false;
        }

        if (isBetBuilder)
        {
            failReason = "Bet Builder requires Tier 3 (no bookmaker SGP odds)";
            return false;
        }

        if (!allLegsHaveValidOdds)
        {
            failReason = legOddsError ?? "Missing individual leg odds";
            return false;
        }

        for (int i = 0; i < legs.Count; i++)
        {
            var leg = legs[i];
            decimal slipLegOdds = legDecimalOddsList[i];
            string teamToSearch = !string.IsNullOrWhiteSpace(leg.Match) ? leg.Match : (leg.Selection ?? "");

            if (!legResults.TryGetValue(i, out var legEval))
            {
                failReason = $"Leg {i + 1}: Not evaluated";
                return false;
            }

            if (legEval.SearchResult == null || string.IsNullOrEmpty(legEval.SearchResult.FixtureId))
            {
                string err = legEval.WarningMessage ?? legEval.ErrorMessage ?? "Match not found in OddsPapi";
                failReason = $"Leg {i + 1} ('{teamToSearch}'): {err}";
                return false;
            }

            if (!string.IsNullOrEmpty(legEval.ErrorMessage) || !string.IsNullOrEmpty(legEval.WarningMessage) || string.IsNullOrEmpty(legEval.HighlightedOutcome))
            {
                string detail = legEval.WarningMessage ?? legEval.ErrorMessage ?? "Could not match market/outcome in OddsPapi";
                failReason = $"Leg {i + 1} ({leg.Market} - {leg.Selection}): {detail}";
                return false;
            }

            if (!TryCalculateTier2BookieAverage(legEval.MatchedBookieOdds, out decimal avgBookieOdds, out int validBookieCount))
            {
                // In a multi-leg combo, if this leg happens to pass Tier 1 (sharp Pinnacle with sufficient limit) even if <2 bookies are present, allow it
                if (settings.IsTier1PinnacleEnabled && TryValidateLegForTier1Pinnacle(i, leg, slipLegOdds, legEval, settings, out _))
                {
                    continue;
                }

                failReason = $"Leg {i + 1} ('{leg.Selection}'): Only {validBookieCount}/2 required bookmakers have valid odds";
                return false;
            }

            if (!IsLegOddsWithinPinnacleTolerance(slipLegOdds, avgBookieOdds, settings.Tier2MaxOverBookieAvgPercent, out decimal maxAllowedOdds, out decimal overPercent))
            {
                failReason = $"Leg {i + 1} ('{leg.Selection}'): Odds {slipLegOdds:0.00} > Avg {avgBookieOdds:0.00#} ({validBookieCount} bookies, +{overPercent:0.0}% > {settings.Tier2MaxOverBookieAvgPercent:0.##}% max, limit {maxAllowedOdds:0.000})";
                return false;
            }
        }

        return true;
    }

    private static bool TryEvaluateTier3SmallStake(
        decimal stake,
        decimal netLiability,
        bool isBetBuilder,
        SystemSetting settings,
        out string? failReason)
    {
        failReason = null;

        if (isBetBuilder && !settings.AutoApprovalAllowBetBuilders)
        {
            failReason = "Bet Builder is disabled in Tier 3";
            return false;
        }

        if (stake > settings.Tier3MaxStakeNOK)
        {
            failReason = $"Stake ({stake:N0} kr) > {settings.Tier3MaxStakeNOK:N0} kr max";
            return false;
        }

        if (netLiability > settings.Tier3MaxNetLiabilityNOK)
        {
            failReason = $"Liability ({netLiability:N0} kr) > {settings.Tier3MaxNetLiabilityNOK:N0} kr max";
            return false;
        }

        return true;
    }

    private static string FormatCombinedFailureReason(
        SystemSetting settings,
        string? tier1Fail,
        string? tier2Fail,
        string? tier3Fail)
    {
        var parts = new List<string>(3);

        if (settings.IsTier1PinnacleEnabled && settings.IsTier2SoftBookiesEnabled &&
            !string.IsNullOrEmpty(tier1Fail) && string.Equals(tier1Fail, tier2Fail, StringComparison.Ordinal))
        {
            parts.Add($"[Tier 1 & 2] {tier1Fail}");
        }
        else
        {
            if (settings.IsTier1PinnacleEnabled && !string.IsNullOrEmpty(tier1Fail))
            {
                parts.Add($"[Tier 1] {tier1Fail}");
            }
            if (settings.IsTier2SoftBookiesEnabled && !string.IsNullOrEmpty(tier2Fail))
            {
                parts.Add($"[Tier 2] {tier2Fail}");
            }
        }

        if (settings.IsTier3SmallStakeEnabled && !string.IsNullOrEmpty(tier3Fail))
        {
            parts.Add($"[Tier 3] {tier3Fail}");
        }

        if (parts.Count == 0)
        {
            return "❌ Did not meet any enabled Auto-Approval tier criteria.";
        }

        return $"❌ {string.Join(" | ", parts)}";
    }
}
