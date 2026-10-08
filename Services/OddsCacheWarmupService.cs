using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;
using System;
using Microsoft.Extensions.DependencyInjection;

namespace BettingApp.Services
{
    public class OddsCacheWarmupService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<OddsCacheWarmupService> _logger;
        private readonly IHostApplicationLifetime _appLifetime;

        public OddsCacheWarmupService(IServiceProvider serviceProvider, ILogger<OddsCacheWarmupService> logger, IHostApplicationLifetime appLifetime)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _appLifetime = appLifetime;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("OddsCacheWarmupService is waiting for the application to fully start...");
            
            // 1. Wait gracefully until Kestrel (the web server) is fully up and running.
            // This strictly satisfies the AI's recommendation to not block Azure's health probe.
            var tcs = new TaskCompletionSource();
            using var registration = _appLifetime.ApplicationStarted.Register(() => tcs.TrySetResult());
            
            await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, stoppingToken));
            
            if (stoppingToken.IsCancellationRequested) return;

            _logger.LogInformation("Application started securely. Beginning OddsCacheWarmup scheduled loop.");

            // 2. Execute immediately, then loop every 5.5 hours using modern PeriodicTimer (prevents timer drift)
            using var timer = new PeriodicTimer(TimeSpan.FromHours(5.5));
            do
            {
                try
                {
                    _logger.LogInformation("Background Worker: Running automatic OddsPapi cache warmup...");
                    using var scope = _serviceProvider.CreateScope();
                    var oddsApi = scope.ServiceProvider.GetRequiredService<OddsApiService>();
                    
                    await oddsApi.WarmupCacheAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during background odds cache warmup.");
                }
            } 
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
    }
}
