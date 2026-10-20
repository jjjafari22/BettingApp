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

        public async Task<(bool Success, string ErrorMessage)> ProcessBetAsync(
            int betId,
            string newStatus,
            string adminName,
            string expectedOriginalStatus,
            decimal newAmount,
            decimal newOdds,
            string? customAuditDetails = null,
            bool isAutoSettled = false)
        {
            const int maxConcurrencyAttempts = 3;

            for (int attempt = 1; attempt <= maxConcurrencyAttempts; attempt++)
            {
                using var context = _dbFactory.CreateDbContext();
                
                var dbBet = await context.Bets.Include(b => b.Legs).Include(b => b.AiEvaluation).FirstOrDefaultAsync(b => b.Id == betId);
                if (dbBet == null) return (false, "Bet not found.");
                
                var dbUser = await context.Users.FindAsync(dbBet.UserId);
                if (dbUser == null) return (false, "User not found.");

                if (dbBet.Status != expectedOriginalStatus)
                {
                    var lastAudit = await context.AuditLogs
                        .Where(a => a.Details.Contains($"Bet ID: {betId}"))
                        .OrderByDescending(a => a.Timestamp)
                        .FirstOrDefaultAsync();
                    
                    string otherAdminName = lastAudit != null ? lastAudit.AdminUserName : "another admin";
                    return (false, $"Action aborted: Bet #{betId} was already updated to '{dbBet.Status}' by Admin {otherAdminName}.");
                }

                string originalStatus = dbBet.Status;
                bool isUndo = (originalStatus == "Won" || originalStatus == "Lost" || originalStatus == "Void" || originalStatus == "Rejected" || originalStatus == "Cancelled") && newStatus == "Approved";

                if (originalStatus == "Pending" && (newStatus == "Approved" || newStatus == "Won" || newStatus == "Lost"))
                {
                    var currentBal = dbUser.Balance;
                    var freeBetBal = dbUser.FreeBetBalance;
                    var creditLimit = (decimal)dbUser.CreditLimit;
                    
                    var availableCredit = currentBal + freeBetBal + creditLimit - newAmount;
                    if (availableCredit < 0)
                    {
                        return (false, $"Insufficient funds. Available: {(currentBal + freeBetBal + creditLimit):N0} kr. Required: {newAmount:N0} kr.");
                    }
                }

                decimal dbAmount = (decimal)(dbBet.AmountNOK ?? 0);

                if (originalStatus != "Pending" && (newStatus == "Approved" || newStatus == "Won" || newStatus == "Lost") && newAmount > dbAmount)
                {
                    var currentBal = dbUser.Balance;
                    var creditLimit = (decimal)dbUser.CreditLimit;
                    var additionalAmount = newAmount - dbAmount;
                    
                    var availableCredit = currentBal + creditLimit - additionalAmount;
                    if (availableCredit < 0)
                    {
                        return (false, $"Insufficient funds for stake increase. Available: {(currentBal + creditLimit):N0} kr. Additional Required: {additionalAmount:N0} kr.");
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

                // APPLY NEW VALUES
                dbBet.Status = newStatus;
                dbBet.AmountNOK = (int?)newAmount;
                dbBet.Odds = newOdds;

                if (isAutoSettled)
                {
                    dbBet.IsAutoSettled = true;
                }

                if (originalStatus == "Pending")
                {
                    // Automatic consumption if user has free bet balance
                    dbBet.FreeBetAmount = Math.Min(newAmount, dbUser.FreeBetBalance);
                }
                else 
                {
                     // Safety: Capping free bet amount if admin reduced the total stake
                     dbBet.FreeBetAmount = Math.Min(dbBet.FreeBetAmount, newAmount);
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

                string effectiveAdminName = isAutoSettled ? "SYSTEM_AUTO" : adminName;
                string? effectiveAuditDetails = isAutoSettled
                    ? $"Bet ID: {betId} was auto-settled to {newStatus} based on FotMob verification."
                    : customAuditDetails;

                string details = effectiveAuditDetails ?? $"Bet ID: {betId}, Status: {newStatus}, Amount: {dbBet.AmountNOK}, FreeBetPart: {dbBet.FreeBetAmount}, Payout: {dbBet.PotentialPayout:N0}";
                
                string actionName = isUndo 
                    ? "Bet Undone (Reverted to Approved)" 
                    : (isAutoSettled 
                        ? "Auto-Settled Bet" 
                        : (originalStatus == "Approved" && newStatus == "Approved" ? "Bet Updated" : $"Bet {newStatus}"));

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
                        bool isAiFinished = false;
                        if (dbBet.AiEvaluation != null && !string.IsNullOrEmpty(dbBet.AiEvaluation.AiOutcomeResult))
                        {
                            try {
                                var doc = System.Text.Json.JsonDocument.Parse(dbBet.AiEvaluation.AiOutcomeResult);
                                if (doc.RootElement.TryGetProperty("overallStatus", out var prop))
                                {
                                    var s = prop.GetString()?.ToUpperInvariant() ?? "";
                                    if (s == "WON" || s == "LOST" || s == "VOID" || s == "UNKNOWN")
                                    {
                                        isAiFinished = true;
                                    }
                                }
                            } catch { }
                        }
                        
                        if (isAiFinished || (dbBet.AiEvaluation != null && dbBet.AiEvaluation.AiOutcomeResult == "Admin Override"))
                        {
                            dbBet.NextCheckTime = null;
                        }
                        else
                        {
                            // Schedule AI Check for 60 minutes later
                            dbBet.NextCheckTime = DateTime.UtcNow.AddMinutes(60);
                        }
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
                    return (false, $"Action aborted: Bet #{betId} was modified concurrently.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to process bet {betId}");
                    return (false, "Database error during bet processing.");
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

                return (true, "");
            }

            return (false, $"Action aborted: Bet #{betId} was modified concurrently.");
        }
    }
}
