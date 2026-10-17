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
    private List<ApplicationUser> filterSearchResults = new();
    private ApplicationUser? filterSelectedUser;

    private string? filterType;
    private string? filterStatus;

    private async Task PerformFilterSearch()
    {
        if (string.IsNullOrWhiteSpace(filterSearchTerm) || filterSearchTerm.Length < 2)
        {
            filterSearchResults.Clear();
            return;
        }

        using var context = DbFactory.CreateDbContext();
        filterSearchResults = await context.Users
            .Where(u => u.DiscordUsername != null && u.DiscordUsername.Contains(filterSearchTerm) || (u.UserName != null && u.UserName.Contains(filterSearchTerm)))
            .OrderBy(u => u.DiscordUsername)
            .Take(5)
            .AsNoTracking()
            .ToListAsync();
    }

    private async Task SelectFilterUser(ApplicationUser user)
    {
        filterSelectedUser = user;
        filterSearchTerm = "";
        filterSearchResults.Clear();
        currentPage = 1;

        using var context = DbFactory.CreateDbContext();
        await LoadTransactions(context);
    }

    private async Task ClearFilterSelection()
    {
        filterSelectedUser = null;
        filterSearchTerm = "";
        filterSearchResults.Clear();
        currentPage = 1;

        using var context = DbFactory.CreateDbContext();
        await LoadTransactions(context);
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
        if (filterSelectedUser != null)
        {
            query = query.Where(t => t.UserId == filterSelectedUser.Id);
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

        cashCows = await context.CashCows.ToListAsync();
        ccLastUpdated.Clear();
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

                ccLastUpdated[cow.Id] = maxDate;
            }
        }

        pendingWithdrawals = await context.Transactions
            .Where(t => t.Type == "Withdrawal" && t.Status == "Pending")
            .OrderBy(t => t.Date)
            .AsNoTracking()
            .ToListAsync();

        var userIds = allTransactions.Select(t => t.UserId).Concat(pendingWithdrawals.Select(t => t.UserId)).Distinct().ToList();
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
