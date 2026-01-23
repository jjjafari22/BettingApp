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
using Microsoft.AspNetCore.HttpOverrides; // NEW: Required for Azure

var builder = WebApplication.CreateBuilder(args);

// --- FIX 1: Force App to listen on Port 8080 (Azure Default) ---
// This ensures the app matches the port Azure expects.
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(8080);
});
// -------------------------------------------------------------

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpClient();
builder.Services.AddSignalR();
builder.Services.AddScoped<SettlementService>();
builder.Services.AddHostedService<SettlementBackgroundService>();

// --- NEW: Register the Pending Bets Reminder Service ---
builder.Services.AddHostedService<PendingBetsNotificationService>();
// -----------------------------------------------------

// Register Discord Service
builder.Services.AddSingleton<DiscordNotificationService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<DiscordNotificationService>());

// Register Azure Blob Storage
builder.Services.AddSingleton(x => new BlobServiceClient(builder.Configuration["AzureStorage:ConnectionString"]));

// --- FIX 2: Configure Forwarded Headers ---
// This tells the app "Trust the Azure Load Balancer when it says we are on HTTPS"
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});
// ----------------------------------------------------------------

// --- UNIVERSAL DATA PROTECTION ---
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
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
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

// --- CHANGED: Register the Real Email Service instead of the NoOp one ---
builder.Services.AddTransient<IEmailSender<ApplicationUser>, EmailSender>();
// -----------------------------------------------------------------------

var app = builder.Build();

// --- NEW: Apply Forwarded Headers immediately ---
app.UseForwardedHeaders(); 
// ------------------------------------------------

// --- GLOBAL DATE/TIME FORMATTING ---
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
        // Migrations run here. If the app "hangs" on startup, it is likely waiting for the DB.
        context.Database.Migrate(); 
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while migrating the database.");
    }
}

app.Run();