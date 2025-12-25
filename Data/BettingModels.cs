using System.ComponentModel.DataAnnotations;

namespace BettingApp.Data
{
    public class Bet
    {
        public int Id { get; set; }
        public string UserId { get; set; } 
        public string UserName { get; set; }
        
        [Required]
        [Range(1, 100000, ErrorMessage = "Amount must be positive")]
        public decimal AmountNOK { get; set; }

        [Required]
        [Range(1.0, 1000.0, ErrorMessage = "Odds must be at least 1.0")]
        public decimal Odds { get; set; }

        // Helper property to calculate payout (not necessarily stored in DB if not needed)
        public decimal PotentialPayout => AmountNOK * Odds;
        
        public string? ScreenshotUrl { get; set; }
        public string Status { get; set; } = "Pending";

        // Outcome tracks the bet result (e.g., Won, Lost, Void)
        public string? Outcome { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class Transaction
    {
        public int Id { get; set; }
        public string UserId { get; set; }
        public string UserName { get; set; } // Store name for easier display in Admin
        
        [Required]
        public string Type { get; set; } // "Deposit" or "Withdrawal"
        
        [Required]
        public decimal AmountNOK { get; set; }
        
        [Required]
        public string Platform { get; set; } // e.g., "Bank", "Vipps", "Crypto"
        
        public DateTime Date { get; set; } = DateTime.UtcNow;
    }
}