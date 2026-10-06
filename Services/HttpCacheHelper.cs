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

        /// <summary>
        /// Attempts to get the value from cache. If not found, acquires a lock specific to the cache key,
        /// checks the cache again, and if still not found, executes the fetchFactory.
        /// Handles 429 rate limit retries automatically.
        /// </summary>
        private static readonly SemaphoreSlim _globalThrottle = new SemaphoreSlim(1, 1);

        public static async Task<string?> GetOrCreateAsync(
            IMemoryCache cache,
            string cacheKey,
            TimeSpan absoluteExpirationRelativeToNow,
            Func<Task<HttpResponseMessage>> fetchFactory,
            ILogger logger)
        {
            if (cache.TryGetValue(cacheKey, out string? cachedValue))
            {
                return cachedValue;
            }

            var myLock = _locks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
            
            await myLock.WaitAsync();
            try
            {
                // Double-check cache inside lock
                if (cache.TryGetValue(cacheKey, out cachedValue))
                {
                    return cachedValue;
                }

                logger.LogInformation($"OddsPapi: Fetching fresh data for {cacheKey} (Cache Miss)");

                await _globalThrottle.WaitAsync();
                try { await Task.Delay(600); } finally { _globalThrottle.Release(); }

                using var response = await fetchFactory();
                
                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        logger.LogWarning($"OddsPapi: 429 Too Many Requests for {cacheKey}. Retrying in 1.5s...");
                        await Task.Delay(1500);
                        
                        await _globalThrottle.WaitAsync();
                        try { await Task.Delay(600); } finally { _globalThrottle.Release(); }

                        using var retryResponse = await fetchFactory();
                        if (retryResponse.IsSuccessStatusCode)
                        {
                            cachedValue = await retryResponse.Content.ReadAsStringAsync();
                            cache.Set(cacheKey, cachedValue, absoluteExpirationRelativeToNow);
                            return cachedValue;
                        }
                        logger.LogError($"OddsPapi: Retry failed for {cacheKey} with status {retryResponse.StatusCode}");
                        return null;
                    }
                    else
                    {
                        logger.LogError($"OddsPapi: HTTP request failed for {cacheKey} with status {response.StatusCode}");
                        return null;
                    }
                }

                cachedValue = await response.Content.ReadAsStringAsync();
                cache.Set(cacheKey, cachedValue, absoluteExpirationRelativeToNow);
                return cachedValue;
            }
            finally
            {
                myLock.Release();
                // We could remove the lock from the dictionary, but it's small and prevents race conditions on removal.
            }
        }
    }
}
