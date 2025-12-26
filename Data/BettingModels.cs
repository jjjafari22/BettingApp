using System.ComponentModel.DataAnnotations;

namespace BettingApp.Data
{
    public class Bet
    {
        public int Id { get; set; }
        // Initialize with default values to prevent CS8618
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        
        [Required]
        [Range(1, 100000, ErrorMessage = "Amount must be positive")]
        public decimal AmountNOK { get; set; }

        [Required]
        [Range(1.0, 1000.0, ErrorMessage = "Odds must be at least 1.0")]
        public decimal Odds { get; set; }

        public decimal PotentialPayout => AmountNOK * Odds;
        
        public string? ScreenshotUrl { get; set; }
        public string Status { get; set; } = "Pending";
        public string? Outcome { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class Transaction
    {
        public int Id { get; set; }
        // Initialize with default values to prevent CS8618
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        
        [Required]
        public string Type { get; set; } = string.Empty; // Initialize
        
        [Required]
        public decimal AmountNOK { get; set; }
        
        [Required]
        public string Platform { get; set; } = string.Empty; // Initialize
        
        public DateTime Date { get; set; } = DateTime.UtcNow;
    }
}