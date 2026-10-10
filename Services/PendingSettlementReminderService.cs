using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using BettingApp.Data;

namespace BettingApp.Services
{
    public class PendingSettlementReminderService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<PendingSettlementReminderService> _logger;
        private readonly DiscordNotificationService _discordService;

        public PendingSettlementReminderService(
            IServiceProvider serviceProvider, 
            ILogger<PendingSettlementReminderService> logger,
            DiscordNotificationService discordService)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _discordService = discordService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("PendingSettlementReminderService starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckPendingSettlementsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in PendingSettlementReminderService loop");
                }

                // Wait 15 minutes before the next check
                await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
            }
        }

        private async Task CheckPendingSettlementsAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
            using var context = dbFactory.CreateDbContext();

            // A bet needs manual settlement if it's Approved, has no next AI check (meaning AI finished), 
            // and actually has an outcome result from the AI that isn't empty.
            var count = await context.Bets
                .Include(b => b.AiEvaluation)
                .Where(b => b.Status == "Approved" && 
                            b.NextCheckTime == null && 
                            b.AiEvaluation != null && 
                            b.AiEvaluation.AiOutcomeResult != null &&
                            b.AiEvaluation.AiOutcomeResult != "")
                .CountAsync(stoppingToken);

            if (count > 0)
            {
                _logger.LogInformation($"Found {count} pending settlements requiring admin review. Sending Discord notification.");
                await _discordService.NotifyAdminPendingSettlementsAsync(count);
            }
        }
    }
}
