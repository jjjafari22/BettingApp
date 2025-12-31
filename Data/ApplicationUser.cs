using Microsoft.AspNetCore.Identity;

namespace BettingApp.Data;

public class ApplicationUser : IdentityUser
{
    public bool IsAdmin { get; set; }
    public decimal CreditLimit { get; set; } = 1000m;

    // --- NEW: Discord Integration ---
    public string? DiscordUserId { get; set; }

    // Fix: Add creation date for new users
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // --- NEW: Personal Info ---
    public string? FirstName { get; set; }
    public string? LastName { get; set; }

    // --- NEW: Track when the user profile was last updated ---
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}