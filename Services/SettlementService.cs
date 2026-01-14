using System.Text;
using System.Text.Json;
using BettingApp.Data;
using Microsoft.EntityFrameworkCore;

namespace BettingApp.Services
{
    public class SettlementService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;

        public SettlementService(IDbContextFactory<ApplicationDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task<SettlementSnapshot> CreateSnapshotAsync()
        {
            using var context = _dbFactory.CreateDbContext();
            
            // 1. Get all users with non-zero balance
            var users = await context.Users
                .Where(u => u.Balance != 0)
                .Select(u => new { u.UserName, u.Balance })
                .ToListAsync();

            var result = new SettlementResult { Date = DateTime.UtcNow };
            
            var debtors = new List<(string Name, decimal Amount)>();
            var creditors = new List<(string Name, decimal Amount)>();

            // 2. Filter & Sort
            foreach (var user in users)
            {
                // Removed tolerance logic to keep settlement precise.
                // All non-zero balances are now processed.

                if (user.Balance < 0)
                    debtors.Add((user.UserName!, Math.Abs(user.Balance)));
                else
                    creditors.Add((user.UserName!, user.Balance));
            }

            // Sort by amount descending to minimize transaction count (Greedy approach)
            debtors = debtors.OrderByDescending(x => x.Amount).ToList();
            creditors = creditors.OrderByDescending(x => x.Amount).ToList();

            // 3. Match Debtors to Creditors
            int dIndex = 0;
            int cIndex = 0;

            while (dIndex < debtors.Count && cIndex < creditors.Count)
            {
                var debtor = debtors[dIndex];
                var creditor = creditors[cIndex];

                var amount = Math.Min(debtor.Amount, creditor.Amount);

                result.Instructions.Add(new SettlementInstruction
                {
                    FromUser = debtor.Name,
                    ToUser = creditor.Name,
                    Amount = amount
                });

                // Adjust remaining amounts
                debtor.Amount -= amount;
                creditor.Amount -= amount;

                // Update list values (structs/tuples are value types, so we update the list directly or track indices)
                // Since we modify 'amount', we need to check who is "settled"
                
                if (debtor.Amount < 0.01m) dIndex++; // Debtor settled
                else debtors[dIndex] = (debtor.Name, debtor.Amount); // Update remainder

                if (creditor.Amount < 0.01m) cIndex++; // Creditor settled
                else creditors[cIndex] = (creditor.Name, creditor.Amount); // Update remainder
            }

            // Any remaining unmatched balances are system imbalances (adjustments)
            while (dIndex < debtors.Count)
            {
                result.Adjustments.Add(new SettlementAdjustment { UserName = debtors[dIndex].Name, Amount = -debtors[dIndex].Amount, Reason = "Unmatched Deficit" });
                dIndex++;
            }
            while (cIndex < creditors.Count)
            {
                result.Adjustments.Add(new SettlementAdjustment { UserName = creditors[cIndex].Name, Amount = creditors[cIndex].Amount, Reason = "Unmatched Surplus" });
                cIndex++;
            }

            // 4. Save Snapshot
            var snapshot = new SettlementSnapshot
            {
                CreatedAt = DateTime.UtcNow,
                SettlementJson = JsonSerializer.Serialize(result),
                UserCount = users.Count,
                TransactionCount = result.Instructions.Count,
                TotalVolume = result.Instructions.Sum(i => i.Amount)
            };

            context.SettlementSnapshots.Add(snapshot);
            await context.SaveChangesAsync();

            return snapshot;
        }

        // Updated signature to accept createdAtUtc
        public string GenerateCsv(SettlementResult result, DateTime createdAtUtc)
        {
            var sb = new StringBuilder();

            // Helper to get Norway time (same logic as UI)
            DateTime GetNorwayTime(DateTime utc)
            {
                try
                {
                    var tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");
                    return TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
                }
                catch
                {
                    try { return TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time")); }
                    catch { return utc.AddHours(1); }
                }
            }

            var norwayTime = GetNorwayTime(createdAtUtc);

            // Add Header Info
            sb.AppendLine($"Snapshot Time (UTC),{createdAtUtc:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Snapshot Time (Norway),{norwayTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine(); // Empty line separator

            // CSV Columns
            sb.AppendLine("Type,From,To,Amount,Details");

            foreach (var instr in result.Instructions)
            {
                sb.AppendLine($"Payment,{instr.FromUser},{instr.ToUser},{instr.Amount:F0},");
            }

            foreach (var adj in result.Adjustments)
            {
                sb.AppendLine($"Adjustment,-,-,{adj.Amount:F0},{adj.Reason} ({adj.UserName})");
            }

            return sb.ToString();
        }
    }
}