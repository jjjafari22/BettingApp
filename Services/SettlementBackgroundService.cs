using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BettingApp.Services
{
    public class SettlementBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<SettlementBackgroundService> _logger;

        public SettlementBackgroundService(IServiceProvider serviceProvider, ILogger<SettlementBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTime.UtcNow; // Or convert to Norway time if needed
                
                // Norway is usually UTC+1 or UTC+2. Let's assume target is Norway.
                // We'll check every minute.
                var norwayTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
                var norwayTime = TimeZoneInfo.ConvertTimeFromUtc(now, norwayTimeZone);

                // Run every hour at xx:59
                if (norwayTime.Minute == 59)
                {
                    try
                    {
                        using var scope = _serviceProvider.CreateScope();
                        var settlementService = scope.ServiceProvider.GetRequiredService<SettlementService>();
                        _logger.LogInformation($"Running Scheduled Buddy Settlement Snapshot for {norwayTime:HH:mm}...");
                        await settlementService.CreateSnapshotAsync();
                        
                        // Wait 2 minutes to ensure we don't run it twice in the same minute window (shifts to xx:01)
                        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error running scheduled settlement.");
                    }
                }
                else
                {
                    // Check again in 30 seconds
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
            }
        }
    }
}