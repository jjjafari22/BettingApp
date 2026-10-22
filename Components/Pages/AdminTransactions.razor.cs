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
