using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace BettingApp.Services
{
    /// <summary>
    /// Dedicated service for resolving missing match names on betting slips
    /// when the slip image is cropped or only shows player prop selections.
    /// Completely isolated from FotMobScraperService to avoid impacting bet settlement.
    /// </summary>
    public class PlayerMatchResolverService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<PlayerMatchResolverService> _logger;
        private readonly IMemoryCache? _cache;

        public PlayerMatchResolverService(HttpClient httpClient, ILogger<PlayerMatchResolverService> logger, IMemoryCache? cache = null)
        {
            _httpClient = httpClient;
            _logger = logger;
            _cache = cache;
            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            }
        }

        /// <summary>
        /// Extracts player name from a selection string and resolves the most appropriate match
        /// (Live > Soonest Upcoming > Recently Finished within 24h).
        /// Handles both Club and National Team fixtures seamlessly.
        /// </summary>
        public async Task<string?> ResolvePlayerMatchAsync(string selection, DateTime? betPlacedAt, int? betId = null, System.Threading.CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(selection)) return null;
            string betLabel = betId.HasValue ? $"[Bet #{betId.Value}] [Auto-Read] " : "[Auto-Read] ";

            try
            {
                // 1. Extract clean player name from selection
                string splitStr = selection.Split(new[] { " - ", ": ", ":", " to " }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                string playerName = Regex.Match(splitStr, @"^([\p{L}\s\-\'\.]+)").Groups[1].Value.Trim();

                if (string.IsNullOrWhiteSpace(playerName) || playerName.Length < 3)
                {
                    return null;
                }

                DateTime targetDate = betPlacedAt ?? DateTime.UtcNow;
                string normPlayerKey = TeamAliasMappingService.RemoveDiacritics(playerName).ToLowerInvariant();
                string cacheKey = $"player_match_{normPlayerKey}_{targetDate:yyyyMMdd_HH}";

                if (_cache != null && _cache.TryGetValue(cacheKey, out string? cachedMatch) && !string.IsNullOrEmpty(cachedMatch))
                {
                    _logger.LogInformation($"{betLabel}PlayerMatchResolver: Resolved player '{playerName}' from cache to '{cachedMatch}'");
                    return cachedMatch;
                }

                string? CacheAndReturn(string? matchResult)
                {
                    if (!string.IsNullOrEmpty(matchResult) && _cache != null)
                    {
                        _cache.Set(cacheKey, matchResult, TimeSpan.FromMinutes(20));
                    }
                    return matchResult;
                }

                // 2. Query FotMob suggest API to find player ID
                string? playerId = null;
                int? playerTeamId = null;
                double bestScore = -1;

                string searchUrl = $"https://apigw.fotmob.com/searchapi/suggest?term={Uri.EscapeDataString(playerName)}";
                var suggestResponse = await _httpClient.GetAsync(searchUrl, cancellationToken);
                if (!suggestResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning($"{betLabel}PlayerMatchResolver: Suggest API failed for '{playerName}' with status {suggestResponse.StatusCode}");
                    return null;
                }

                string suggestJson = await suggestResponse.Content.ReadAsStringAsync(cancellationToken);
                using (var suggestDoc = JsonDocument.Parse(suggestJson))
                {
                    if (suggestDoc.RootElement.TryGetProperty("squadMemberSuggest", out var playerSuggests) && playerSuggests.GetArrayLength() > 0)
                    {
                        var options = playerSuggests[0].GetProperty("options");
                        if (options.GetArrayLength() > 0)
                        {
                            var topOpt = options[0];
                            bestScore = topOpt.TryGetProperty("score", out var scProp) && scProp.ValueKind == JsonValueKind.Number ? scProp.GetDouble() : 0;
                            if (topOpt.TryGetProperty("payload", out var payload))
                            {
                                playerId = payload.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                                if (payload.TryGetProperty("teamId", out var tidProp) && tidProp.ValueKind == JsonValueKind.Number)
                                {
                                    playerTeamId = tidProp.GetInt32();
                                }
                            }
                        }
                    }
                }

                // 2b. Nickname / Shortened First-Name Disambiguation (e.g. 'Nicolas Paz' on slip vs 'Nico Paz' on FotMob):
                // FotMob's suggest score is (300000 + popularity). If a full-name query returned no player or an obscure low-popularity player (< 300030),
                // do a fast (~70ms) surname lookup to see if a much higher-popularity player with the same surname & first-name prefix exists.
                var nameParts = normPlayerKey.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (nameParts.Length >= 2 && bestScore < 300030)
                {
                    string firstToken = nameParts[0];
                    string lastToken = nameParts[^1];
                    if (firstToken.Length >= 3 && lastToken.Length >= 3)
                    {
                        string firstPrefix = firstToken.Substring(0, 3);
                        string surnameUrl = $"https://apigw.fotmob.com/searchapi/suggest?term={Uri.EscapeDataString(lastToken)}";
                        var surnameResp = await _httpClient.GetAsync(surnameUrl, cancellationToken);
                        if (surnameResp.IsSuccessStatusCode)
                        {
                            string surnameJson = await surnameResp.Content.ReadAsStringAsync(cancellationToken);
                            using var surnameDoc = JsonDocument.Parse(surnameJson);
                            if (surnameDoc.RootElement.TryGetProperty("squadMemberSuggest", out var sSuggests) && sSuggests.GetArrayLength() > 0)
                            {
                                var sOptions = sSuggests[0].GetProperty("options");
                                foreach (var opt in sOptions.EnumerateArray())
                                {
                                    double optScore = opt.TryGetProperty("score", out var sc) && sc.ValueKind == JsonValueKind.Number ? sc.GetDouble() : 0;
                                    if (optScore <= bestScore) continue;

                                    string rawText = opt.TryGetProperty("text", out var txt) ? txt.GetString() ?? "" : "";
                                    string displayName = rawText.Split('|')[0].Trim();
                                    var candParts = TeamAliasMappingService.RemoveDiacritics(displayName).ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                    if (candParts.Length >= 2)
                                    {
                                        string candFirst = candParts[0];
                                        string candLast = candParts[^1];
                                        if (candLast == lastToken && candFirst.StartsWith(firstPrefix, StringComparison.OrdinalIgnoreCase))
                                        {
                                            if (opt.TryGetProperty("payload", out var optPayload))
                                            {
                                                string? candId = optPayload.TryGetProperty("id", out var cidProp) ? cidProp.GetString() : null;
                                                if (!string.IsNullOrEmpty(candId))
                                                {
                                                    bestScore = optScore;
                                                    playerId = candId;
                                                    playerTeamId = optPayload.TryGetProperty("teamId", out var ctidProp) && ctidProp.ValueKind == JsonValueKind.Number ? ctidProp.GetInt32() : null;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                if (string.IsNullOrEmpty(playerId))
                {
                    _logger.LogInformation($"{betLabel}PlayerMatchResolver: No player found for '{playerName}'");
                    return null;
                }

                // 3. Fetch Player Overview page on FotMob
                string playerUrl = $"https://www.fotmob.com/players/{playerId}/overview";
                var playerResponse = await _httpClient.GetAsync(playerUrl, cancellationToken);
                if (!playerResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning($"{betLabel}PlayerMatchResolver: Failed to fetch player profile for ID {playerId} ({playerName})");
                    return null;
                }

                string playerHtml = await playerResponse.Content.ReadAsStringAsync(cancellationToken);
                var nextDataMatch = Regex.Match(playerHtml, @"<script id=""__NEXT_DATA__"" type=""application/json"">(.*?)</script>");
                if (!nextDataMatch.Success)
                {
                    return null;
                }

                using var playerDoc = JsonDocument.Parse(nextDataMatch.Groups[1].Value);
                var fallback = playerDoc.RootElement.GetProperty("props").GetProperty("pageProps").GetProperty("fallback");

                if (!fallback.TryGetProperty($"player:{playerId}", out var playerData))
                {
                    return null;
                }

                // Candidate matches to evaluate
                string? upcomingMatch = null;
                DateTime upcomingDate = DateTime.MaxValue;

                // 4. Check player's direct nextMatch (handles both club and national team!)
                if (playerData.TryGetProperty("nextMatch", out var nm) && nm.ValueKind == JsonValueKind.Object)
                {
                    string hName = nm.TryGetProperty("homeName", out var h) ? h.GetString() ?? "" : "";
                    string aName = nm.TryGetProperty("awayName", out var a) ? a.GetString() ?? "" : "";
                    string matchName = $"{hName} vs {aName}".Trim();

                    DateTime? fixtureDate = null;
                    if (nm.TryGetProperty("status", out var s) && s.TryGetProperty("utcTime", out var u) && DateTime.TryParse(u.GetString(), out DateTime parsedUtc))
                    {
                        fixtureDate = parsedUtc;
                    }
                    else if (nm.TryGetProperty("matchDate", out var md) && DateTime.TryParse(md.GetString(), out DateTime parsedMd))
                    {
                        fixtureDate = parsedMd;
                    }

                    bool started = nm.TryGetProperty("status", out var st) && st.TryGetProperty("started", out var stStarted) && stStarted.GetBoolean();
                    bool finished = nm.TryGetProperty("status", out var st2) && st2.TryGetProperty("finished", out var stFin) && stFin.GetBoolean();

                    if (!string.IsNullOrEmpty(hName) && !string.IsNullOrEmpty(aName))
                    {
                        // Priority 1: Match is currently Live / In-Progress!
                        if (started && !finished)
                        {
                            _logger.LogInformation($"{betLabel}PlayerMatchResolver: Resolved player '{playerName}' to LIVE nextMatch '{matchName}'");
                            return CacheAndReturn(matchName);
                        }

                        // Priority 2: Upcoming match
                        if (fixtureDate.HasValue)
                        {
                            // If kickoff is within 7 days in the future (or started in last 15 mins)
                            if (fixtureDate.Value >= targetDate.AddMinutes(-15) && fixtureDate.Value <= targetDate.AddDays(7))
                            {
                                upcomingMatch = matchName;
                                upcomingDate = fixtureDate.Value;
                            }
                        }
                    }
                }

                // 5. Check player's recentMatches (for live match or recently finished match from today)
                if (playerData.TryGetProperty("recentMatches", out var rm) && rm.ValueKind == JsonValueKind.Array && rm.GetArrayLength() > 0)
                {
                    var latestMatch = rm[0];
                    string t = latestMatch.TryGetProperty("teamName", out var tProp) ? tProp.GetString() ?? "" : "";
                    string opp = latestMatch.TryGetProperty("opponentTeamName", out var oProp) ? oProp.GetString() ?? "" : "";
                    bool isHome = latestMatch.TryGetProperty("isHomeTeam", out var ihProp) && ihProp.GetBoolean();
                    string recentMatchName = isHome ? $"{t} vs {opp}" : $"{opp} vs {t}";

                    if (latestMatch.TryGetProperty("matchDate", out var rMd) && rMd.TryGetProperty("utcTime", out var rUt) && DateTime.TryParse(rUt.GetString(), out DateTime recentUtc))
                    {
                        TimeSpan timeSinceKickoff = targetDate - recentUtc;

                        // Check if this recent match was ongoing when bet was placed / uploaded (within 120 mins)
                        if (timeSinceKickoff >= TimeSpan.Zero && timeSinceKickoff <= TimeSpan.FromMinutes(120))
                        {
                            _logger.LogInformation($"{betLabel}PlayerMatchResolver: Resolved player '{playerName}' to ongoing recentMatch '{recentMatchName}'");
                            return CacheAndReturn(recentMatchName);
                        }

                        // Or if the bet was uploaded retrospectively for a match that finished within the last 24 hours
                        // and there is no upcoming match starting within 48 hours
                        if (timeSinceKickoff >= TimeSpan.Zero && timeSinceKickoff <= TimeSpan.FromHours(24))
                        {
                            if (upcomingDate > targetDate.AddHours(48))
                            {
                                _logger.LogInformation($"{betLabel}PlayerMatchResolver: Resolved player '{playerName}' to recently finished match '{recentMatchName}' (finished {timeSinceKickoff.TotalHours:F1}h ago)");
                                return CacheAndReturn(recentMatchName);
                            }
                        }
                    }
                }

                // If we found an upcoming match, return it
                if (!string.IsNullOrEmpty(upcomingMatch))
                {
                    _logger.LogInformation($"{betLabel}PlayerMatchResolver: Resolved player '{playerName}' to soonest upcoming match '{upcomingMatch}' (kickoff: {upcomingDate:u})");
                    return CacheAndReturn(upcomingMatch);
                }

                // 6. Fallback: If player profile nextMatch was empty, check team fixture calendar
                if (playerTeamId.HasValue)
                {
                    string teamId = playerTeamId.Value.ToString();
                    string? teamFallbackMatch = await ResolveFromTeamFixturesAsync(teamId, targetDate, betLabel, playerName, cancellationToken);
                    if (!string.IsNullOrEmpty(teamFallbackMatch))
                    {
                        return CacheAndReturn(teamFallbackMatch);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning($"{betLabel}PlayerMatchResolver: Timed out while resolving player match for '{selection}'");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError($"{betLabel}PlayerMatchResolver: Exception resolving player match for '{selection}': {ex.Message}");
            }

            return null;
        }

        private async Task<string?> ResolveFromTeamFixturesAsync(string teamId, DateTime targetDate, string betLabel, string playerName, System.Threading.CancellationToken cancellationToken = default)
        {
            try
            {
                string teamHtml = await _httpClient.GetStringAsync($"https://www.fotmob.com/teams/{teamId}/overview/team", cancellationToken);
                var regex = Regex.Match(teamHtml, @"<script id=""__NEXT_DATA__"" type=""application/json"">(.*?)</script>");
                if (!regex.Success) return null;

                using var teamDoc = JsonDocument.Parse(regex.Groups[1].Value);
                var fallback = teamDoc.RootElement.GetProperty("props").GetProperty("pageProps").GetProperty("fallback");
                var teamData = fallback.GetProperty($"team-{teamId}");
                var fixturesProp = teamData.GetProperty("fixtures");

                if (fixturesProp.ValueKind == JsonValueKind.Object &&
                    fixturesProp.TryGetProperty("allFixtures", out var allFixtures) &&
                    allFixtures.TryGetProperty("fixtures", out var fixtures))
                {
                    string? bestUpcomingMatch = null;
                    DateTime earliestKickoff = DateTime.MaxValue;

                    foreach (var f in fixtures.EnumerateArray())
                    {
                        string homeName = f.TryGetProperty("home", out var h) && h.TryGetProperty("name", out var hn) ? hn.GetString() ?? "" : "";
                        string awayName = f.TryGetProperty("away", out var a) && a.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "";
                        string matchName = $"{homeName} vs {awayName}".Trim();

                        if (f.TryGetProperty("status", out var s) && s.TryGetProperty("utcTime", out var u) && DateTime.TryParse(u.GetString(), out DateTime fixtureDate))
                        {
                            bool started = s.TryGetProperty("started", out var st) && st.GetBoolean();
                            bool finished = s.TryGetProperty("finished", out var fin) && fin.GetBoolean();

                            // Live match
                            if (started && !finished)
                            {
                                _logger.LogInformation($"{betLabel}PlayerMatchResolver: Resolved player '{playerName}' via team fixtures to LIVE match '{matchName}'");
                                return matchName;
                            }

                            // Upcoming match (kickoff in the future within 7 days)
                            if (fixtureDate >= targetDate.AddMinutes(-15) && fixtureDate <= targetDate.AddDays(7))
                            {
                                if (fixtureDate < earliestKickoff)
                                {
                                    earliestKickoff = fixtureDate;
                                    bestUpcomingMatch = matchName;
                                }
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(bestUpcomingMatch))
                    {
                        _logger.LogInformation($"{betLabel}PlayerMatchResolver: Resolved player '{playerName}' via team fixtures to upcoming match '{bestUpcomingMatch}' (kickoff: {earliestKickoff:u})");
                        return bestUpcomingMatch;
                    }
                }
            }
            catch { }

            return null;
        }
    }
}
