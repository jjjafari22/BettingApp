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
            ILogger logger,
            CancellationToken cancellationToken = default)
        {
            if (absoluteExpirationRelativeToNow > TimeSpan.Zero && cache.TryGetValue(cacheKey, out string? cachedValue))
            {
                return cachedValue;
            }

            var myLock = _locks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
            
            await myLock.WaitAsync(cancellationToken);
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
                    cancellationToken.ThrowIfCancellationRequested();
                    HttpResponseMessage response;
                    await _globalThrottle.WaitAsync(cancellationToken);
                    try 
                    {
                        var elapsed = DateTime.UtcNow - _lastRequestStart;
                        bool isColdConnection = elapsed.TotalSeconds > 60;
                        if (elapsed.TotalMilliseconds < 1100)
                        {
                            int waitMs = (int)Math.Ceiling(1100 - elapsed.TotalMilliseconds);
                            if (waitMs > 0)
                            {
                                await Task.Delay(waitMs, cancellationToken);
                            }
                        }
                        
                        var reqStart = DateTime.UtcNow;
                        _lastRequestStart = reqStart;
                        response = await fetchFactory();

                        var reqDurationMs = (DateTime.UtcNow - reqStart).TotalMilliseconds;
                        if (isColdConnection && reqDurationMs < 1100)
                        {
                            _lastRequestStart = reqStart.AddMilliseconds(Math.Min(reqDurationMs * 0.5, 400));
                        }
                    }
                    catch (HttpRequestException httpEx)
                    {
                        if (attempt == maxRetries)
                        {
                            logger.LogError(httpEx, $"OddsPapi: HTTP request exception for {cacheKey} after {maxRetries} attempts.");
                            return null;
                        }

                        logger.LogWarning($"OddsPapi: Transient network error for {cacheKey} ({httpEx.Message}). Retrying... (Attempt {attempt} of {maxRetries - 1})");
                        await Task.Delay(1000 * attempt, cancellationToken);
                        continue;
                    }
                    finally
                    {
                        _globalThrottle.Release();
                    }

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
                            await _globalThrottle.WaitAsync(cancellationToken);
                            try
                            {
                                var pushback = DateTime.UtcNow.AddMilliseconds(1500 * attempt);
                                if (pushback > _lastRequestStart)
                                {
                                    _lastRequestStart = pushback;
                                }
                            }
                            finally
                            {
                                _globalThrottle.Release();
                            }
                            await Task.Delay(1500 * attempt, cancellationToken);
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
