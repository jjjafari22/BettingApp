using System.ComponentModel.DataAnnotations;

namespace BettingApp.Data
{
    public class Bet
    {
        public int Id { get; set; }
        // Initialize with default values to prevent CS8618
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        
        // Removed Required and Range to allow submission without amount
        public int? AmountNOK { get; set; }

        // Removed Required and Range to allow submission without specifying odds
        public decimal Odds { get; set; }

        // Round down to the nearest whole number
        public decimal PotentialPayout => Math.Floor((decimal)(AmountNOK ?? 0) * Odds);
        
        public string? ScreenshotUrl { get; set; }
        public string Status { get; set; } = "Pending";
        public string? Outcome { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class Transaction
    {
        public int Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        
        [Required]
        public string Type { get; set; } = string.Empty;
        
        [Required]
        // Enforce whole numbers for transactions
        public int AmountNOK { get; set; }
        
        [Required]
        public string Platform { get; set; } = string.Empty;
        
        public DateTime Date { get; set; } = DateTime.UtcNow;
    }
}