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

// --- Azure Port Configuration ---
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(8080);
});

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

builder.Services.AddHttpClient();

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
builder.Services.AddHostedService<SettlementBackgroundService>();
builder.Services.AddHostedService<PendingBetsNotificationService>();

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
    options.UseSqlServer(connectionString));

builder.Services.AddScoped<ApplicationDbContext>(p => 
    p.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext());

builder.Services.AddIdentityCore<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = true)
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

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

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapHub<BetHub>("/bethub");
app.MapAdditionalIdentityEndpoints();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        context.Database.Migrate(); 
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while migrating the database.");
    }
}

app.Run();