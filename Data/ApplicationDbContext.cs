using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BettingApp.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Bet> Bets { get; set; }
    public DbSet<Transaction> Transactions { get; set; }
    public DbSet<SystemSetting> Settings { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }

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

        builder.Entity<SystemSetting>()
            .Property(s => s.MaxPayout)
            .HasPrecision(18, 0);

        // --- NEW: Configure Balance Precision (No Decimals) ---
        builder.Entity<ApplicationUser>()
            .Property(u => u.Balance)
            .HasPrecision(18, 0); // Changed to 0 to enforce whole numbers

        // --- NEW: Performance Indexes ---
        // 1. Speeds up finding "Pending" bets for Admin
        builder.Entity<Bet>().HasIndex(b => b.Status); 
        
        // 2. Speeds up sorting bets by date (My History / Admin Logs)
        builder.Entity<Bet>().HasIndex(b => b.UpdatedAt);
        
        // 3. Speeds up "My Bets" queries for users
        builder.Entity<Bet>().HasIndex(b => b.UserId);
        
        // 4. Speeds up Transaction history
        builder.Entity<Transaction>().HasIndex(t => t.UserId);
    }
}