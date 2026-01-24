using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BettingApp.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Bet> Bets { get; set; }
    public DbSet<Transaction> Transactions { get; set; }
    public DbSet<SystemSetting> Settings { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }

    public DbSet<SettlementSnapshot> SettlementSnapshots { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder); // IMPORTANT: This configures Identity tables!

        // Configure precision for decimal properties to avoid warnings
        builder.Entity<Bet>()
            .Property(b => b.Odds)
            .HasPrecision(18, 2); // Stores up to 18 digits, 2 of them after decimal (e.g., 1.50)

        builder.Entity<SystemSetting>()
            .Property(s => s.MinBetAmount)
            .HasPrecision(18, 0);

        // --- NEW: Configure Balance & MaxPayout Precision (No Decimals) ---
        builder.Entity<ApplicationUser>()
            .Property(u => u.Balance)
            .HasPrecision(18, 0); 
            
        builder.Entity<ApplicationUser>()
            .Property(u => u.MaxPayout)
            .HasPrecision(18, 0);

        // --- Fix for Warning [30000] ---
        builder.Entity<SettlementSnapshot>()
            .Property(s => s.TotalVolume)
            .HasPrecision(18, 0);

        // --- NEW: Performance Indexes ---
        builder.Entity<Bet>().HasIndex(b => b.Status); 
        builder.Entity<Bet>().HasIndex(b => b.UpdatedAt);
        builder.Entity<Bet>().HasIndex(b => b.UserId);
        builder.Entity<Transaction>().HasIndex(t => t.UserId);
    }
}