using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using BettingApp.Data;
using Microsoft.AspNetCore.SignalR;
using BettingApp.Hubs;
using Microsoft.Extensions.Logging;

namespace BettingApp.Services
{
    public class BetProcessingService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
        private readonly IHubContext<BetHub> _hubContext;
        private readonly DiscordNotificationService _discordService;
        private readonly ILogger<BetProcessingService> _logger;

        public BetProcessingService(
            IDbContextFactory<ApplicationDbContext> dbFactory,
            IHubContext<BetHub> hubContext,
            DiscordNotificationService discordService,
            ILogger<BetProcessingService> logger)
        {
            _dbFactory = dbFactory;
            _hubContext = hubContext;
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
            bool isUndo = originalStatus != "Pending" && newStatus == "Approved";

            decimal dbAmount = (decimal)(dbBet.AmountNOK ?? 0);

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

            string details = customAuditDetails ?? $"Bet ID: {betId}, Status: {newStatus}, Amount: {dbBet.AmountNOK}, FreeBetPart: {dbBet.FreeBetAmount}, Payout: {dbBet.PotentialPayout:N0}";
            
            context.AuditLogs.Add(new AuditLog
            {
                AdminUserName = adminName,
                Action = isUndo ? "Bet Undone (Reverted to Approved)" : (isAutoSettled ? "Auto-Settled Bet" : $"Bet {newStatus}"),
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
                    _ = _discordService.NotifyUserBetAsync(dbUser.DiscordUserId, dbBet, isUndo ? "Undone" : newStatus, isUpdate: isUndo);
                }

                await _hubContext.Clients.Group(dbBet.UserId).SendAsync("ReceiveUpdate", $"Your bet status is now: {newStatus}");
                await _hubContext.Clients.Group("Admins").SendAsync("ReceiveAdminNotification", "Update");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Failed to send notifications for bet {betId}");
            }

            return (true, "");
        }
    }
}
