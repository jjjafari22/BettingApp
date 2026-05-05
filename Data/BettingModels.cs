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
        
        public string? ScreenshotUrl { get; set; }
        
        // Single Source of Truth
        // Lifecycle: Pending -> Approved -> (Won / Lost / Void)
        // Or: Pending -> Rejected / Cancelled
        public string Status { get; set; } = "Pending";
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class Transaction
    {
        public int Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        
        [Required]
        public string Type { get; set; } = string.Empty;
        
        [Required]
        public int AmountNOK { get; set; }
        
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
        // MaxPayout removed (now per-user)
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
}