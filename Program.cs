using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using BettingApp.Components;
using BettingApp.Components.Account;
using BettingApp.Data;
using BettingApp.Hubs;
using Microsoft.AspNetCore.DataProtection;
using System.IO;
using System.Runtime.InteropServices;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpClient();
builder.Services.AddSignalR();

// --- UNIVERSAL DATA PROTECTION (Works on Azure, Linux, Mac, & Windows) ---
// This ensures keys are persisted to a folder so users don't get logged out on restart.
var dataProtectionPath = Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aspnet", "DataProtection-Keys");

// On Windows Azure, use the specific HOME environment variable if available
if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HOME")))
{
    dataProtectionPath = Path.Combine(Environment.GetEnvironmentVariable("HOME")!, "ASP.NET", "DataProtection-Keys");
}

// Ensure the directory exists
Directory.CreateDirectory(dataProtectionPath);

// Configure Data Protection to persist keys
var dataProtectionBuilder = builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath));

// On Windows, protect keys with DPAPI (standard practice)
if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
{
    dataProtectionBuilder.ProtectKeysWithDpapi();
}
// -------------------------------------------------------------------------

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

// --- CRITICAL FIX FOR PWA / HOME SCREEN APPS ---
builder.Services.ConfigureApplicationCookie(options =>
{
    // Lax is required for OAuth and some PWA scenarios on iOS/Android
    options.Cookie.SameSite = SameSiteMode.Lax;
    // Ensure cookies are only sent over HTTPS
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    // Ensure the cookie persists even if the app is closed (aligns with 'isPersistent: true' in login)
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
    options.SlidingExpiration = true;
});
// -----------------------------------------------

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));

builder.Services.AddScoped<ApplicationDbContext>(p => 
    p.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext());

builder.Services.AddIdentityCore<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = true)
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.MapStaticAssets();

// --- CRITICAL MIDDLEWARE ORDER ---
app.UseAuthentication(); // Must be before Antiforgery
app.UseAuthorization();  // Must be before Antiforgery
app.UseAntiforgery();
// --------------------------------

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