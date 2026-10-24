#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BettingApp.Data;
using BettingApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BettingApp.Services;

public class AutoApprovalService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly OddsApiService _oddsApi;
    private readonly MarketMappingService _marketMapper;
    private readonly UiNotificationService _uiNotifier;
    private readonly ILogger<AutoApprovalService> _logger;

    private static readonly SemaphoreSlim _wakeSignal = new(0, int.MaxValue);
    private static readonly ConcurrentDictionary<int, bool> _evaluatingBetIds = new();

    public AutoApprovalService(
        IServiceScopeFactory scopeFactory,
        IDbContextFactory<ApplicationDbContext> dbFactory,
        OddsApiService oddsApi,
        MarketMappingService marketMapper,
        UiNotificationService uiNotifier,
        ILogger<AutoApprovalService> logger)
    {
        _scopeFactory = scopeFactory;
        _dbFactory = dbFactory;
        _oddsApi = oddsApi;
        _marketMapper = marketMapper;
        _uiNotifier = uiNotifier;
        _logger = logger;
    }

    public static void WakeUp()
    {
        try
        {
            if (_wakeSignal.CurrentCount == 0)
            {
                _wakeSignal.Release();
            }
        }
        catch { }
    }

    /// <summary>
    /// Fire-and-forget trigger called immediately after a bet lands in Pending.
    /// Runs completely in the background so there is 0ms delay to the Pending queue.
    /// </summary>
    public void EnqueuePendingBet(int betId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await EvaluateInitialPendingBetAsync(betId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[Bet #{betId}] [Auto-Approve] Error in background initial evaluation.");
            }
        });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan nextWait = TimeSpan.FromSeconds(5);
            try
            {
                nextWait = await ProcessDueAutoApprovalsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Auto-Approve] Error in background loop.");
            }

            try
            {
                await _wakeSignal.WaitAsync(nextWait, stoppingToken);
                // Drain extra signals
                while (_wakeSignal.CurrentCount > 0)
                {
                    await _wakeSignal.WaitAsync(0, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch { }
        }
    }

    private async Task<TimeSpan> ProcessDueAutoApprovalsAsync(CancellationToken ct)
    {
        using var context = _dbFactory.CreateDbContext();
        var now = DateTime.UtcNow;

        var pendingWithTimers = await context.Bets
            .AsNoTracking()
            .Where(b => b.Status == "Pending" && b.AutoApproveAt != null)
            .Select(b => new { b.Id, b.AutoApproveAt })
            .ToListAsync(ct);

        if (pendingWithTimers.Count == 0)
        {
            return TimeSpan.FromSeconds(5);
        }

        var dueBetIds = pendingWithTimers
            .Where(b => b.AutoApproveAt!.Value <= now)
            .Select(b => b.Id)
            .ToList();

        foreach (var betId in dueBetIds)
        {
            if (ct.IsCancellationRequested) break;
            await ExecuteFinalAutoApprovalAsync(betId, ct);
        }

        var remaining = pendingWithTimers
            .Where(b => b.AutoApproveAt!.Value > DateTime.UtcNow)
            .Select(b => b.AutoApproveAt!.Value - DateTime.UtcNow)
            .ToList();

        if (remaining.Count == 0)
        {
            return TimeSpan.FromSeconds(2);
        }

        var minRemaining = remaining.Min();
        if (minRemaining < TimeSpan.FromMilliseconds(250))
            return TimeSpan.FromMilliseconds(250);
        if (minRemaining > TimeSpan.FromSeconds(5))
            return TimeSpan.FromSeconds(5);
        return minRemaining;
    }

    public async Task EvaluateInitialPendingBetAsync(int betId, CancellationToken ct = default)
    {
        if (!_evaluatingBetIds.TryAdd(betId, true))
        {
            return;
        }

        try
        {
            using var context = _dbFactory.CreateDbContext();
            var dbBet = await context.Bets
                .Include(b => b.AiEvaluation)
                .Include(b => b.Legs)
                .ThenInclude(l => l.BookmakerOdds)
                .FirstOrDefaultAsync(b => b.Id == betId, ct);

            if (dbBet == null || dbBet.Status != "Pending")
            {
                return;
            }

            var dbUser = await context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == dbBet.UserId, ct);
            var settings = await context.Settings.AsNoTracking().OrderBy(s => s.Id).FirstOrDefaultAsync(ct) ?? new SystemSetting();

            var checkResult = await CheckIfBetCanBeAutoApprovedAsync(dbBet, dbUser, settings, isFinalCheck: false, ct);

            // Re-fetch fresh tracked entity in case an admin modified/approved the bet while OddsPapi was being queried
            using var saveContext = _dbFactory.CreateDbContext();
            var freshBet = await saveContext.Bets
                .Include(b => b.AiEvaluation)
                .Include(b => b.Legs)
                .ThenInclude(l => l.BookmakerOdds)
                .FirstOrDefaultAsync(b => b.Id == betId, ct);

            if (freshBet == null || freshBet.Status != "Pending")
            {
                return;
            }

            if (freshBet.AiEvaluation == null)
            {
                var existingEval = await saveContext.BetAiEvaluations.FirstOrDefaultAsync(a => a.BetId == freshBet.Id, ct);
                freshBet.AiEvaluation = existingEval ?? new BetAiEvaluation { BetId = freshBet.Id };
            }

            // Persist StartTime, FixtureId, and IsLive (if OP found a live match) from the lookup
            ApplyLookupMetadataToBet(freshBet, checkResult, saveBookmakerOdds: false);

            if (!checkResult.CanAutoApprove)
            {
                freshBet.AutoApproveAt = null;
                freshBet.AutoApproveReason = checkResult.Reason;
                await saveContext.SaveChangesAsync(ct);

                _logger.LogInformation($"[Bet #{betId}] [Auto-Approve] Not eligible: {checkResult.Reason}");
                if (checkResult.MetadataUpdated)
                {
                    _uiNotifier.NotifyAdmin("PendingUpdate");
                }
                return;
            }

            int delaySeconds = Math.Max(0, settings.AutoApprovalDelaySeconds);
            if (delaySeconds == 0)
            {
                // Immediate auto-approval when delay is configured to 0s
                ApplyLookupMetadataToBet(freshBet, checkResult, saveBookmakerOdds: true);
                await saveContext.SaveChangesAsync(ct);

                using var scope = _scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<BetProcessingService>();
                var procRes = await processor.ProcessBetAsync(
                    betId: freshBet.Id,
                    newStatus: "Approved",
                    adminName: "SYSTEM_AUTO",
                    expectedOriginalStatus: "Pending",
                    newAmount: (decimal)(freshBet.AmountNOK ?? 0),
                    newOdds: freshBet.Odds,
                    isAutoApproved: true);

                if (procRes.Success)
                {
                    _logger.LogInformation($"[Bet #{betId}] [Auto-Approve] ✅ Immediately auto-approved (0s timer).");
                }
                else
                {
                    _logger.LogWarning($"[Bet #{betId}] [Auto-Approve] Immediate approval failed: {procRes.ErrorMessage}");
                }
                return;
            }

            freshBet.AutoApproveAt = DateTime.UtcNow.AddSeconds(delaySeconds);
            freshBet.AutoApproveReason = $"⏳ Auto-Approve timer running ({delaySeconds}s)";
            await saveContext.SaveChangesAsync(ct);

            _logger.LogInformation($"[Bet #{betId}] [Auto-Approve] Eligible! Started {delaySeconds}s countdown (until {freshBet.AutoApproveAt:HH:mm:ss} UTC).");
            _uiNotifier.NotifyAdmin("PendingUpdate");
            WakeUp();
        }
        finally
        {
            _evaluatingBetIds.TryRemove(betId, out _);
        }
    }

    private async Task ExecuteFinalAutoApprovalAsync(int betId, CancellationToken ct)
    {
        if (!_evaluatingBetIds.TryAdd(betId, true))
        {
            return;
        }

        try
        {
            using var context = _dbFactory.CreateDbContext();
            var dbBet = await context.Bets
                .Include(b => b.AiEvaluation)
                .Include(b => b.Legs)
                .ThenInclude(l => l.BookmakerOdds)
                .FirstOrDefaultAsync(b => b.Id == betId, ct);

            if (dbBet == null || dbBet.Status != "Pending" || dbBet.AutoApproveAt == null)
            {
                return;
            }

            var dbUser = await context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == dbBet.UserId, ct);
            var settings = await context.Settings.AsNoTracking().OrderBy(s => s.Id).FirstOrDefaultAsync(ct) ?? new SystemSetting();

            var checkResult = await CheckIfBetCanBeAutoApprovedAsync(dbBet, dbUser, settings, isFinalCheck: true, ct);

            using var saveContext = _dbFactory.CreateDbContext();
            var freshBet = await saveContext.Bets
                .Include(b => b.AiEvaluation)
                .Include(b => b.Legs)
                .ThenInclude(l => l.BookmakerOdds)
                .FirstOrDefaultAsync(b => b.Id == betId, ct);

            if (freshBet == null || freshBet.Status != "Pending" || freshBet.AutoApproveAt == null)
            {
                return;
            }

            if (freshBet.AiEvaluation == null)
            {
                var existingEval = await saveContext.BetAiEvaluations.FirstOrDefaultAsync(a => a.BetId == freshBet.Id, ct);
                freshBet.AiEvaluation = existingEval ?? new BetAiEvaluation { BetId = freshBet.Id };
            }

            if (!checkResult.CanAutoApprove)
            {
                ApplyLookupMetadataToBet(freshBet, checkResult, saveBookmakerOdds: false);
                freshBet.AutoApproveAt = null;
                string cleanReason = checkResult.Reason.TrimStart('❌', ' ');
                freshBet.AutoApproveReason = $"❌ Cancelled at 0s: {cleanReason}";
                await saveContext.SaveChangesAsync(ct);

                _logger.LogInformation($"[Bet #{betId}] [Auto-Approve] Cancelled at 0s re-check: {cleanReason}");
                _uiNotifier.NotifyAdmin("PendingUpdate");
                return;
            }

            // Save the fresh BookmakerOdds snapshot from the moment of auto-approval
            ApplyLookupMetadataToBet(freshBet, checkResult, saveBookmakerOdds: true);
            await saveContext.SaveChangesAsync(ct);

            using var scope = _scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<BetProcessingService>();
            var procRes = await processor.ProcessBetAsync(
                betId: freshBet.Id,
                newStatus: "Approved",
                adminName: "SYSTEM_AUTO",
                expectedOriginalStatus: "Pending",
                newAmount: (decimal)(freshBet.AmountNOK ?? 0),
                newOdds: freshBet.Odds,
                isAutoApproved: true);

            if (procRes.Success)
            {
                _logger.LogInformation($"[Bet #{betId}] [Auto-Approve] ✅ Auto-approved Bet #{betId} ({freshBet.AmountNOK:N0} kr @ {freshBet.Odds:0.00}).");
                if (!freshBet.MatchStartTime.HasValue || (freshBet.Legs != null && freshBet.Legs.Any(l => !l.StartTime.HasValue)) || freshBet.MatchStartTime.Value <= DateTime.UtcNow)
                {
                    BetMonitoringService.WakeUp();
                }
            }
            else
            {
                using var failCtx = _dbFactory.CreateDbContext();
                var failBet = await failCtx.Bets.FirstOrDefaultAsync(b => b.Id == betId, ct);
                if (failBet != null && failBet.Status == "Pending")
                {
                    failBet.AutoApproveAt = null;
                    failBet.AutoApproveReason = $"❌ Cancelled at 0s: {procRes.ErrorMessage}";
                    await failCtx.SaveChangesAsync(ct);
                    _uiNotifier.NotifyAdmin("PendingUpdate");
                }
                _logger.LogWarning($"[Bet #{betId}] [Auto-Approve] ProcessBetAsync failed at 0s: {procRes.ErrorMessage}");
            }
        }
        finally
        {
            _evaluatingBetIds.TryRemove(betId, out _);
        }
    }

    public async Task CancelAutoApprovalForBetAsync(int betId, string adminName)
    {
        using var context = _dbFactory.CreateDbContext();
        var dbBet = await context.Bets.FirstOrDefaultAsync(b => b.Id == betId);
        if (dbBet != null && dbBet.AutoApproveAt != null)
        {
            dbBet.AutoApproveAt = null;
            dbBet.AutoApproveReason = $"⏹️ Stopped manually by {adminName}";
            await context.SaveChangesAsync();
            _logger.LogInformation($"[Bet #{betId}] [Auto-Approve] Timer stopped manually by {adminName}.");
            _uiNotifier.NotifyAdmin("PendingUpdate");
        }
    }

    public async Task CancelAllActiveCountdownsAsync(string reason)
    {
        using var context = _dbFactory.CreateDbContext();
        var activeBets = await context.Bets
            .Where(b => b.Status == "Pending" && b.AutoApproveAt != null)
            .ToListAsync();

        if (activeBets.Count == 0) return;

        foreach (var bet in activeBets)
        {
            bet.AutoApproveAt = null;
            bet.AutoApproveReason = $"⏹️ {reason}";
        }

        await context.SaveChangesAsync();
        _uiNotifier.NotifyAdmin("PendingUpdate");
    }

    public class AutoApprovalEvaluationResult
    {
        public bool CanAutoApprove { get; set; }
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

    public async Task<AutoApprovalEvaluationResult> CheckIfBetCanBeAutoApprovedAsync(
        Bet dbBet,
        ApplicationUser? dbUser,
        SystemSetting settings,
        bool isFinalCheck,
        CancellationToken ct = default)
    {
        var eval = new AutoApprovalEvaluationResult();

        if (dbBet.Status != "Pending")
        {
            eval.Reason = $"❌ Bet status is '{dbBet.Status}'.";
            return eval;
        }

        // Parse AI Vision JSON first so we can still look up start times if needed
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

        // 1. Master Toggle & User Toggle
        if (!settings.IsAutoApprovalEnabled)
        {
            eval.Reason = "❌ Global Auto-Approval is turned OFF in Admin Settings.";
            return eval;
        }

        if (dbUser == null)
        {
            eval.Reason = "❌ User not found.";
            return eval;
        }

        if (!dbUser.AllowAutoApproval)
        {
            eval.Reason = "❌ Auto-Approval is disabled for this user.";
            return eval;
        }

        // 2. Basic Bet & AI Extraction Presence
        if (aiVision == null || aiVision.Legs == null || aiVision.Legs.Count == 0)
        {
            eval.Reason = "❌ No legs extracted by AI Vision.";
            return eval;
        }

        if (!dbBet.AmountNOK.HasValue || dbBet.AmountNOK.Value <= 0)
        {
            eval.Reason = "❌ Missing or invalid stake amount.";
            return eval;
        }

        if (dbBet.Odds <= 1m || Math.Round(dbBet.Odds, 2) != dbBet.Odds)
        {
            eval.Reason = $"❌ Invalid total odds ({dbBet.Odds:0.00}).";
            return eval;
        }

        // 3. Bet Type Checks: Bet Builder, AI Live flag
        if (dbBet.IsBetBuilder || aiVision.IsBetBuilder)
        {
            eval.Reason = "❌ Bet Builder is not eligible for Auto-Approval.";
            return eval;
        }

        if (dbBet.IsLive || aiVision.IsLive)
        {
            eval.Reason = "❌ Bet is marked as a Live Bet.";
            return eval;
        }

        // 4. Size, Credit & Liability Guardrails
        decimal stake = (decimal)dbBet.AmountNOK.Value;
        if (stake < settings.MinBetAmount)
        {
            eval.Reason = $"❌ Stake ({stake:N0} kr) is below minimum bet ({settings.MinBetAmount:N0} kr).";
            return eval;
        }

        if (stake > settings.AutoApprovalMaxStakeNOK)
        {
            eval.Reason = $"❌ Stake ({stake:N0} kr) exceeds Auto-Approve max stake ({settings.AutoApprovalMaxStakeNOK:N0} kr).";
            return eval;
        }

        decimal netLiability = Math.Floor(stake * (dbBet.Odds - 1m));
        if (netLiability > settings.AutoApprovalMaxNetLiabilityNOK)
        {
            eval.Reason = $"❌ Net Liability ({netLiability:N0} kr) exceeds Auto-Approve max liability ({settings.AutoApprovalMaxNetLiabilityNOK:N0} kr).";
            return eval;
        }

        if (netLiability > dbUser.MaxNetLiability)
        {
            eval.Reason = $"❌ Net Liability ({netLiability:N0} kr) exceeds user Max Liability ({dbUser.MaxNetLiability:N0} kr).";
            return eval;
        }

        decimal availableCredit = dbUser.Balance + dbUser.FreeBetBalance + (decimal)dbUser.CreditLimit;
        if (availableCredit < stake)
        {
            eval.Reason = $"❌ Insufficient funds (Available: {availableCredit:N0} kr, Required: {stake:N0} kr).";
            return eval;
        }

        // 5. Individual Leg Odds Presence & Total Odds Consistency (with unrounded leg wiggle room)
        var legDecimalOddsList = new List<decimal>(aiVision.Legs.Count);
        for (int i = 0; i < aiVision.Legs.Count; i++)
        {
            if (!TryParseOddsDecimal(aiVision.Legs[i].Odds, out decimal legOdds) || legOdds <= 1.0m)
            {
                eval.Reason = $"❌ Leg {i + 1} is missing valid individual odds on the screenshot.";
                return eval;
            }
            legDecimalOddsList.Add(legOdds);
        }

        if (!IsTotalOddsConsistentWithLegs(dbBet.Odds, legDecimalOddsList, out string? totalOddsMismatchReason))
        {
            eval.Reason = $"❌ {totalOddsMismatchReason}";
            return eval;
        }

        // 6. Query OddsPapi for Every Leg (Pre-match check + Market/Outcome match + Pinnacle Price & € Limit)
        string? firstFailureReason = null;

        for (int i = 0; i < aiVision.Legs.Count; i++)
        {
            var leg = aiVision.Legs[i];
            decimal slipLegOdds = legDecimalOddsList[i];
            string teamToSearch = !string.IsNullOrWhiteSpace(leg.Match) ? leg.Match : (leg.Selection ?? "");

            var (apiResult, apiError) = await _oddsApi.SearchOddsComparisonAsync(
                teamToSearch,
                dbBet.Id,
                isLiveRequest: false,
                sport: leg.Sport,
                cancellationToken: ct,
                bypassOddsCache: isFinalCheck);

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

            if (firstFailureReason != null)
            {
                continue;
            }

            if (apiResult == null)
            {
                firstFailureReason = $"❌ Leg {i + 1} ('{teamToSearch}'): {apiError ?? "Match not found in OddsPapi"}.";
                continue;
            }

            if (apiResult.IsLive || apiResult.StatusId == 1)
            {
                firstFailureReason = $"❌ Leg {i + 1} ('{apiResult.MatchName}') is LIVE in OddsPapi.";
                continue;
            }

            if (apiResult.StatusId == 2)
            {
                firstFailureReason = $"❌ Leg {i + 1} ('{apiResult.MatchName}') has already finished.";
                continue;
            }

            if (apiResult.StatusId != 0 || apiResult.StartTime <= DateTime.UtcNow)
            {
                firstFailureReason = $"❌ Leg {i + 1} ('{apiResult.MatchName}') has already started.";
                continue;
            }

            if (!string.IsNullOrEmpty(legEval.ErrorMessage) || !string.IsNullOrEmpty(legEval.WarningMessage) || string.IsNullOrEmpty(legEval.HighlightedOutcome))
            {
                string detail = legEval.WarningMessage ?? legEval.ErrorMessage ?? "Could not match market/outcome in OddsPapi";
                firstFailureReason = $"❌ Leg {i + 1} ({leg.Market} - {leg.Selection}): {detail}";
                continue;
            }

            if (!legEval.MatchedBookieOdds.TryGetValue("pinnacle+30", out var pinData) || pinData.Price <= 1.0)
            {
                firstFailureReason = $"❌ Leg {i + 1} ('{leg.Selection}'): Pinnacle odds not available.";
                continue;
            }

            if (pinData.IsSuspended)
            {
                firstFailureReason = $"❌ Leg {i + 1} ('{leg.Selection}'): Pinnacle market is suspended.";
                continue;
            }

            decimal pinLimit = pinData.Limit.HasValue ? Convert.ToDecimal(pinData.Limit.Value) : 0m;
            if (!pinData.Limit.HasValue || pinLimit < settings.AutoApprovalMinPinnacleLimitEUR)
            {
                firstFailureReason = $"❌ Leg {i + 1} ('{leg.Selection}'): Pinnacle limit €{pinLimit:0} < €{settings.AutoApprovalMinPinnacleLimitEUR:0} min.";
                continue;
            }

            decimal pinOdds = Convert.ToDecimal(pinData.Price);
            if (!IsLegOddsWithinPinnacleTolerance(slipLegOdds, pinOdds, settings.AutoApprovalMaxOverPinnaclePercent, out decimal maxAllowedOdds, out decimal overPercent))
            {
                firstFailureReason = $"❌ Leg {i + 1} ('{leg.Selection}'): Odds {slipLegOdds:0.00} > Pinnacle {pinOdds:0.00} (+{overPercent:0.1}% > {settings.AutoApprovalMaxOverPinnaclePercent:0.##}% max, limit {maxAllowedOdds:0.000}).";
                continue;
            }
        }

        if (firstFailureReason != null)
        {
            eval.CanAutoApprove = false;
            eval.Reason = firstFailureReason;
            return eval;
        }

        eval.CanAutoApprove = true;
        eval.Reason = "✅ All Auto-Approval criteria met.";
        return eval;
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
        decimal pinnacleOdds,
        decimal maxOverPinnaclePercent,
        out decimal maxAllowedOdds,
        out decimal actualOverPercent)
    {
        maxAllowedOdds = pinnacleOdds * (1m + (maxOverPinnaclePercent / 100m));
        actualOverPercent = pinnacleOdds > 0m ? ((slipLegOdds - pinnacleOdds) / pinnacleOdds) * 100m : 0m;
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
                    var targets = new[] { "pinnacle+30", "unibet.se", "coolbet", "bet365", "betsson" };
                    foreach (var tb in targets)
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

    private static void ApplyLookupMetadataToBet(Bet dbBet, AutoApprovalEvaluationResult checkResult, bool saveBookmakerOdds)
    {
        if (checkResult.DetectedLiveFromOp && !dbBet.IsLive)
        {
            dbBet.IsLive = true;
            checkResult.MetadataUpdated = true;
        }

        var aiVision = checkResult.UpdatedVisionResult;
        if (aiVision?.Legs != null && aiVision.Legs.Count > 0)
        {
            bool allScheduled = aiVision.Legs.All(l => l.StartTime > DateTime.MinValue);
            if (allScheduled)
            {
                var earliest = aiVision.Legs.Min(l => l.StartTime);
                if (dbBet.MatchStartTime != earliest)
                {
                    dbBet.MatchStartTime = earliest;
                    checkResult.MetadataUpdated = true;
                }
            }

            if (checkResult.MetadataUpdated)
            {
                dbBet.AiVisionResultJson = JsonSerializer.Serialize(aiVision);
            }

            if (dbBet.Legs != null && dbBet.Legs.Count == aiVision.Legs.Count)
            {
                var orderedDbLegs = dbBet.Legs.OrderBy(l => l.Id).ToList();
                for (int i = 0; i < orderedDbLegs.Count; i++)
                {
                    if (aiVision.Legs[i].StartTime > DateTime.MinValue)
                    {
                        orderedDbLegs[i].StartTime = aiVision.Legs[i].StartTime;
                    }

                    if (checkResult.LegResults.TryGetValue(i, out var legResult))
                    {
                        if (!string.IsNullOrEmpty(legResult.SearchResult?.FixtureId))
                        {
                            orderedDbLegs[i].FixtureId = legResult.SearchResult.FixtureId;
                        }

                        if (saveBookmakerOdds && legResult.MatchedBookieOdds.Count > 0)
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
    }
}
