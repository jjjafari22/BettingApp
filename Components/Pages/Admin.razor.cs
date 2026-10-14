#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using BettingApp.Data;
using BettingApp.Services;
using BettingApp.Models;

namespace BettingApp.Components.Pages;

// Core page lifecycle, global collections, and notification listeners
public partial class Admin
{
    private bool isInitializing = true;
    private bool isSavingBet = false;

    private List<Bet> pendingBets = new();
    private List<Bet> approvedBets = new();
    private List<Bet> completedBets = new();
    private List<(ulong Id, string Name, string CategoryName)> discordChannels = new();
    private Dictionary<string, (string DiscordName, string LastName, string DiscordUserId)> userLookup = new();
    private Dictionary<string, string> userDiscordLinks = new();
    private Dictionary<int, (string Json, BettingApp.Services.AiVisionExtractionResult? Result)> _visionCache = new();
    private Dictionary<int, (string Json, BettingApp.Services.AiOutcomeResultData? Result)> _outcomeCache = new();
    private int pendingPage = 1;
    private int approvedPage = 1;
    private string approvedSortColumn = "UpdatedAt";
    private bool approvedSortDesc = true;
    private int completedPage = 1;
    private int pageSize = 25;
    

    private bool hasMoreCompletedBets = false;
    private bool hasMoreApprovedBets = false;
    private bool hasMorePendingBets = false;
    private int totalPendingBets = 0;
    private int totalApprovedBets = 0;

    private string? _successMessage;
    private string? successMessage
    {
        get => _successMessage;
        set { _successMessage = value; if (value != null) AutoClearMessages(); }
    }

    private string? _errorMessage;
    private string? errorMessage
    {
        get => _errorMessage;
        set { _errorMessage = value; if (value != null) AutoClearMessages(); }
    }

    private void AutoClearMessages()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(5000);
            var changed = false;
            if (_successMessage != null) { _successMessage = null; changed = true; }
            if (_errorMessage != null) { _errorMessage = null; changed = true; }
            if (_successMessage != null) { _successMessage = null; changed = true; }
            if (changed) await InvokeAsync(StateHasChanged);
        });
    }
    private System.Timers.Timer? adminDebounceTimer;
    private string? currentAdminName;

    private System.Timers.Timer? uiRefreshTimer;

    private int TimezoneOffset { get; set; }

    private DateTime ToUserLocal(DateTime utcDate)
    {
        if (utcDate == DateTime.MinValue || utcDate == DateTime.MaxValue) return utcDate;
        return utcDate.AddMinutes(-TimezoneOffset);
    }

    private static readonly System.Threading.SemaphoreSlim _userLookupLock = new(1, 1);

    private async Task PopulateUserLookup(IEnumerable<string> userIds)
    {
        await _userLookupLock.WaitAsync();
        try
        {
            var missingIds = userIds.Except(userLookup.Keys).Distinct().ToList();
            if (missingIds.Any())
            {
                using var context = DbFactory.CreateDbContext();
                var users = await context.Users.Where(u => missingIds.Contains(u.Id)).Select(u => new { u.Id, u.DiscordUsername, u.LastName, u.DiscordUserId }).ToListAsync();
                foreach(var u in users) {
                    string discordName = u.DiscordUsername ?? "";
                    userLookup[u.Id] = (discordName, u.LastName ?? "", u.DiscordUserId ?? "");
                    
                    var expectedChannel = $"{discordName.Trim().ToLower().Replace(" ", "-")}-admins";
                    var matchedChannel = discordChannels?.FirstOrDefault(c => string.Equals(c.Name, expectedChannel, StringComparison.OrdinalIgnoreCase));
                    userDiscordLinks[u.Id] = matchedChannel.HasValue && matchedChannel.Value.Id != 0 
                        ? $"https://discord.com/channels/{DiscordService.GuildId}/{matchedChannel.Value.Id}" 
                        : $"https://discord.com/channels/{DiscordService.GuildId}";
                }
            }
        }
        finally
        {
            _userLookupLock.Release();
        }
    }

    private HttpContext? HttpContext { get; set; }


    protected override async Task OnInitializedAsync()
    {
        if (HttpContext != null) return; // Prerendering: exit instantly to show spinner

        var authState = await AuthStateProvider.GetAuthenticationStateAsync();
        var user = authState.User;
        currentAdminName = user.Identity?.Name;

        try
        {
            using var context = DbFactory.CreateDbContext();
            var currentUserName = user.Identity?.Name;
            var dbUser = currentUserName != null ? await context.Users.FirstOrDefaultAsync(u => u.UserName == currentUserName) : null;
            if (dbUser == null || !dbUser.IsAdmin)
            {
                Nav.NavigateTo("/");
                return;
            }
            discordChannels = await DiscordService.GetGuildTextChannelsAsync();
            await Task.WhenAll(LoadPendingBets(), LoadApprovedBets(), LoadCompletedBets());
            if (ReviewBetId.HasValue)
            {
                var targetBet = pendingBets.FirstOrDefault(b => b.Id == ReviewBetId.Value);
                if (targetBet == null) targetBet = approvedBets.FirstOrDefault(b => b.Id == ReviewBetId.Value);

                if (targetBet != null) StartReview(targetBet);
                else 
                {
                    var exists = await context.Bets.AnyAsync(b => b.Id == ReviewBetId.Value);
                    if (exists) errorMessage = $"Bet #{ReviewBetId.Value} is already reviewed.";
                    else errorMessage = $"Bet #{ReviewBetId.Value} not found.";
                }
            }
        }
        catch (Exception ex)
        {
            errorMessage = "A database connection error occurred during initialization. Please refresh the page.";
            Console.WriteLine($"[Admin.razor] OnInitializedAsync DB Error: {ex.Message}");
        }
        finally
        {
            isInitializing = false;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            try
            {
                var value = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "openInNativeApp");
                if (!string.IsNullOrEmpty(value)) openInNativeApp = value == "true";

                var pkg = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "unibetPwaPackageName");
                if (!string.IsNullOrEmpty(pkg)) unibetPwaPackageName = pkg;

                var pkgPin = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "pinnaclePwaPackageName");
                if (!string.IsNullOrEmpty(pkgPin)) pinnaclePwaPackageName = pkgPin;

                var pkgBetsson = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "betssonPwaPackageName");
                if (!string.IsNullOrEmpty(pkgBetsson)) betssonPwaPackageName = pkgBetsson;

                var pkgBet365 = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "bet365PwaPackageName");
                if (!string.IsNullOrEmpty(pkgBet365)) bet365PwaPackageName = pkgBet365;

                var pkgCoolbet = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "coolbetPwaPackageName");
                if (!string.IsNullOrEmpty(pkgCoolbet)) coolbetPwaPackageName = pkgCoolbet;

                
                var ua = await JSRuntime.InvokeAsync<string>("eval", "navigator.userAgent");
                var uaLower = ua.ToLowerInvariant();
                isDesktop = !uaLower.Contains("mobi") && !uaLower.Contains("android") && !uaLower.Contains("iphone");
                isAndroid = uaLower.Contains("android");
                
                StateHasChanged();
            }
            catch { }

             try
            {
                TimezoneOffset = await JSRuntime.InvokeAsync<int>("getBrowserTimeZoneOffset");
                
                uiRefreshTimer = new System.Timers.Timer(15000); // 15 seconds
                uiRefreshTimer.Elapsed += (s, e) => InvokeAsync(StateHasChanged);
                uiRefreshTimer.Start();
                
                StateHasChanged(); 
            }
            catch (TaskCanceledException) { /* Ignore when user navigates away */ }
            catch (Exception ex) { Logger.LogError($"Timezone error: {ex.Message}"); }

            UiNotifier.OnAdminNotification += HandleAdminNotification;
        }
        if (shouldFocusReviewModal) { shouldFocusReviewModal = false;
            try { await reviewModalRef.FocusAsync(); } catch { } }
    }

    private void HandleAdminNotification(string? message)
    {
        if (!string.IsNullOrEmpty(message) && message != "Update") successMessage = message;
        
        if (adminDebounceTimer == null)
        {
            adminDebounceTimer = new System.Timers.Timer(1000);
            adminDebounceTimer.AutoReset = false;
            adminDebounceTimer.Elapsed += async (s, e) => 
            {
                await InvokeAsync(async () =>
                {
                    try
                    {
                        await Task.WhenAll(LoadPendingBets(), LoadApprovedBets(), LoadCompletedBets());
                        
                        if (reviewingBetModel != null)
                        {
                            var matchingBet = pendingBets.FirstOrDefault(b => b.Id == reviewingBetModel.Id)
                                           ?? approvedBets.FirstOrDefault(b => b.Id == reviewingBetModel.Id)
                                           ?? completedBets.FirstOrDefault(b => b.Id == reviewingBetModel.Id);
                            if (matchingBet != null)
                            {
                                if (matchingBet.AiOutcomeResult != reviewingBetModel.AiOutcomeResult ||
                                    matchingBet.Status != reviewingBetModel.Status ||
                                    matchingBet.NextCheckTime != reviewingBetModel.NextCheckTime ||
                                    matchingBet.AiVisionResultJson != reviewingBetModel.AiVisionResultJson)
                                {
                                    reviewingBetModel.AiOutcomeResult = matchingBet.AiOutcomeResult;
                                    reviewingBetModel.Status = matchingBet.Status;
                                    reviewingBetModel.NextCheckTime = matchingBet.NextCheckTime;
                                    reviewingBetModel.MatchStartTime = matchingBet.MatchStartTime;
                                    reviewingBetModel.AiVisionResultJson = matchingBet.AiVisionResultJson;
                                    reviewingBetModel.AiVisionError = matchingBet.AiVisionError;
                                    if (matchingBet.Legs != null)
                                    {
                                        reviewingBetModel.Legs = matchingBet.Legs.ToList();
                                    }
                                    
                                    if (!string.IsNullOrEmpty(reviewingBetModel.AiOutcomeResult) && 
                                        !reviewingBetModel.AiOutcomeResult.StartsWith("Error") && 
                                        !reviewingBetModel.AiOutcomeResult.StartsWith("Exception"))
                                    {
                                        try {
                                            ParsedAiOutcome = System.Text.Json.JsonSerializer.Deserialize<BettingApp.Services.AiOutcomeResultData>(
                                                reviewingBetModel.AiOutcomeResult, 
                                                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                                            );
                                        } catch { }
                                    }
                                }
                            }
                        }

                        StateHasChanged();
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Failed to refresh bets on admin notification.");
                    }
                });
            };
        }
        
        adminDebounceTimer.Stop();
        adminDebounceTimer.Start();
    }

    public async ValueTask DisposeAsync()
    {
        uiRefreshTimer?.Dispose();
        adminDebounceTimer?.Dispose();
        UiNotifier.OnAdminNotification -= HandleAdminNotification;
        await Task.CompletedTask;
    }

}
