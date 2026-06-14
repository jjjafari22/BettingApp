using System.ComponentModel.DataAnnotations;

namespace BettingApp.Data;

public class Referral
{
    public int Id { get; set; }

    [Required]
    public string ReferrerUserId { get; set; } = string.Empty;
    public string ReferrerUserName { get; set; } = string.Empty;

    [Required]
    public string ReferredUserId { get; set; } = string.Empty;
    public string ReferredUserName { get; set; } = string.Empty;

    public decimal TotalBonusAmount { get; set; } = 2500m;

    public bool Tier1Paid { get; set; } // 20%
    public bool Tier2Paid { get; set; } // 30%
    public bool Tier3Paid { get; set; } // 50%

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
