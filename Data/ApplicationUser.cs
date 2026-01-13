using Microsoft.AspNetCore.Identity;

namespace BettingApp.Data;

public class ApplicationUser : IdentityUser
{
    public bool IsAdmin { get; set; }
    
    public int CreditLimit { get; set; } = 1000;

    // --- PERFORMANCE FIX: Store Balance directly ---
    // This allows instant reads without summing history.
    public decimal Balance { get; set; } = 0m;

    public string? DiscordUserId { get; set; }
    public string? DiscordUsername { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? ReferredBy { get; set; }
    public bool IsManuallyVerified { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}