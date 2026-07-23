using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using BettingApp.Data;
using System.Text.Json;
using BettingApp.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace BettingApp.Services
{
    public class BetMonitoringService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<BetMonitoringService> _logger;
        private readonly AiVisionService _aiVisionService;

        public BetMonitoringService(
            IServiceProvider serviceProvider, 
            ILogger<BetMonitoringService> logger,
            AiVisionService aiVisionService)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _aiVisionService = aiVisionService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("BetMonitoringService starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessDueBetsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in BetMonitoringService loop");
                }

                // Wait 5 minutes before the next check
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }

        private async Task ProcessDueBetsAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
            using var context = dbFactory.CreateDbContext();

            // Find active bets where NextCheckTime has passed
            var dueBets = await context.Bets
                .Where(b => b.Status == "Approved" && b.NextCheckTime.HasValue && b.NextCheckTime.Value <= DateTime.UtcNow)
                .ToListAsync(stoppingToken);

            foreach (var bet in dueBets)
            {
                if (stoppingToken.IsCancellationRequested) break;

                if (string.IsNullOrEmpty(bet.AiVisionResultJson)) continue;

                _logger.LogInformation($"Checking outcome for bet #{bet.Id}");
                string? result = await _aiVisionService.ConfirmOutcomeAsync(bet.AiVisionResultJson, bet.CreatedAt);
                
                // Refresh bet from DB in case it was modified
                var dbBet = await context.Bets.FindAsync(bet.Id);
                if (dbBet == null || dbBet.Status != "Approved") continue;

                dbBet.AiOutcomeResult = result;
                
                try 
                {
                    if (string.IsNullOrEmpty(result))
                    {
                        dbBet.NextCheckTime = DateTime.UtcNow.AddMinutes(60);
                    }
                    else
                    {
                        var doc = JsonDocument.Parse(result);
                        if (doc.RootElement.TryGetProperty("overallStatus", out var statusElement))
                        {
                            var status = statusElement.GetString();
                            if (status == "MATCH FINISHED - WON" || status == "MATCH FINISHED - LOST" || status == "MATCH FINISHED - VOID")
                            {
                                // Match is finished, stop checking
                                dbBet.NextCheckTime = null;
                            }
                            else 
                            {
                                // Match is still running or unknown, check again in 60 minutes
                                dbBet.NextCheckTime = DateTime.UtcNow.AddMinutes(60);
                            }
                        }
                    }
                }
                catch 
                {
                    // If AI fails to return valid JSON, try again in 60 mins
                    dbBet.NextCheckTime = DateTime.UtcNow.AddMinutes(60);
                }

                await context.SaveChangesAsync(stoppingToken);
                
                // Notify UI about the update
                var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<BetHub>>();
                await hubContext.Clients.Group("Admins").SendAsync("ReceiveAdminNotification", "Update");
            }
        }
    }
}
