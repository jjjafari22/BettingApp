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
            var creditors = new List<(string Name, decimal Amount, string PaymentDetails, string FirstName, string DiscordUsername, string FullName)>();

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

            string GetDiscordUsername(string userName)
            {
                if (userLookup.TryGetValue(userName, out var u) && !string.IsNullOrWhiteSpace(u.DiscordUsername))
                    return u.DiscordUsername;
                return string.Empty;
            }

            string GetFullName(string userName)
            {
                if (userLookup.TryGetValue(userName, out var u))
                {
                    var parts = new List<string>();
                    if (!string.IsNullOrWhiteSpace(u.FirstName)) parts.Add(u.FirstName);
                    if (!string.IsNullOrWhiteSpace(u.LastName)) parts.Add(u.LastName);
                    if (parts.Any()) return string.Join(" ", parts);
                }
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
                string discordUsername = GetDiscordUsername(creditor.UserName!);
                string fullName = GetFullName(creditor.UserName!);
                creditors.Add((displayName, creditor.TotalAmount, creditor.PaymentDetails, firstName, discordUsername, fullName));
                // Add to UserBalances to show what they are owed in the snapshot history
                result.UserBalances.Add(new SettlementUserBalance { UserName = displayName + " (Pending Withdrawal)", Balance = creditor.TotalAmount });
            }

            // Split into P2P eligible and Castle-only (excluded)
            var p2pDebtors = debtors.Where(d => !excludedUserNames.Contains(d.RawUserName)).OrderByDescending(x => x.Amount).ToList();
            var castleOnlyDebtors = debtors.Where(d => excludedUserNames.Contains(d.RawUserName)).ToList();
            
            // Sort creditors by amount descending to minimize transaction count
            creditors = creditors.OrderByDescending(x => x.Amount).ToList();

            // 3. Match P2P Debtors to Creditors
            
            // OPTIMIZATION: Full Subset-Sum Matching for absolute minimum transactions
            IEnumerable<List<int>> GetCombinations(int n, int k)
            {
                var combResult = new List<List<int>>();
                var combination = new int[k];
                void Generate(int index, int start)
                {
                    if (index == k) { combResult.Add(new List<int>(combination)); return; }
                    for (int i = start; i < n; i++) { combination[index] = i; Generate(index + 1, i + 1); }
                }
                Generate(0, 0);
                return combResult;
            }

            bool matchFound = true;
            while (matchFound && p2pDebtors.Count > 0 && creditors.Count > 0)
            {
                matchFound = false;
                int maxSubsetSize = Math.Min(6, p2pDebtors.Count + creditors.Count);

                for (int size = 2; size <= maxSubsetSize && !matchFound; size++)
                {
                    for (int dCount = 1; dCount < size; dCount++)
                    {
                        int cCount = size - dCount;
                        if (dCount > p2pDebtors.Count || cCount > creditors.Count) continue;

                        var dCombs = GetCombinations(p2pDebtors.Count, dCount);
                        var cCombs = GetCombinations(creditors.Count, cCount);

                        foreach (var dComb in dCombs)
                        {
                            decimal dSum = dComb.Sum(i => p2pDebtors[i].Amount);
                            foreach (var cComb in cCombs)
                            {
                                decimal cSum = cComb.Sum(i => creditors[i].Amount);
                                if (Math.Abs(dSum - cSum) < 0.01m)
                                {
                                    // Generate greedy instructions for this exact subset
                                    var subDebtors = dComb.Select(i => p2pDebtors[i]).ToList();
                                    var subCreditors = cComb.Select(i => creditors[i]).ToList();

                                    int sd = 0, sc = 0;
                                    while (sd < subDebtors.Count && sc < subCreditors.Count)
                                    {
                                        var subD = subDebtors[sd];
                                        var subC = subCreditors[sc];
                                        var amount = Math.Min(subD.Amount, subC.Amount);

                                        result.Instructions.Add(new SettlementInstruction
                                        {
                                            FromUser = subD.Name,
                                            ToUser = subC.Name,
                                            ToUserFirstName = subC.FirstName,
                                            ToUserFullName = subC.FullName,
                                            ToUserDiscordUsername = subC.DiscordUsername,
                                            Amount = amount,
                                            PaymentDetails = subC.PaymentDetails
                                        });

                                        var nd = subD.Amount - amount;
                                        var nc = subC.Amount - amount;

                                        if (nd < 0.01m) sd++; else subDebtors[sd] = (subD.Name, nd, subD.RawUserName);
                                        if (nc < 0.01m) sc++; else subCreditors[sc] = (subC.Name, nc, subC.PaymentDetails, subC.FirstName, subC.DiscordUsername, subC.FullName);
                                    }

                                    foreach (var i in dComb.OrderByDescending(x => x)) p2pDebtors.RemoveAt(i);
                                    foreach (var i in cComb.OrderByDescending(x => x)) creditors.RemoveAt(i);

                                    matchFound = true;
                                    break;
                                }
                            }
                            if (matchFound) break;
                        }
                        if (matchFound) break;
                    }
                }
            }

            // Greedy matching for any remainder that didn't fit into a subset <= size 6
            // We re-sort after EVERY transaction. This spreads the load across multiple users
            // rather than completely "draining" the largest user and forcing them to make many payments.
            while (p2pDebtors.Count > 0 && creditors.Count > 0)
            {
                p2pDebtors = p2pDebtors.OrderByDescending(x => x.Amount).ToList();
                creditors = creditors.OrderByDescending(x => x.Amount).ToList();

                var debtor = p2pDebtors[0];
                var creditor = creditors[0];

                var amount = Math.Min(debtor.Amount, creditor.Amount);

                result.Instructions.Add(new SettlementInstruction
                {
                    FromUser = debtor.Name,
                    ToUser = creditor.Name,
                    ToUserFirstName = creditor.FirstName,
                    ToUserFullName = creditor.FullName,
                    ToUserDiscordUsername = creditor.DiscordUsername,
                    Amount = amount,
                    PaymentDetails = creditor.PaymentDetails
                });

                var newDebtorAmount = debtor.Amount - amount;
                var newCreditorAmount = creditor.Amount - amount;

                if (newDebtorAmount < 0.01m) p2pDebtors.RemoveAt(0);
                else p2pDebtors[0] = (debtor.Name, newDebtorAmount, debtor.RawUserName); 

                if (newCreditorAmount < 0.01m) creditors.RemoveAt(0);
                else creditors[0] = (creditor.Name, newCreditorAmount, creditor.PaymentDetails, creditor.FirstName, creditor.DiscordUsername, creditor.FullName); 
            }

            // Any remaining unmatched P2P balances are system imbalances (adjustments)
            foreach (var debtor in p2pDebtors)
            {
                result.Adjustments.Add(new SettlementAdjustment { UserName = debtor.Name, Amount = debtor.Amount, Reason = "Owes Castle Directly" });
            }
            
            // ALL CastleOnly (Excluded) Debtors owe Castle directly
            foreach (var debtor in castleOnlyDebtors)
            {
                result.Adjustments.Add(new SettlementAdjustment { UserName = debtor.Name, Amount = debtor.Amount, Reason = "Owes Castle Directly" });
            }
            foreach (var creditor in creditors)
            {
                result.Adjustments.Add(new SettlementAdjustment { UserName = creditor.Name, Amount = creditor.Amount, Reason = "Castle Owes Directly" });
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