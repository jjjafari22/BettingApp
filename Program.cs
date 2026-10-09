using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using BettingApp.Components;
using BettingApp.Components.Account;
using BettingApp.Data;
using BettingApp.Hubs;
using BettingApp.Services;
using Microsoft.AspNetCore.DataProtection;
using System.IO;
using System.Runtime.InteropServices;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);


// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// --- FIX 1: Keep User Circuit Alive for 30 Minutes ---
// This prevents the "Refresh" crash if the phone sleeps for >3 minutes.
builder.Services.Configure<Microsoft.AspNetCore.Components.Server.CircuitOptions>(options =>
{
    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(30);
    options.DetailedErrors = true; 
});
// -----------------------------------------------------

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
builder.Services.AddHealthChecks();

// --- FIX 2: Relax SignalR Timeouts for Mobile Data ---
builder.Services.AddSignalR(hubOptions =>
{
    // Wait longer before deciding the client is truly gone (helps in tunnels/elevators)
    hubOptions.ClientTimeoutInterval = TimeSpan.FromMinutes(2);
    // Ping slightly more often to keep the connection alive through proxies
    hubOptions.KeepAliveInterval = TimeSpan.FromSeconds(10);
});
// -----------------------------------------------------

builder.Services.AddScoped<SettlementService>();
builder.Services.AddScoped<DialogService>();
Func<HttpMessageHandler> compressedHandler = () => new HttpClientHandler
{
    AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate | System.Net.DecompressionMethods.Brotli
};

builder.Services.AddHttpClient<OddsApiService>().ConfigurePrimaryHttpMessageHandler(compressedHandler);
builder.Services.AddHttpClient<KambiScraperService>().ConfigurePrimaryHttpMessageHandler(compressedHandler);
builder.Services.AddHttpClient<Bet365ScraperService>().ConfigurePrimaryHttpMessageHandler(compressedHandler);
builder.Services.AddHttpClient<AiVisionService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(300); // 5 minutes for LLM processing
}).ConfigurePrimaryHttpMessageHandler(compressedHandler);
builder.Services.AddHttpClient<FotMobScraperService>().ConfigurePrimaryHttpMessageHandler(compressedHandler);

builder.Services.AddSingleton<MarketMappingService>();
builder.Services.AddSingleton<TeamAliasMappingService>();
builder.Services.AddHostedService<SettlementBackgroundService>();
builder.Services.AddHostedService<PendingBetsNotificationService>();
builder.Services.AddHostedService<BetMonitoringService>();

if (!builder.Environment.IsDevelopment())
{
    builder.Services.AddHostedService<OddsCacheWarmupService>();
}

// Register Discord Service
builder.Services.AddSingleton<DiscordNotificationService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<DiscordNotificationService>());

// Register Azure Blob Storage
builder.Services.AddSingleton(x => new BlobServiceClient(builder.Configuration["AzureStorage:ConnectionString"]));

// --- Azure Forwarded Headers ---
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

// --- Data Protection ---
var dataProtectionPath = Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aspnet", "DataProtection-Keys");

if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HOME")))
{
    dataProtectionPath = Path.Combine(Environment.GetEnvironmentVariable("HOME")!, "ASP.NET", "DataProtection-Keys");
}

Directory.CreateDirectory(dataProtectionPath);

var dataProtectionBuilder = builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath));

if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
{
    dataProtectionBuilder.ProtectKeysWithDpapi();
}

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityUserAccessor>();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // Allows Localhost/LAN login
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
    options.SlidingExpiration = true;
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString, sqlOptions => sqlOptions.EnableRetryOnFailure()));

builder.Services.AddScoped<ApplicationDbContext>(p => 
    p.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext());

builder.Services.AddIdentityCore<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = true)
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders()
    .AddErrorDescriber<CustomIdentityErrorDescriber>();

builder.Services.AddTransient<IEmailSender<ApplicationUser>, EmailSender>();

var app = builder.Build();

app.UseForwardedHeaders(); 

// --- Global Date/Time Formatting ---
var customCulture = new System.Globalization.CultureInfo("en-GB");
customCulture.DateTimeFormat.ShortDatePattern = "dd.MMM";
customCulture.DateTimeFormat.ShortTimePattern = "HH:mm";
var supportedCultures = new[] { customCulture };

app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture(customCulture),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures
});

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStaticFiles();
app.MapStaticAssets();

// --- FIX: Prevent Android Chrome from displaying "Showing offline" cached copies
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        // Only apply to HTML documents (Blazor pages) to prevent caching
        if (context.Response.ContentType?.StartsWith("text/html") == true)
        {
            context.Response.Headers.Append("Cache-Control", "no-store, no-cache, must-revalidate, max-age=0");
            context.Response.Headers.Append("Pragma", "no-cache");
            context.Response.Headers.Append("Expires", "0");
        }
        return Task.CompletedTask;
    });
    await next();
});
// --------------------------------------------------------------------------

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapHub<BetHub>("/bethub");
app.MapAdditionalIdentityEndpoints();
app.MapHealthChecks("/health");

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        context.Database.Migrate(); 
        
        // --- One-Time Data Migration for BetLegs ---
        try
        {
            // If we have Bets with AiVisionResultJson but NO BetLegs, we need to migrate them
            bool needsMigration = !context.BetLegs.Any() && context.Bets.Any(b => b.AiVisionResultJson != null);
            if (needsMigration)
            {
                var logger = services.GetRequiredService<ILogger<Program>>();
                logger.LogInformation("Starting one-time data migration for BetLegs...");
                
                var betsToMigrate = context.Bets.Where(b => b.AiVisionResultJson != null).ToList();
                int count = 0;
                foreach(var bet in betsToMigrate)
                {
                    try 
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(bet.AiVisionResultJson);
                        var root = doc.RootElement;
                        
                        bool isLive = root.TryGetProperty("isLive", out var l) && l.GetBoolean();
                        bool isBb = root.TryGetProperty("isBetBuilder", out var bb) && bb.GetBoolean();
                        string bookmaker = root.TryGetProperty("bookmaker", out var bk) ? bk.GetString() : "";

                        bet.IsLive = isLive;
                        bet.IsBetBuilder = isBb;
                        bet.Bookmaker = bookmaker ?? "";

                        if (root.TryGetProperty("legs", out var legs) && legs.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            foreach (var leg in legs.EnumerateArray())
                            {
                                string match = leg.TryGetProperty("match", out var m) ? m.GetString() : "";
                                string sport = leg.TryGetProperty("sport", out var sp) ? sp.GetString() : "";
                                string market = leg.TryGetProperty("market", out var mk) ? mk.GetString() : "";
                                string selection = leg.TryGetProperty("selection", out var sl) ? sl.GetString() : "";
                                string odds = leg.TryGetProperty("odds", out var o) ? (o.ValueKind == System.Text.Json.JsonValueKind.String ? o.GetString() : o.GetRawText()) : "";
                                
                                DateTime? startTime = null;
                                if (leg.TryGetProperty("startTime", out var st) && st.ValueKind == System.Text.Json.JsonValueKind.String)
                                {
                                    if (st.TryGetDateTime(out var parsedTime)) startTime = parsedTime;
                                }

                                string mappedOutcome = "Pending";
                                string mappedSource = "Unknown";
                                string mappedStats = "";
                                
                                if (!string.IsNullOrEmpty(bet.AiOutcomeResult))
                                {
                                    try 
                                    {
                                        using var outcomeDoc = System.Text.Json.JsonDocument.Parse(bet.AiOutcomeResult);
                                        if (outcomeDoc.RootElement.TryGetProperty("legs", out var outLegs) && outLegs.ValueKind == System.Text.Json.JsonValueKind.Array)
                                        {
                                            foreach (var oLeg in outLegs.EnumerateArray())
                                            {
                                                if (oLeg.TryGetProperty("match", out var oMatch) && string.Equals(oMatch.GetString(), match, StringComparison.OrdinalIgnoreCase))
                                                {
                                                    if (oLeg.TryGetProperty("outcome", out var oc)) mappedOutcome = oc.GetString() ?? "Pending";
                                                    if (oLeg.TryGetProperty("verificationSource", out var vs)) mappedSource = vs.GetString() ?? "Unknown";
                                                    if (oLeg.TryGetProperty("stats", out var ost)) mappedStats = ost.GetString() ?? "";
                                                    break;
                                                }
                                            }
                                        }
                                    } catch { }
                                }

                                context.BetLegs.Add(new BettingApp.Data.BetLeg
                                {
                                    BetId = bet.Id,
                                    Match = match ?? "",
                                    Sport = sport ?? "",
                                    Market = market ?? "",
                                    Selection = selection ?? "",
                                    Odds = odds ?? "",
                                    StartTime = startTime,
                                    Outcome = string.IsNullOrEmpty(mappedOutcome) ? "Pending" : mappedOutcome,
                                    VerificationSource = string.IsNullOrEmpty(mappedSource) ? "Unknown" : mappedSource,
                                    Stats = mappedStats ?? ""
                                });
                            }
                        }
                        count++;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, $"Failed to migrate Bet {bet.Id}");
                    }
                }
                
                context.SaveChanges();
                logger.LogInformation($"Successfully migrated data for {count} historical bets.");
            }
        }
        catch (Exception ex)
        {
            var logger = services.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "An error occurred during one-time BetLeg data migration.");
        }
        // ---------------------------------------------
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while migrating the database.");
    }
}

app.Run();