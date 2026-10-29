#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using BettingApp.Data;
using BettingApp.Services;

namespace BettingApp.Components.Pages;

public partial class AdminTransactions : IDisposable
{
    private string activeTab = "pending";
    private bool isInitializing = true;
    private bool isAdmin = false;
    private string? successMessage;
    private string? errorMessage;
    private bool isSavingTransaction = false;
    private bool isProcessingAction = false;
    private string? currentAdminName;
    private System.Timers.Timer? _debounceTimer;
    private bool _isDisposed = false;

    private int TimezoneOffset { get; set; }

    private Dictionary<string, (string DiscordName, string FirstName, string LastName)> userLookup = new();
    private Dictionary<string, decimal> currentBalances = new();

    private sealed record TransactionPartyFilterItem(
        string Id,
        bool IsCashCow,
        string PrimaryLabel,
        string SecondaryLabel);

    private string ResolveCashCowName(string? cowId, string? fallbackName)
    {
        if (!string.IsNullOrEmpty(cowId))
        {
            var cow = cashCows.FirstOrDefault(c => c.Id == cowId);
            if (cow != null)
            {
                var fullName = FormatFullName(cow.FirstName, cow.LastName);
                if (!string.IsNullOrEmpty(fullName)) return fullName;
            }
        }
        return fallbackName ?? "";
    }

    private async Task<List<TransactionPartyFilterItem>> SearchTransactionPartiesAsync(string? rawTerm, string? excludeCashCowId = null)
    {
        var trimmed = rawTerm?.Trim() ?? "";
        if (trimmed.Length < 2)
        {
            return new List<TransactionPartyFilterItem>();
        }

        using var context = DbFactory.CreateDbContext();
        var cowsSource = cashCows.Any()
            ? cashCows
            : await context.CashCows.AsNoTracking().ToListAsync();

        bool isCcKeyword =
            trimmed.Equals("cc", StringComparison.OrdinalIgnoreCase) ||
            (trimmed.Length >= 3 && (
                "cash cow".Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                "cashcow".Contains(trimmed, StringComparison.OrdinalIgnoreCase)));

        var matchingCowItems = cowsSource
            .Where(c => string.IsNullOrEmpty(excludeCashCowId) || c.Id != excludeCashCowId)
            .Select(c => new
            {
                Cow = c,
                FullName = FormatFullName(c.FirstName, c.LastName)
            })
            .Where(x =>
                isCcKeyword ||
                x.FullName.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(x.Cow.FirstName) && x.Cow.FirstName.Contains(trimmed, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(x.Cow.LastName) && x.Cow.LastName.Contains(trimmed, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(x =>
                x.FullName.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(x.Cow.FirstName) && x.Cow.FirstName.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(x.Cow.LastName) && x.Cow.LastName.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase)))
            .ThenBy(x => x.FullName, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .Select(x => new TransactionPartyFilterItem(
                x.Cow.Id,
                IsCashCow: true,
                PrimaryLabel: x.FullName,
                SecondaryLabel: "Cash Cow"))
            .ToList();

        var matchingUsers = await context.Users
            .AsNoTracking()
            .Where(u =>
                (u.DiscordUsername != null && u.DiscordUsername.Contains(trimmed)) ||
                (u.UserName != null && u.UserName.Contains(trimmed)) ||
                (u.FirstName != null && u.FirstName.Contains(trimmed)) ||
                (u.LastName != null && u.LastName.Contains(trimmed)) ||
                ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Contains(trimmed))
            .OrderBy(u => u.DiscordUsername ?? u.UserName)
            .Take(10)
            .ToListAsync();

        var matchingUserItems = matchingUsers
            .Select(u => new TransactionPartyFilterItem(
                u.Id,
                IsCashCow: false,
                PrimaryLabel: !string.IsNullOrWhiteSpace(u.DiscordUsername) ? u.DiscordUsername : (u.UserName ?? "Unknown"),
                SecondaryLabel: FormatFullName(u.FirstName, u.LastName)))
            .ToList();

        return matchingCowItems.Concat(matchingUserItems).ToList();
    }

    private DateTime ToUserLocal(DateTime utcDate)
    {
        if (utcDate == DateTime.MinValue || utcDate == DateTime.MaxValue) return utcDate;
        return utcDate.AddMinutes(-TimezoneOffset);
    }

    private static string FormatFullName(string? firstName, string? lastName)
    {
        var first = firstName?.Trim() ?? "";
        var last = lastName?.Trim() ?? "";
        if (string.IsNullOrEmpty(first)) return last;
        if (string.IsNullOrEmpty(last)) return first;
        return $"{first} {last}";
    }

    private async Task PopulateUserLookup(IEnumerable<string> userIds)
    {
        var missingIds = userIds.Except(userLookup.Keys).Distinct().ToList();
        if (missingIds.Any())
        {
            using var context = DbFactory.CreateDbContext();
            var users = await context.Users
                .Where(u => missingIds.Contains(u.Id))
                .Select(u => new { u.Id, u.DiscordUsername, u.FirstName, u.LastName })
                .ToListAsync();
            foreach (var u in users)
            {
                userLookup[u.Id] = (u.DiscordUsername ?? "", u.FirstName ?? "", u.LastName ?? "");
            }
        }
    }

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var authState = await AuthStateProvider.GetAuthenticationStateAsync();
            var user = authState.User;
            currentAdminName = user.Identity?.Name;

            using var context = DbFactory.CreateDbContext();
            var userName = user.Identity?.Name;
            ApplicationUser? dbUser = null;
            if (!string.IsNullOrEmpty(userName))
            {
                dbUser = await context.Users.FirstOrDefaultAsync(u => u.UserName == userName);
            }

            isAdmin = dbUser?.IsAdmin ?? false;
            if (!isAdmin)
            {
                Nav.NavigateTo("/");
                return;
            }

            currentViewDate = DateTime.UtcNow.AddMinutes(-TimezoneOffset).Date;
            await LoadTransactions(context);
            UiNotifier.OnAdminNotification += HandleAdminNotification;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error initializing AdminTransactions: {ex.Message}");
        }
        finally
        {
            isInitializing = false;
        }
    }

    private void HandleAdminNotification(string? message)
    {
        if (_isDisposed) return;
        if (_debounceTimer == null)
        {
            _debounceTimer = new System.Timers.Timer(1000);
            _debounceTimer.AutoReset = false;
            _debounceTimer.Elapsed += async (s, e) =>
            {
                try
                {
                    if (_isDisposed) return;
                    await InvokeAsync(async () =>
                    {
                        try
                        {
                            using var context = DbFactory.CreateDbContext();
                            await LoadTransactions(context);
                            StateHasChanged();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error reloading transactions: {ex.Message}");
                        }
                    });
                }
                catch { }
            };
        }
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            try
            {
                TimezoneOffset = await JSRuntime.InvokeAsync<int>("getBrowserTimeZoneOffset");
            }
            catch { }

            _ = GoToCurrent();
        }
    }

    private async Task ShowSuccess(string message)
    {
        successMessage = message;
        StateHasChanged();
        await Task.Delay(3000);
        if (_isDisposed) return;
        successMessage = null;
        StateHasChanged();
    }

    public void Dispose()
    {
        _isDisposed = true;
        UiNotifier.OnAdminNotification -= HandleAdminNotification;
        _debounceTimer?.Dispose();
    }
}
