#nullable enable
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BettingApp.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BettingApp.Services;

public partial class AutoApprovalService : BackgroundService
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

            string tierBadge = !string.IsNullOrWhiteSpace(checkResult.MatchedTierName)
                ? $" ({checkResult.MatchedTierName})"
                : "";

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
                    isAutoApproved: true,
                    autoApproveReason: $"✅ Approved automatically{tierBadge}");

                if (procRes.Success)
                {
                    _logger.LogInformation($"[Bet #{betId}] [Auto-Approve] ✅ Immediately auto-approved{tierBadge} (0s timer).");
                }
                else
                {
                    _logger.LogWarning($"[Bet #{betId}] [Auto-Approve] Immediate approval failed: {procRes.ErrorMessage}");
                }
                return;
            }

            freshBet.AutoApproveAt = DateTime.UtcNow.AddSeconds(delaySeconds);
            freshBet.AutoApproveReason = $"⏳ Auto-Approve{tierBadge} timer running ({delaySeconds}s)";
            await saveContext.SaveChangesAsync(ct);

            _logger.LogInformation($"[Bet #{betId}] [Auto-Approve] Eligible{tierBadge}! Started {delaySeconds}s countdown (until {freshBet.AutoApproveAt:HH:mm:ss} UTC).");
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

            string tierBadge = !string.IsNullOrWhiteSpace(checkResult.MatchedTierName)
                ? $" ({checkResult.MatchedTierName})"
                : "";

            using var scope = _scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<BetProcessingService>();
            var procRes = await processor.ProcessBetAsync(
                betId: freshBet.Id,
                newStatus: "Approved",
                adminName: "SYSTEM_AUTO",
                expectedOriginalStatus: "Pending",
                newAmount: (decimal)(freshBet.AmountNOK ?? 0),
                newOdds: freshBet.Odds,
                isAutoApproved: true,
                autoApproveReason: $"✅ Approved automatically{tierBadge}");

            if (procRes.Success)
            {
                _logger.LogInformation($"[Bet #{betId}] [Auto-Approve] ✅ Auto-approved Bet #{betId}{tierBadge} ({freshBet.AmountNOK:N0} kr @ {freshBet.Odds:0.00}).");
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

    public async Task<string?> CancelAutoApprovalForBetAsync(int betId, string adminName)
    {
        using var context = _dbFactory.CreateDbContext();
        var dbBet = await context.Bets.FirstOrDefaultAsync(b => b.Id == betId);
        if (dbBet != null && dbBet.AutoApproveAt != null)
        {
            string tierSuffix = "";
            if (!string.IsNullOrWhiteSpace(dbBet.AutoApproveReason))
            {
                var tierMatch = System.Text.RegularExpressions.Regex.Match(dbBet.AutoApproveReason, @"\(Tier \d+:[^)]+\)");
                if (tierMatch.Success)
                {
                    tierSuffix = $" {tierMatch.Value}";
                }
            }

            dbBet.AutoApproveAt = null;
            dbBet.AutoApproveReason = $"⏹️ Stopped manually by {adminName}{tierSuffix}";
            await context.SaveChangesAsync();
            _logger.LogInformation($"[Bet #{betId}] [Auto-Approve] Timer stopped manually by {adminName}.");
            _uiNotifier.NotifyAdmin("PendingUpdate");
            return dbBet.AutoApproveReason;
        }
        return dbBet?.AutoApproveReason;
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
