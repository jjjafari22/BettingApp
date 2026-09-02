using System.ComponentModel.DataAnnotations;

namespace BettingApp.Data
{
    public class Bet
    {
        public int Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        
        public int? AmountNOK { get; set; }
        public decimal Odds { get; set; }

        // Portion of the bet that was placed using a Free Bet balance
        public decimal FreeBetAmount { get; set; } = 0m;

        public decimal PotentialPayout 
        {
            get 
            {
                if (!AmountNOK.HasValue) return 0;

                // Normal part: Stake * Odds
                decimal normalAmount = (decimal)AmountNOK.Value - FreeBetAmount;
                decimal normalPayout = normalAmount * Odds;

                // Free Bet part: Stake * (Odds - 1)
                // Net winnings only
                decimal freeBetPayout = FreeBetAmount * (Odds - 1);

                return Math.Floor(normalPayout + freeBetPayout);
            }
        }

        public decimal NetLiability
        {
            get
            {
                if (!AmountNOK.HasValue) return 0;
                return Math.Floor((decimal)AmountNOK.Value * (Odds - 1));
            }
        }
        
        public string? ScreenshotUrl { get; set; }
        
        public string? AiVisionResultJson { get; set; }
        public string? AiVisionError { get; set; }
        public string? AiOutcomeResult { get; set; }
        
        // Single Source of Truth
        // Lifecycle: Pending -> Approved -> (Won / Lost / Void)
        // Or: Pending -> Rejected / Cancelled
        public string Status { get; set; } = "Pending";
        
        public DateTime? MatchStartTime { get; set; }
        public DateTime? NextCheckTime { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        [ConcurrencyCheck]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class Transaction
    {
        public int Id { get; set; }
        
        // Legacy fields (kept for backwards compatibility for standard deposits/withdrawals)
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        
        [Required]
        public string Type { get; set; } = string.Empty;
        
        [Required]
        public int AmountNOK { get; set; }
        
        // NEW Peer-to-Peer & CashCow Tracking Fields
        public string? SenderType { get; set; } // "User", "CashCow", "System"
        public string? SenderId { get; set; }
        public string? SenderName { get; set; }
        
        public string? ReceiverType { get; set; } // "User", "CashCow", "System"
        public string? ReceiverId { get; set; }
        public string? ReceiverName { get; set; }
        
        public decimal FulfilledAmount { get; set; } = 0m;
        // ----------------------------------------
        
        [Required]
        public string Platform { get; set; } = string.Empty;
        
        public string? PaymentDetails { get; set; }
        
        public DateTime Date { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public string Status { get; set; } = "Completed"; 
    }

    public class SystemSetting
    {
        public int Id { get; set; }
        public decimal MinBetAmount { get; set; } = 100m; 
        // CashCowsJson removed in favor of dedicated DB Table
    }

    public class CashCow
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Vipps { get; set; } = string.Empty;
        public string Revolut { get; set; } = string.Empty;
        public string BankTransfer { get; set; } = string.Empty;
        public string OtherPlatformName { get; set; } = string.Empty;
        public string OtherPaymentDetails { get; set; } = string.Empty;
        
        public decimal Balance { get; set; } = 0m;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class AuditLog
    {
        public int Id { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string AdminUserName { get; set; } = string.Empty; 
        public string Action { get; set; } = string.Empty; 
        public string TargetUserName { get; set; } = string.Empty; 
        public string Details { get; set; } = string.Empty; 
    }

    public class BroadcastHistory
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string AdminUserName { get; set; } = string.Empty;
        public int RecipientCount { get; set; }
        public string RecipientNames { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class CoverageShift
    {
        public int Id { get; set; }
        public DateTime StartTimeUtc { get; set; } 
        public string AdminUserName { get; set; } = string.Empty; 
        public string? Note { get; set; }
        public bool IsPartlyAvailable { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}