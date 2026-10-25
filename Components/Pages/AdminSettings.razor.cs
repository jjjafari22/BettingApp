#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BettingApp.Data;
using BettingApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace BettingApp.Components.Pages;

public partial class AdminSettings : ComponentBase, IDisposable
{
    [Inject] private IDbContextFactory<ApplicationDbContext> DbFactory { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthStateProvider { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private UiNotificationService UiNotifier { get; set; } = default!;
    [Inject] private DiscordNotificationService DiscordService { get; set; } = default!;
    [Inject] private DialogService DialogService { get; set; } = default!;
    [Inject] private AutoApprovalService AutoApproval { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private SystemSetting settings = new();
    private bool hasAccess = false;
    private bool isLoading = true;
    private bool showSuccess = false;
    private bool showDiscordSuccess = false;
    private bool showPwaSuccess = false;
    private string discordMessage = "";
    private bool isSorting = false;
    private string? currentAdminName;
    private bool openInNativeApp = false;
    private string pwaPackageName = "";
    private string pwaPackagePinnacle = "";
    private string pwaPackageBet365 = "";
    private string pwaPackageBetsson = "";
    private string pwaPackageCoolbet = "";

    private bool _isDisposed;
    private Guid autoApprovalToggleKey = Guid.NewGuid();

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var authState = await AuthStateProvider.GetAuthenticationStateAsync();
            var user = authState.User;
            currentAdminName = user.Identity?.Name;

            using var context = DbFactory.CreateDbContext();
            if (user.Identity?.IsAuthenticated == true)
            {
                var dbUser = await context.Users.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.UserName == user.Identity.Name);
                hasAccess = dbUser?.IsAdmin ?? false;
            }

            if (!hasAccess)
            {
                Nav.NavigateTo("/");
                return;
            }

            await LoadSettings(context);
            UiNotifier.OnAdminNotification += HandleAdminNotification;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error initializing AdminSettings: {ex.Message}");
        }
        finally
        {
            isLoading = false;
        }
    }

    private void HandleAdminNotification(string? message)
    {
        if (_isDisposed) return;
        if (message != null && message.Contains("Settings"))
        {
            try
            {
                _ = InvokeAsync(async () =>
                {
                    try
                    {
                        if (_isDisposed) return;
                        using var ctx = DbFactory.CreateDbContext();
                        await LoadSettings(ctx);
                        StateHasChanged();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error reloading admin settings: {ex.Message}");
                    }
                });
            }
            catch { }
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            try
            {
                var value = await JS.InvokeAsync<string>("localStorage.getItem", "openInNativeApp");
                if (!string.IsNullOrEmpty(value))
                {
                    openInNativeApp = value == "true";
                }
                var pkg = await JS.InvokeAsync<string>("localStorage.getItem", "unibetPwaPackageName");
                if (!string.IsNullOrEmpty(pkg)) pwaPackageName = pkg;

                var pkgPin = await JS.InvokeAsync<string>("localStorage.getItem", "pinnaclePwaPackageName");
                if (!string.IsNullOrEmpty(pkgPin)) pwaPackagePinnacle = pkgPin;

                var pkgBetsson = await JS.InvokeAsync<string>("localStorage.getItem", "betssonPwaPackageName");
                if (!string.IsNullOrEmpty(pkgBetsson)) pwaPackageBetsson = pkgBetsson;

                var pkgBet365 = await JS.InvokeAsync<string>("localStorage.getItem", "bet365PwaPackageName");
                if (!string.IsNullOrEmpty(pkgBet365)) pwaPackageBet365 = pkgBet365;

                var pkgCoolbet = await JS.InvokeAsync<string>("localStorage.getItem", "coolbetPwaPackageName");
                if (!string.IsNullOrEmpty(pkgCoolbet)) pwaPackageCoolbet = pkgCoolbet;

                if (!string.IsNullOrEmpty(value) || !string.IsNullOrEmpty(pkg) || !string.IsNullOrEmpty(pkgPin))
                {
                    StateHasChanged();
                }
            }
            catch { }
        }
    }

    private async Task ToggleOpenNativeApp(ChangeEventArgs e)
    {
        openInNativeApp = (bool)(e.Value ?? false);
        showPwaSuccess = false;
        try
        {
            await JS.InvokeVoidAsync("localStorage.setItem", "openInNativeApp", openInNativeApp.ToString().ToLower());
            showPwaSuccess = true;
        }
        catch { }
    }

    private async Task SavePwaPackageName(string key, string value)
    {
        showPwaSuccess = false;
        try
        {
            await JS.InvokeVoidAsync("localStorage.setItem", key, value.Trim());
            showPwaSuccess = true;
            StateHasChanged();
        }
        catch { }
    }

    private async Task LoadSettings(ApplicationDbContext context)
    {
        var existingSettings = await context.Settings.OrderBy(s => s.Id).FirstOrDefaultAsync();
        if (existingSettings == null)
        {
            settings = new SystemSetting { MinBetAmount = 100m };
            context.Settings.Add(settings);
            await context.SaveChangesAsync();
        }
        else
        {
            settings = existingSettings;
        }
    }

    private async Task SaveSettings()
    {
        showSuccess = false;
        using var context = DbFactory.CreateDbContext();

        var dbSettings = await context.Settings.OrderBy(s => s.Id).FirstOrDefaultAsync();
        if (dbSettings != null)
        {
            var oldMinBet = dbSettings.MinBetAmount;
            bool confirmed = await DialogService.ConfirmAsync($"Are you sure you want to change Minimum Bet Amount from {oldMinBet:N0} NOK to {settings.MinBetAmount:N0} NOK?");
            if (!confirmed)
            {
                await LoadSettings(context);
                StateHasChanged();
                return;
            }

            dbSettings.MinBetAmount = settings.MinBetAmount;

            context.AuditLogs.Add(new AuditLog
            {
                AdminUserName = currentAdminName ?? "Unknown",
                Action = "Changed System Settings",
                TargetUserName = "System",
                Details = $"MinBet: {oldMinBet}->{settings.MinBetAmount}"
            });
            await context.SaveChangesAsync();

            UiNotifier.NotifyAdmin("System Settings Updated");
            showSuccess = true;
        }
    }

    private async Task ToggleGlobalAutoApproval(bool newValue)
    {
        showSuccess = false;
        bool confirmed = await DialogService.ConfirmAsync(
            $"Are you sure you want to {(newValue ? "ENABLE" : "DISABLE")} Auto-Approval globally?" +
            (!newValue ? "\n\nAny active Auto-Approval countdown timers on pending bets will be immediately cancelled." : ""));
        if (!confirmed)
        {
            using var reloadCtx = DbFactory.CreateDbContext();
            await LoadSettings(reloadCtx);
            autoApprovalToggleKey = Guid.NewGuid();
            StateHasChanged();
            return;
        }

        using var context = DbFactory.CreateDbContext();
        var dbSettings = await context.Settings.OrderBy(s => s.Id).FirstOrDefaultAsync();
        if (dbSettings != null)
        {
            var oldVal = dbSettings.IsAutoApprovalEnabled;
            dbSettings.IsAutoApprovalEnabled = newValue;
            settings.IsAutoApprovalEnabled = newValue;

            context.AuditLogs.Add(new AuditLog
            {
                AdminUserName = currentAdminName ?? "Unknown",
                Action = "Toggle Auto-Approval",
                TargetUserName = "System",
                Details = $"IsAutoApprovalEnabled: {oldVal} -> {newValue}"
            });
            await context.SaveChangesAsync();

            if (!newValue)
            {
                await AutoApproval.CancelAllActiveCountdownsAsync($"Auto-Approval disabled globally by {currentAdminName ?? "Admin"}");
            }

            UiNotifier.NotifyAdmin("System Settings Updated");
            showSuccess = true;
        }
    }

    private async Task SaveAutoApprovalSettings()
    {
        showSuccess = false;
        using var context = DbFactory.CreateDbContext();
        var dbSettings = await context.Settings.OrderBy(s => s.Id).FirstOrDefaultAsync();
        if (dbSettings == null) return;

        var changes = new List<string>();

        // High-Level Settings
        if (dbSettings.AutoApprovalDelaySeconds != settings.AutoApprovalDelaySeconds)
            changes.Add($"Countdown Timer: {dbSettings.AutoApprovalDelaySeconds}s -> {settings.AutoApprovalDelaySeconds}s");
        if (dbSettings.AutoApprovalAllowLiveBets != settings.AutoApprovalAllowLiveBets)
            changes.Add($"Allow Live Bets: {dbSettings.AutoApprovalAllowLiveBets} -> {settings.AutoApprovalAllowLiveBets}");

        // Tier 1: Pinnacle
        if (dbSettings.IsTier1PinnacleEnabled != settings.IsTier1PinnacleEnabled)
            changes.Add($"Tier 1 (Pinnacle) Enabled: {dbSettings.IsTier1PinnacleEnabled} -> {settings.IsTier1PinnacleEnabled}");
        if (dbSettings.AutoApprovalMaxStakeNOK != settings.AutoApprovalMaxStakeNOK)
            changes.Add($"Tier 1 Max Stake: {dbSettings.AutoApprovalMaxStakeNOK:N0} NOK -> {settings.AutoApprovalMaxStakeNOK:N0} NOK");
        if (dbSettings.AutoApprovalMaxNetLiabilityNOK != settings.AutoApprovalMaxNetLiabilityNOK)
            changes.Add($"Tier 1 Max Net Liability: {dbSettings.AutoApprovalMaxNetLiabilityNOK:N0} NOK -> {settings.AutoApprovalMaxNetLiabilityNOK:N0} NOK");
        if (dbSettings.AutoApprovalMaxOverPinnaclePercent != settings.AutoApprovalMaxOverPinnaclePercent)
            changes.Add($"Tier 1 Max Over Pinnacle: {dbSettings.AutoApprovalMaxOverPinnaclePercent:0.##}% -> {settings.AutoApprovalMaxOverPinnaclePercent:0.##}%");
        if (dbSettings.AutoApprovalMinPinnacleLimitEUR != settings.AutoApprovalMinPinnacleLimitEUR)
            changes.Add($"Tier 1 Min Pinnacle Limit: €{dbSettings.AutoApprovalMinPinnacleLimitEUR:N0} -> €{settings.AutoApprovalMinPinnacleLimitEUR:N0}");

        // Tier 2: Soft Bookies
        if (dbSettings.IsTier2SoftBookiesEnabled != settings.IsTier2SoftBookiesEnabled)
            changes.Add($"Tier 2 (Soft Bookies) Enabled: {dbSettings.IsTier2SoftBookiesEnabled} -> {settings.IsTier2SoftBookiesEnabled}");
        if (dbSettings.Tier2MaxStakeNOK != settings.Tier2MaxStakeNOK)
            changes.Add($"Tier 2 Max Stake: {dbSettings.Tier2MaxStakeNOK:N0} NOK -> {settings.Tier2MaxStakeNOK:N0} NOK");
        if (dbSettings.Tier2MaxNetLiabilityNOK != settings.Tier2MaxNetLiabilityNOK)
            changes.Add($"Tier 2 Max Net Liability: {dbSettings.Tier2MaxNetLiabilityNOK:N0} NOK -> {settings.Tier2MaxNetLiabilityNOK:N0} NOK");
        if (dbSettings.Tier2MaxOverBookieAvgPercent != settings.Tier2MaxOverBookieAvgPercent)
            changes.Add($"Tier 2 Max Over Bookie Avg: {dbSettings.Tier2MaxOverBookieAvgPercent:0.##}% -> {settings.Tier2MaxOverBookieAvgPercent:0.##}%");

        // Tier 3: Small Stake
        if (dbSettings.IsTier3SmallStakeEnabled != settings.IsTier3SmallStakeEnabled)
            changes.Add($"Tier 3 (Small Stake) Enabled: {dbSettings.IsTier3SmallStakeEnabled} -> {settings.IsTier3SmallStakeEnabled}");
        if (dbSettings.Tier3MaxStakeNOK != settings.Tier3MaxStakeNOK)
            changes.Add($"Tier 3 Max Stake: {dbSettings.Tier3MaxStakeNOK:N0} NOK -> {settings.Tier3MaxStakeNOK:N0} NOK");
        if (dbSettings.Tier3MaxNetLiabilityNOK != settings.Tier3MaxNetLiabilityNOK)
            changes.Add($"Tier 3 Max Net Liability: {dbSettings.Tier3MaxNetLiabilityNOK:N0} NOK -> {settings.Tier3MaxNetLiabilityNOK:N0} NOK");
        if (dbSettings.AutoApprovalAllowBetBuilders != settings.AutoApprovalAllowBetBuilders)
            changes.Add($"Tier 3 Allow Bet Builders: {dbSettings.AutoApprovalAllowBetBuilders} -> {settings.AutoApprovalAllowBetBuilders}");

        string confirmSummary = changes.Count > 0
            ? $"Are you sure you want to update Auto-Approval criteria?\n\n{string.Join("\n", changes)}"
            : "No Auto-Approval values were changed. Save anyway?";

        bool confirmed = await DialogService.ConfirmAsync(confirmSummary);
        if (!confirmed)
        {
            await LoadSettings(context);
            StateHasChanged();
            return;
        }

        if (settings.AutoApprovalDelaySeconds < 0) settings.AutoApprovalDelaySeconds = 0;
        if (settings.AutoApprovalMinPinnacleLimitEUR < 0) settings.AutoApprovalMinPinnacleLimitEUR = 0;
        if (settings.AutoApprovalMaxStakeNOK < 0) settings.AutoApprovalMaxStakeNOK = 0;
        if (settings.AutoApprovalMaxNetLiabilityNOK < 0) settings.AutoApprovalMaxNetLiabilityNOK = 0;
        if (settings.Tier2MaxStakeNOK < 0) settings.Tier2MaxStakeNOK = 0;
        if (settings.Tier2MaxNetLiabilityNOK < 0) settings.Tier2MaxNetLiabilityNOK = 0;
        if (settings.Tier3MaxStakeNOK < 0) settings.Tier3MaxStakeNOK = 0;
        if (settings.Tier3MaxNetLiabilityNOK < 0) settings.Tier3MaxNetLiabilityNOK = 0;

        dbSettings.AutoApprovalDelaySeconds = settings.AutoApprovalDelaySeconds;
        dbSettings.AutoApprovalAllowLiveBets = settings.AutoApprovalAllowLiveBets;
        dbSettings.AutoApprovalAllowBetBuilders = settings.AutoApprovalAllowBetBuilders;

        dbSettings.IsTier1PinnacleEnabled = settings.IsTier1PinnacleEnabled;
        dbSettings.AutoApprovalMaxOverPinnaclePercent = settings.AutoApprovalMaxOverPinnaclePercent;
        dbSettings.AutoApprovalMinPinnacleLimitEUR = settings.AutoApprovalMinPinnacleLimitEUR;
        dbSettings.AutoApprovalMaxStakeNOK = settings.AutoApprovalMaxStakeNOK;
        dbSettings.AutoApprovalMaxNetLiabilityNOK = settings.AutoApprovalMaxNetLiabilityNOK;

        dbSettings.IsTier2SoftBookiesEnabled = settings.IsTier2SoftBookiesEnabled;
        dbSettings.Tier2MaxOverBookieAvgPercent = settings.Tier2MaxOverBookieAvgPercent;
        dbSettings.Tier2MaxStakeNOK = settings.Tier2MaxStakeNOK;
        dbSettings.Tier2MaxNetLiabilityNOK = settings.Tier2MaxNetLiabilityNOK;

        dbSettings.IsTier3SmallStakeEnabled = settings.IsTier3SmallStakeEnabled;
        dbSettings.Tier3MaxStakeNOK = settings.Tier3MaxStakeNOK;
        dbSettings.Tier3MaxNetLiabilityNOK = settings.Tier3MaxNetLiabilityNOK;

        if (changes.Count > 0)
        {
            context.AuditLogs.Add(new AuditLog
            {
                AdminUserName = currentAdminName ?? "Unknown",
                Action = "Changed System Settings",
                TargetUserName = "System",
                Details = string.Join("; ", changes)
            });
        }

        await context.SaveChangesAsync();
        UiNotifier.NotifyAdmin("System Settings Updated");
        showSuccess = true;
    }

    private void RefreshDiscordCache()
    {
        DiscordService.InvalidateChannelCache();
        _ = Task.Run(async () =>
        {
            try
            {
                await DiscordService.GetGuildTextChannelsAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AdminSettings] Failed to refresh discord channel cache: {ex.Message}");
            }
        });
        discordMessage = "Discord cache refreshed successfully!";
        showDiscordSuccess = true;
    }

    private async Task SortDiscordChannels()
    {
        isSorting = true;
        StateHasChanged();

        try
        {
            discordMessage = await DiscordService.SortUserChannelsAlphabeticallyAsync();
            showDiscordSuccess = true;
        }
        catch (Exception ex)
        {
            discordMessage = $"Error sorting channels: {ex.Message}";
            showDiscordSuccess = true;
        }
        finally
        {
            isSorting = false;
        }
    }

    public void Dispose()
    {
        _isDisposed = true;
        UiNotifier.OnAdminNotification -= HandleAdminNotification;
    }
}
