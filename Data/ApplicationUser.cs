using Microsoft.AspNetCore.Identity;

namespace BettingApp.Data;

public class ApplicationUser : IdentityUser
{
    public bool IsAdmin { get; set; }
    public decimal CreditLimit { get; set; } = 1000m;

    // --- NEW: Discord Integration ---
    public string? DiscordUserId { get; set; }
}