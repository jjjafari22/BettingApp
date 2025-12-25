using Microsoft.AspNetCore.Identity;

namespace BettingApp.Data;

public class ApplicationUser : IdentityUser
{
    // Add this new property
    public bool IsAdmin { get; set; }
    public decimal CreditLimit { get; set; }
}