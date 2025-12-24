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
        
        public string? ScreenshotUrl { get; set; }
        public string Status { get; set; } = "Pending"; 
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}