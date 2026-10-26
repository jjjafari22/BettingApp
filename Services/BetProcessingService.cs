using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using BettingApp.Data;
using Microsoft.Extensions.Logging;

namespace BettingApp.Services
{
    public class BetProcessingService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
        private readonly UiNotificationService _uiNotificationService;
        private readonly DiscordNotificationService _discordService;
        private readonly ILogger<BetProcessingService> _logger;

        public BetProcessingService(
            IDbContextFactory<ApplicationDbContext> dbFactory,
            UiNotificationService uiNotificationService,
            DiscordNotificationService discordService,
            ILogger<BetProcessingService> logger)
        {
            _dbFactory = dbFactory;
            _uiNotificationService = uiNotificationService;
            _discordService = discordService;
            _logger = logger;
        }

        public async Task<(bool Success, string ErrorMessage, bool IsValidationError)> ProcessBetAsync(
            int betId,
            string newStatus,
            string adminName,
            string expectedOriginalStatus,
            decimal newAmount,
            decimal newOdds,
            string? customAuditDetails = null,
            bool isAutoSettled = false,
            bool isAutoApproved = false,
            string? autoApproveReason = null)
        {
            const int maxConcurrencyAttempts = 3;

            for (int attempt = 1; attempt <= maxConcurrencyAttempts; attempt++)
            {
                using var context = _dbFactory.CreateDbContext();
                
                var dbBet = await context.Bets.Include(b => b.Legs).Include(b => b.AiEvaluation).FirstOrDefaultAsync(b => b.Id == betId);
                if (dbBet == null) return (false, "Bet not found.", false);
                
                var dbUser = await context.Users.FindAsync(dbBet.UserId);
                if (dbUser == null) return (false, "User not found.", false);

                bool isProcessingToPendingTransition =
                    (expectedOriginalStatus == "Processing" && dbBet.Status == "Pending") ||
                    (expectedOriginalStatus == "Pending" && dbBet.Status == "Processing");

                if (dbBet.Status != expectedOriginalStatus && !isProcessingToPendingTransition)
                {
                    var lastAudit = await context.AuditLogs
                        .Where(a => a.Details.Contains($"Bet ID: {betId}"))
                        .OrderByDescending(a => a.Timestamp)
                        .FirstOrDefaultAsync();
                    
                    string otherAdminName = lastAudit != null ? lastAudit.AdminUserName : "another admin";
                    return (false, $"Action aborted: Bet #{betId} was already updated to '{dbBet.Status}' by Admin {otherAdminName}.", false);
                }

                string originalStatus = dbBet.Status;
                bool isUndo = (originalStatus == "Won" || originalStatus == "Lost" || originalStatus == "Void" || originalStatus == "Rejected" || originalStatus == "Cancelled") && newStatus == "Approved";
                bool wasStaked = originalStatus == "Approved" || originalStatus == "Won" || originalStatus == "Lost";
                bool willBeStaked = newStatus == "Approved" || newStatus == "Won" || newStatus == "Lost";
                bool isFirstApproval = originalStatus == "Pending" || originalStatus == "Processing" || originalStatus == "Rejected" || originalStatus == "Cancelled";

                decimal dbAmount = (decimal)(dbBet.AmountNOK ?? 0);

                if (newStatus != "Rejected" && newStatus != "Cancelled")
                {
                    if (newAmount <= 0) return (false, "Invalid amount", true);
                    if (newOdds <= 1m) return (false, "Invalid odds", true);
                    if (Math.Round(newOdds, 2) != newOdds) return (false, "Odds max 2 decimals", true);

                    bool isNewOrModified = (!wasStaked && willBeStaked) || (newAmount != dbAmount || newOdds != dbBet.Odds);
                    if (isNewOrModified)
                    {
                        var settings = await context.Settings.AsNoTracking().OrderBy(s => s.Id).FirstOrDefaultAsync();
                        if (settings != null && newAmount < settings.MinBetAmount)
                        {
                            return (false, $"Amount below limit ({settings.MinBetAmount:N0})", true);
                        }

                        decimal netLiability = Math.Floor(newAmount * (newOdds - 1m));
                        decimal userLimit = dbUser.MaxNetLiability;
                        if (netLiability > userLimit)
                        {
                            decimal maxBetAmount = Math.Floor(userLimit / (newOdds - 1m));
                            return (false, $"Net liability exceeds limit ({userLimit:N0} kr). Max bet for {newOdds} odds is {maxBetAmount:N0} kr.", true);
                        }
                    }
                }

                if (!wasStaked && willBeStaked)
                {
                    var currentBal = dbUser.Balance;
                    var freeBetBal = isFirstApproval ? dbUser.FreeBetBalance : Math.Min(dbBet.FreeBetAmount, dbUser.FreeBetBalance);
                    var creditLimit = (decimal)dbUser.CreditLimit;
                    
                    var availableCredit = currentBal + freeBetBal + creditLimit - newAmount;
                    if (availableCredit < 0)
                    {
                        return (false, $"Insufficient funds. Available: {(currentBal + freeBetBal + creditLimit):N0} kr. Required: {newAmount:N0} kr.", true);
                    }
                }

                if (wasStaked && willBeStaked && newAmount > dbAmount)
                {
                    var currentBal = dbUser.Balance;
                    var creditLimit = (decimal)dbUser.CreditLimit;
                    var additionalAmount = newAmount - dbAmount;
                    
                    var availableCredit = currentBal + creditLimit - additionalAmount;
                    if (availableCredit < 0)
                    {
                        return (false, $"Insufficient funds for stake increase. Available: {(currentBal + creditLimit):N0} kr. Additional Required: {additionalAmount:N0} kr.", true);
                    }
                }

                // REVERSE OLD IMPACT
                // Statuses that have deducted balance: Approved, Won, Lost
                if (originalStatus == "Approved" || originalStatus == "Lost")
                {
                    dbUser.Balance += (dbAmount - dbBet.FreeBetAmount);
                    dbUser.FreeBetBalance += dbBet.FreeBetAmount;
                    if (originalStatus == "Lost")
                    {
                        dbUser.LifetimeProfit += (dbAmount - dbBet.FreeBetAmount);
                    }
                }
                else if (originalStatus == "Won")
                {
                    dbUser.Balance += (dbAmount - dbBet.FreeBetAmount);
                    dbUser.FreeBetBalance += dbBet.FreeBetAmount;
                    dbUser.Balance -= dbBet.PotentialPayout;
                    
                    // Refund the old net profit
                    dbUser.LifetimeProfit -= (dbBet.PotentialPayout - (dbAmount - dbBet.FreeBetAmount));
                }
                // originalStatus == Void/Rejected/Cancelled/Pending already has balance returned

                bool hadActiveAutoApproveTimer = dbBet.AutoApproveAt != null ||
                    (!string.IsNullOrWhiteSpace(dbBet.AutoApproveReason) && dbBet.AutoApproveReason.StartsWith("⏳"));

                // APPLY NEW VALUES
                dbBet.Status = newStatus;
                dbBet.AmountNOK = (int?)newAmount;
                dbBet.Odds = newOdds;
                dbBet.AutoApproveAt = null;

                if (isAutoSettled)
                {
                    dbBet.IsAutoSettled = true;
                }

                if (isAutoApproved)
                {
                    dbBet.IsAutoApproved = true;
                    dbBet.AutoApproveReason = !string.IsNullOrWhiteSpace(autoApproveReason)
                        ? autoApproveReason
                        : "✅ Approved automatically";
                }
                else if (hadActiveAutoApproveTimer)
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
                    dbBet.AutoApproveReason = $"⏹️ {newStatus} manually by {adminName}{tierSuffix}";
                }

                if (isFirstApproval)
                {
                    // Automatic consumption if user has free bet balance
                    dbBet.FreeBetAmount = Math.Min(newAmount, dbUser.FreeBetBalance);
                }
                else 
                {
                     // Safety: Capping free bet amount if admin reduced the total stake or if refunded free bet was already spent
                     dbBet.FreeBetAmount = Math.Min(Math.Min(dbBet.FreeBetAmount, newAmount), dbUser.FreeBetBalance);
                }

                // APPLY NEW STATUS IMPACT
                // Statuses that should deduct balance: Approved, Won, Lost
                if (newStatus == "Approved" || newStatus == "Lost")
                {
                    dbUser.Balance -= (newAmount - dbBet.FreeBetAmount);
                    dbUser.FreeBetBalance -= dbBet.FreeBetAmount;
                    if (newStatus == "Lost")
                    {
                        dbUser.LifetimeProfit -= (newAmount - dbBet.FreeBetAmount);
                    }
                }
                else if (newStatus == "Won")
                {
                    dbUser.Balance -= (newAmount - dbBet.FreeBetAmount);
                    dbUser.FreeBetBalance -= dbBet.FreeBetAmount;
                    dbUser.Balance += dbBet.PotentialPayout;
                    
                    // Add the new net profit
                    dbUser.LifetimeProfit += (dbBet.PotentialPayout - (newAmount - dbBet.FreeBetAmount));
                }
                // newStatus == Void/Rejected/Cancelled doesn't deduct stakes

                dbBet.UpdatedAt = DateTime.UtcNow;

                string effectiveAdminName = (isAutoSettled || isAutoApproved) ? "SYSTEM_AUTO" : adminName;
                string? effectiveAuditDetails = isAutoSettled
                    ? $"Bet ID: {betId} was auto-settled to {newStatus} based on FotMob verification."
                    : (isAutoApproved
                        ? $"Bet ID: {betId} was auto-approved. Amount: {dbBet.AmountNOK}, FreeBetPart: {dbBet.FreeBetAmount}, Odds: {dbBet.Odds:N2}, Payout: {dbBet.PotentialPayout:N0}"
                        : customAuditDetails);

                string details = effectiveAuditDetails ?? $"Bet ID: {betId}, Status: {newStatus}, Amount: {dbBet.AmountNOK}, FreeBetPart: {dbBet.FreeBetAmount}, Payout: {dbBet.PotentialPayout:N0}";
                
                string actionName = isUndo 
                    ? "Bet Undone (Reverted to Approved)" 
                    : (isAutoSettled 
                        ? "Auto-Settled Bet" 
                        : (isAutoApproved
                            ? "Auto-Approved Bet"
                            : (originalStatus == "Approved" && newStatus == "Approved" ? "Bet Updated" : $"Bet {newStatus}")));

                context.AuditLogs.Add(new AuditLog
                {
                    AdminUserName = effectiveAdminName,
                    Action = actionName,
                    TargetUserName = dbUser.UserName ?? "",
                    Details = details
                });

                if (isUndo)
                {
                    if (dbBet.AiEvaluation != null)
                    {
                        dbBet.AiEvaluation.AiOutcomeResult = null;
                    }
                    dbBet.IsAutoSettled = false;
                    
                    if (dbBet.Legs != null)
                    {
                        foreach (var leg in dbBet.Legs)
                        {
                            leg.Outcome = "Pending";
                            leg.VerificationSource = "Unknown";
                            leg.Stats = "";
                        }
                    }
                }

                if (newStatus == "Approved")
                {
                    dbBet.NextCheckTime = BettingApp.Services.BetSchedulingLogic.CalculateNextCheckTime(dbBet);
                }
                else if (newStatus != "Approved" && newStatus != "Pending")
                {
                    if (newStatus == "Won" || newStatus == "Lost" || newStatus == "Void")
                    {
                        dbBet.NextCheckTime = BettingApp.Services.BetSchedulingLogic.CalculatePostSettlementCheckTime(dbBet, isAutoSettled);
                    }
                    else
                    {
                        dbBet.NextCheckTime = null;
                    }
                }

                try
                {
                    await context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (attempt < maxConcurrencyAttempts)
                    {
                        _logger.LogWarning($"[Bet #{betId}] Concurrency conflict on attempt {attempt}/{maxConcurrencyAttempts}. Retrying with fresh state...");
                        await Task.Delay(50 * attempt);
                        continue;
                    }
                    return (false, $"Action aborted: Bet #{betId} was modified concurrently.", false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to process bet {betId}");
                    return (false, "Database error during bet processing.", false);
                }

                // Notifications
                try
                {
                    if (!string.IsNullOrEmpty(dbUser.DiscordUserId))
                    {
                        bool isBetUpdate = isUndo || (originalStatus == "Approved" && newStatus == "Approved");
                        _ = _discordService.NotifyUserBetAsync(dbUser.DiscordUserId, dbBet, isUndo ? "Undone" : newStatus, isUpdate: isBetUpdate);
                    }

                    _uiNotificationService.NotifyUser(dbBet.UserId, $"Your bet status is now: {newStatus}");
                    _uiNotificationService.NotifyAdmin("Update");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Failed to send notifications for bet {betId}");
                }

                return (true, "", false);
            }

            return (false, $"Action aborted: Bet #{betId} was modified concurrently.", false);
        }
    }
}
