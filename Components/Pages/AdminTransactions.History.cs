#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using BettingApp.Data;

namespace BettingApp.Components.Pages;

public partial class AdminTransactions
{
    private List<Transaction> allTransactions = new();
    private int currentPage = 1;
    private const int PageSize = 25;
    private bool hasMoreTransactions = false;
    private int totalTransactions = 0;

    private string filterSearchTerm = "";
    private List<TransactionPartyFilterItem> filterSearchResults = new();
    private TransactionPartyFilterItem? filterSelectedParty;
    private int _filterSearchVersion = 0;

    private string? filterType;
    private string? filterStatus;

    private async Task PerformFilterSearch()
    {
        int version = ++_filterSearchVersion;

        if (filterSelectedParty != null && filterSearchTerm != filterSelectedParty.PrimaryLabel)
        {
            filterSelectedParty = null;
            currentPage = 1;
            using var context = DbFactory.CreateDbContext();
            await LoadTransactions(context);
        }

        if (string.IsNullOrWhiteSpace(filterSearchTerm) || filterSearchTerm.Trim().Length < 2)
        {
            filterSearchResults.Clear();
            return;
        }

        var results = await SearchTransactionPartiesAsync(filterSearchTerm);
        if (version != _filterSearchVersion) return;
        filterSearchResults = results;
    }

    private async Task SelectFilterParty(TransactionPartyFilterItem party)
    {
        ++_filterSearchVersion;
        filterSelectedParty = party;
        filterSearchTerm = party.PrimaryLabel;
        filterSearchResults.Clear();
        currentPage = 1;

        using var context = DbFactory.CreateDbContext();
        await LoadTransactions(context);
    }

    private async Task ClearFilterSelection()
    {
        ++_filterSearchVersion;
        bool hadSelection = filterSelectedParty != null;
        filterSelectedParty = null;
        filterSearchTerm = "";
        filterSearchResults.Clear();

        if (hadSelection)
        {
            currentPage = 1;
            using var context = DbFactory.CreateDbContext();
            await LoadTransactions(context);
        }
    }

    private async Task OnTypeFilterChanged(ChangeEventArgs e)
    {
        filterType = e.Value?.ToString();
        currentPage = 1;
        using var context = DbFactory.CreateDbContext();
        await LoadTransactions(context);
    }

    private async Task OnStatusFilterChanged(ChangeEventArgs e)
    {
        filterStatus = e.Value?.ToString();
        currentPage = 1;
        using var context = DbFactory.CreateDbContext();
        await LoadTransactions(context);
    }

    private async Task LoadTransactions(ApplicationDbContext context)
    {
        var query = context.Transactions.Where(t => t.Status != "Pending").AsQueryable();
        if (filterSelectedParty != null)
        {
            var selectedId = filterSelectedParty.Id;
            if (filterSelectedParty.IsCashCow)
            {
                query = query.Where(t => t.SenderId == selectedId || t.ReceiverId == selectedId);
            }
            else
            {
                query = query.Where(t => t.UserId == selectedId);
            }
        }

        if (!string.IsNullOrEmpty(filterType))
        {
            query = query.Where(t => t.Type == filterType);
        }

        if (!string.IsNullOrEmpty(filterStatus))
        {
            query = query.Where(t => t.Status == filterStatus);
        }

        allTransactions = await query
            .OrderByDescending(t => t.UpdatedAt)
            .Skip((currentPage - 1) * PageSize)
            .Take(PageSize)
            .AsNoTracking()
            .ToListAsync();
        totalTransactions = await query.CountAsync();
        hasMoreTransactions = totalTransactions > (currentPage * PageSize);

        cashCows = await context.CashCows.AsNoTracking().ToListAsync();
        var nextLastUpdated = new Dictionary<string, DateTime?>();
        if (cashCows.Any())
        {
            var cowIds = cashCows.Select(c => c.Id).ToList();

            var lastSenderDates = await context.Transactions
                .Where(t => t.SenderType == "CashCow" && cowIds.Contains(t.SenderId!))
                .GroupBy(t => t.SenderId)
                .Select(g => new { CowId = g.Key, LastDate = g.Max(x => x.Date) })
                .ToListAsync();

            var lastReceiverDates = await context.Transactions
                .Where(t => t.ReceiverType == "CashCow" && cowIds.Contains(t.ReceiverId!))
                .GroupBy(t => t.ReceiverId)
                .Select(g => new { CowId = g.Key, LastDate = g.Max(x => x.Date) })
                .ToListAsync();

            foreach (var cow in cashCows)
            {
                var date1 = lastSenderDates.FirstOrDefault(x => x.CowId == cow.Id)?.LastDate;
                var date2 = lastReceiverDates.FirstOrDefault(x => x.CowId == cow.Id)?.LastDate;

                DateTime? maxDate = null;
                if (date1 != null && date2 != null) maxDate = date1 > date2 ? date1 : date2;
                else if (date1 != null) maxDate = date1;
                else if (date2 != null) maxDate = date2;

                nextLastUpdated[cow.Id] = maxDate;
            }
        }
        ccLastUpdated = nextLastUpdated;

        await UpdateCashCowPeriodNetCore(context);

        pendingWithdrawals = await context.Transactions
            .Where(t => t.Type == "Withdrawal" && t.Status == "Pending")
            .OrderBy(t => t.Date)
            .AsNoTracking()
            .ToListAsync();

        var userIds = allTransactions.Select(t => t.UserId)
            .Concat(allTransactions.Select(t => t.SenderId ?? ""))
            .Concat(allTransactions.Select(t => t.ReceiverId ?? ""))
            .Concat(pendingWithdrawals.Select(t => t.UserId))
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct()
            .ToList();
        await PopulateUserLookup(userIds);

        if (pendingWithdrawals.Any())
        {
            var pendingUserIds = pendingWithdrawals.Select(t => t.UserId).Distinct().ToList();
            var balances = await context.Users.Where(u => pendingUserIds.Contains(u.Id)).Select(u => new { u.Id, u.Balance }).ToListAsync();
            currentBalances.Clear();
            foreach (var b in balances)
            {
                currentBalances[b.Id] = b.Balance;
            }
        }

        if (viewingCow != null)
        {
            var updatedCow = cashCows.FirstOrDefault(c => c.Id == viewingCow.Id) ?? viewingCow;
            await LoadCowTransactions(updatedCow);
        }
    }

    private async Task NextPage()
    {
        currentPage++;
        using var c = DbFactory.CreateDbContext();
        await LoadTransactions(c);
    }

    private async Task PrevPage()
    {
        if (currentPage > 1)
        {
            currentPage--;
            using var c = DbFactory.CreateDbContext();
            await LoadTransactions(c);
        }
    }
}
