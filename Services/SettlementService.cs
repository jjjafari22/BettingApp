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

        public async Task<SettlementSnapshot> CreateSnapshotAsync(List<string>? excludedUserNames = null)
        {
            excludedUserNames ??= new List<string>();
            
            using var context = _dbFactory.CreateDbContext();
            
            // 1. Get Debtors (Users with negative balance)
            var debtorsQuery = await context.Users
                .Where(u => u.Balance < 0 && !u.IsAdmin && !u.IsTestUser)
                .Select(u => new { u.UserName, u.Balance, u.DiscordUsername, u.LastName })
                .ToListAsync();

            // Debtors are not filtered out completely. We keep them, but handle exclusions during P2P matching.
            var debtorsList = debtorsQuery.ToList();

            // 2. Get Creditors (Pending Withdrawals)
            var pendingWithdrawals = await context.Transactions
                .Where(t => t.Type == "Withdrawal" && t.Status == "Pending")
                .Select(t => new { t.UserName, t.AmountNOK, t.PaymentDetails })
                .ToListAsync();

            // Group pending withdrawals by user in case they made multiple requests, and filter excluded
            var creditorsList = pendingWithdrawals
                .Where(t => !excludedUserNames.Contains(t.UserName!))
                .GroupBy(t => t.UserName)
                .Select(g => new { 
                    UserName = g.Key, 
                    TotalAmount = (decimal)g.Sum(x => x.AmountNOK),
                    PaymentDetails = string.Join(" / ", g.Select(x => x.PaymentDetails).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct())
                })
                .ToList();

            var result = new SettlementResult { Date = DateTime.UtcNow };
            
            var debtors = new List<(string Name, decimal Amount, string RawUserName)>();
            var creditors = new List<(string Name, decimal Amount, string PaymentDetails, string FirstName)>();

            // Need a quick lookup for User display names if we want to format Creditors correctly
            var allUsersToFormat = debtorsList.Select(u => u.UserName)
                .Union(creditorsList.Select(c => c.UserName)).Distinct().ToList();
            
            var userLookup = await context.Users
                .Where(u => allUsersToFormat.Contains(u.UserName))
                .Select(u => new { u.UserName, u.DiscordUsername, u.LastName, u.FirstName })
                .ToDictionaryAsync(u => u.UserName!, u => u);

            string GetDisplayName(string userName)
            {
                if (userLookup.TryGetValue(userName, out var u) && !string.IsNullOrWhiteSpace(u.DiscordUsername))
                    return $"{u.DiscordUsername} ({u.LastName})";
                return userName;
            }

            string GetFirstName(string userName)
            {
                if (userLookup.TryGetValue(userName, out var u) && !string.IsNullOrWhiteSpace(u.FirstName))
                    return u.FirstName;
                return userName.Split('@')[0];
            }

            foreach (var debtor in debtorsList)
            {
                string displayName = GetDisplayName(debtor.UserName!);
                result.UserBalances.Add(new SettlementUserBalance { UserName = displayName, Balance = debtor.Balance });
                debtors.Add((displayName, Math.Abs(debtor.Balance), debtor.UserName!));
            }

            foreach (var creditor in creditorsList)
            {
                string displayName = GetDisplayName(creditor.UserName!);
                string firstName = GetFirstName(creditor.UserName!);
                creditors.Add((displayName, creditor.TotalAmount, creditor.PaymentDetails, firstName));
                // Add to UserBalances to show what they are owed in the snapshot history
                result.UserBalances.Add(new SettlementUserBalance { UserName = displayName + " (Pending Withdrawal)", Balance = creditor.TotalAmount });
            }

            // Split into P2P eligible and Castle-only (excluded)
            var p2pDebtors = debtors.Where(d => !excludedUserNames.Contains(d.RawUserName)).OrderByDescending(x => x.Amount).ToList();
            var castleOnlyDebtors = debtors.Where(d => excludedUserNames.Contains(d.RawUserName)).ToList();
            
            // Sort creditors by amount descending to minimize transaction count
            creditors = creditors.OrderByDescending(x => x.Amount).ToList();

            // 3. Match P2P Debtors to Creditors
            int dIndex = 0;
            int cIndex = 0;

            while (dIndex < p2pDebtors.Count && cIndex < creditors.Count)
            {
                var debtor = p2pDebtors[dIndex];
                var creditor = creditors[cIndex];

                var amount = Math.Min(debtor.Amount, creditor.Amount);

                result.Instructions.Add(new SettlementInstruction
                {
                    FromUser = debtor.Name,
                    ToUser = creditor.Name,
                    ToUserFirstName = creditor.FirstName,
                    Amount = amount,
                    PaymentDetails = creditor.PaymentDetails
                });

                var newDebtorAmount = debtor.Amount - amount;
                var newCreditorAmount = creditor.Amount - amount;

                if (newDebtorAmount < 0.01m) dIndex++; 
                else p2pDebtors[dIndex] = (debtor.Name, newDebtorAmount, debtor.RawUserName); 

                if (newCreditorAmount < 0.01m) cIndex++; 
                else creditors[cIndex] = (creditor.Name, newCreditorAmount, creditor.PaymentDetails, creditor.FirstName); 
            }

            // Any remaining unmatched P2P balances are system imbalances (adjustments)
            while (dIndex < p2pDebtors.Count)
            {
                result.Adjustments.Add(new SettlementAdjustment { UserName = p2pDebtors[dIndex].Name, Amount = p2pDebtors[dIndex].Amount, Reason = "Owes Castle Directly" });
                dIndex++;
            }
            
            // ALL CastleOnly (Excluded) Debtors owe Castle directly
            foreach (var debtor in castleOnlyDebtors)
            {
                result.Adjustments.Add(new SettlementAdjustment { UserName = debtor.Name, Amount = debtor.Amount, Reason = "Owes Castle Directly" });
            }
            while (cIndex < creditors.Count)
            {
                result.Adjustments.Add(new SettlementAdjustment { UserName = creditors[cIndex].Name, Amount = creditors[cIndex].Amount, Reason = "Castle Owes Directly" });
                cIndex++;
            }

            // 4. Save Snapshot
            var snapshot = new SettlementSnapshot
            {
                CreatedAt = DateTime.UtcNow,
                SettlementJson = JsonSerializer.Serialize(result),
                UserCount = debtorsList.Count + creditorsList.Count,
                TransactionCount = result.Instructions.Count,
                TotalVolume = result.Instructions.Sum(i => i.Amount)
            };

            context.SettlementSnapshots.Add(snapshot);
            await context.SaveChangesAsync();

            return snapshot;
        }

        public string GenerateCsv(SettlementResult result, DateTime createdAtUtc)
        {
            var sb = new StringBuilder();

            var norwayTime = BettingApp.Data.TimeHelpers.GetNorwayTime(createdAtUtc);

            // Add Header Info
            sb.AppendLine($"Snapshot Time (UTC),{createdAtUtc:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Snapshot Time (Norway),{norwayTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine(); 

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

            // Add historical balances to the CSV export
            if (result.UserBalances != null && result.UserBalances.Any())
            {
                sb.AppendLine();
                sb.AppendLine("Historical Balances,User,Balance");
                foreach (var bal in result.UserBalances)
                {
                    sb.AppendLine($",{bal.UserName},{bal.Balance:F0}");
                }
            }

            return sb.ToString();
        }
    }
}