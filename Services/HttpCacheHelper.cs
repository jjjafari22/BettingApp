using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using BettingApp.Models;
using System.Collections.Concurrent;

namespace BettingApp.Services
{
    public static class HttpCacheHelper
    {
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
        private static readonly SemaphoreSlim _globalThrottle = new SemaphoreSlim(1, 1);
        private static DateTime _lastRequestStart = DateTime.MinValue;

        /// <summary>
        /// Attempts to get the value from cache. If not found, acquires a lock specific to the cache key,
        /// checks the cache again, and if still not found, executes the fetchFactory.
        /// Handles 429 rate limit retries automatically with exponential backoff.
        /// </summary>
        public static async Task<string?> GetOrCreateAsync(
            IMemoryCache cache,
            string cacheKey,
            TimeSpan absoluteExpirationRelativeToNow,
            Func<Task<HttpResponseMessage>> fetchFactory,
            ILogger logger)
        {
            if (absoluteExpirationRelativeToNow > TimeSpan.Zero && cache.TryGetValue(cacheKey, out string? cachedValue))
            {
                return cachedValue;
            }

            var myLock = _locks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
            
            await myLock.WaitAsync();
            try
            {
                // Double-check cache inside lock
                if (absoluteExpirationRelativeToNow > TimeSpan.Zero && cache.TryGetValue(cacheKey, out cachedValue))
                {
                    return cachedValue;
                }

                string durationStr = absoluteExpirationRelativeToNow.TotalHours >= 1 
                    ? $"{absoluteExpirationRelativeToNow.TotalHours}h" 
                    : $"{absoluteExpirationRelativeToNow.TotalSeconds}sec";
                logger.LogInformation($"OddsPapi: Fetching fresh data for {cacheKey} (Cache Miss - {durationStr})");

                int maxRetries = 3;
                
                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    await _globalThrottle.WaitAsync();
                    try 
                    {
                        var elapsed = DateTime.UtcNow - _lastRequestStart;
                        if (elapsed.TotalMilliseconds < 1050)
                        {
                            await Task.Delay(1050 - (int)elapsed.TotalMilliseconds);
                        }
                        
                        _lastRequestStart = DateTime.UtcNow;
                    }
                    finally
                    {
                        _globalThrottle.Release();
                    }

                    HttpResponseMessage response;
                    response = await fetchFactory();

                    using (response)
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            cachedValue = await response.Content.ReadAsStringAsync();
                            if (absoluteExpirationRelativeToNow > TimeSpan.Zero)
                            {
                                cache.Set(cacheKey, cachedValue, absoluteExpirationRelativeToNow);
                            }
                            return cachedValue;
                        }
                        else if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                        {
                            if (attempt == maxRetries)
                            {
                                logger.LogError($"OddsPapi: 429 Too Many Requests for {cacheKey}. Max retries reached.");
                                return null;
                            }
                            
                            logger.LogWarning($"OddsPapi: 429 Too Many Requests for {cacheKey}. Retrying... (Attempt {attempt} of {maxRetries - 1})");
                            await Task.Delay(500); // Small wait before re-queueing
                        }
                        else
                        {
                            logger.LogError($"OddsPapi: HTTP request failed for {cacheKey} with status {response.StatusCode}");
                            return null;
                        }
                    }
                }
                return null;
            }
            finally
            {
                myLock.Release();
            }
        }
    }
}
