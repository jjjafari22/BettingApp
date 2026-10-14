using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace BettingApp.Services
{
    public class EspnApiService
    {
        private readonly HttpClient _httpClient;
        private readonly TeamAliasMappingService _teamAliasMapper;
        private readonly IMemoryCache _cache;
        private readonly ILogger<EspnApiService> _logger;

        private static readonly SemaphoreSlim _throttleLock = new(1, 1);
        private static DateTime _lastRequestTime = DateTime.MinValue;
        private const int MinRequestDelayMs = 2000; // 2.0s delay between calls to protect IP

        public EspnApiService(
            HttpClient httpClient,
            TeamAliasMappingService teamAliasMapper,
            IMemoryCache cache,
            ILogger<EspnApiService> logger)
        {
            _httpClient = httpClient;
            _teamAliasMapper = teamAliasMapper;
            _cache = cache;
            _logger = logger;
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        }

        /// <summary>
        /// Attempts to map a sport name into ESPN sport and league identifiers.
        /// Strictly maps 'American Football' to NFL.
        /// </summary>
        public static (string sport, string league)? ResolveEspnSportAndLeague(string? rawSport)
        {
            if (string.IsNullOrWhiteSpace(rawSport)) return null;

            string s = rawSport.Trim().ToLowerInvariant();

            // American Football
            if (s.Contains("american football") || s == "nfl" || s == "football")
            {
                return ("football", "nfl");
            }

            // Ice Hockey
            if (s.Contains("hockey") || s == "nhl")
            {
                return ("hockey", "nhl");
            }

            // Basketball
            if (s.Contains("basketball") || s == "nba")
            {
                return ("basketball", "nba");
            }

            // Baseball
            if (s.Contains("baseball") || s == "mlb")
            {
                return ("baseball", "mlb");
            }

            return null;
        }

        /// <summary>
        /// Fetches match status and detailed boxscore from ESPN.
        /// Searches across the target date and up to +5 days into the future to discover upcoming match start times.
        /// Returns null if the sport is unsupported or if the event is not found (e.g. European hockey).
        /// </summary>
        public async Task<string?> GetMatchStatsJsonAsync(string matchName, string sportName, DateTime? betPlacedAt = null, DateTime? betStartsAt = null, int? betId = null)
        {
            var mapping = ResolveEspnSportAndLeague(sportName);
            if (!mapping.HasValue)
            {
                return null;
            }

            string betLabel = betId.HasValue ? $"[Bet #{betId.Value}]" : "[Test/Manual]";
            var (sport, league) = mapping.Value;
            string emoji = GetSportEmoji(sportName);

            try
            {
                // 1. Clean the match name to extract Home and Away terms
                string[] split = matchName.Split(new[] { " vs ", " - ", " v ", " at ", " @ " }, StringSplitOptions.None);
                if (split.Length < 2) return null;

                string teamA = split[0].Trim();
                string teamB = split[1].Trim();

                // 2. Determine target dates to inspect. 
                // If betStartsAt is known, the date is already pinned down (check that date +/- 1 day).
                // If betStartsAt is unknown (e.g. new bet), inspect today and up to +5 days ahead to discover start times (matching OddsPapi's 5-day future window).
                var baseDate = (betStartsAt ?? betPlacedAt ?? DateTime.UtcNow).Date;
                var datesToCheck = new List<string>();

                if (betStartsAt.HasValue)
                {
                    datesToCheck.Add(baseDate.ToString("yyyyMMdd"));
                    datesToCheck.Add(baseDate.AddDays(1).ToString("yyyyMMdd"));
                    datesToCheck.Add(baseDate.AddDays(-1).ToString("yyyyMMdd"));
                }
                else
                {
                    // For NFL, the default scoreboard endpoint returns the ENTIRE current week of games in 1 single call!
                    if (sport == "football" && league == "nfl")
                    {
                        datesToCheck.Add(""); // default week endpoint
                    }

                    // Inspect primary date, then +/- 1, then up to +5 days
                    datesToCheck.Add(baseDate.ToString("yyyyMMdd"));
                    datesToCheck.Add(baseDate.AddDays(1).ToString("yyyyMMdd"));
                    datesToCheck.Add(baseDate.AddDays(-1).ToString("yyyyMMdd"));
                    datesToCheck.Add(baseDate.AddDays(2).ToString("yyyyMMdd"));
                    datesToCheck.Add(baseDate.AddDays(3).ToString("yyyyMMdd"));
                    datesToCheck.Add(baseDate.AddDays(4).ToString("yyyyMMdd"));
                    datesToCheck.Add(baseDate.AddDays(5).ToString("yyyyMMdd"));
                }

                string dateRangeDesc = datesToCheck.Count > 3 ? $"{baseDate:MM-dd} to {baseDate.AddDays(5):MM-dd}" : $"{baseDate:MM-dd}";

                JsonElement? matchedEvent = null;
                string matchedDate = "";

                foreach (var dateStr in datesToCheck)
                {
                    var scoreboardJson = await GetScoreboardJsonCachedAsync(sport, league, dateStr);
                    if (string.IsNullOrEmpty(scoreboardJson)) continue;

                    using var doc = JsonDocument.Parse(scoreboardJson);
                    if (doc.RootElement.TryGetProperty("events", out var events) && events.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var ev in events.EnumerateArray())
                        {
                            if (IsEventMatch(ev, teamA, teamB))
                            {
                                matchedEvent = ev.Clone();
                                matchedDate = string.IsNullOrEmpty(dateStr) ? "current week" : dateStr;
                                break;
                            }
                        }
                    }

                    if (matchedEvent.HasValue) break;
                }

                if (!matchedEvent.HasValue)
                {
                    _logger.LogInformation($"{betLabel} ❌ [ESPN] Match '{matchName}' ({sport}/{league}) not found in scoreboard across {dateRangeDesc}.");
                    return null;
                }

                var evObj = matchedEvent.Value;
                string eventId = evObj.TryGetProperty("id", out var idElem) ? idElem.GetString() ?? "" : "";
                string startTimeUtc = evObj.TryGetProperty("date", out var dElem) ? dElem.GetString() ?? "" : "";
                string eventName = evObj.TryGetProperty("name", out var nElem) ? nElem.GetString() ?? matchName : matchName;

                // 3. Inspect Event Status
                string statusName = "";
                bool isCompleted = false;
                string statusDetail = "";

                if (evObj.TryGetProperty("status", out var statusObj))
                {
                    if (statusObj.TryGetProperty("type", out var typeObj))
                    {
                        statusName = typeObj.TryGetProperty("name", out var sn) ? sn.GetString() ?? "" : "";
                        isCompleted = typeObj.TryGetProperty("completed", out var comp) && comp.GetBoolean();
                        statusDetail = typeObj.TryGetProperty("detail", out var det) ? det.GetString() ?? "" : "";
                    }
                }

                _logger.LogInformation($"{betLabel} ✅ [ESPN] Found Event ID {eventId} ('{eventName}') on {matchedDate} | Status: {statusName} ({statusDetail}) | StartTime: {startTimeUtc}");

                // 4. If the match is not completed (Scheduled or In-Progress), return the status payload immediately
                if (!isCompleted || statusName != "STATUS_FINAL")
                {
                    var pendingPayload = new Dictionary<string, object>
                    {
                        ["provider"] = "ESPN",
                        ["eventId"] = eventId,
                        ["match"] = eventName,
                        ["sport"] = sportName,
                        ["startTimeIso"] = startTimeUtc,
                        ["status"] = isCompleted ? "Final" : (statusName.Contains("PROGRESS") ? "In Progress" : "Scheduled"),
                        ["statusDetail"] = statusDetail,
                        ["isFinished"] = false,
                        ["WARNING"] = "MATCH NOT FINISHED YET. DO NOT SEARCH GOOGLE OR GRADE LEGS AS WON/LOST. MARK AS PENDING."
                    };

                    if (evObj.TryGetProperty("competitions", out var comps) && comps.GetArrayLength() > 0)
                    {
                        var comp = comps[0];
                        if (comp.TryGetProperty("competitors", out var competitors))
                        {
                            var teamScores = new List<object>();
                            foreach (var c in competitors.EnumerateArray())
                            {
                                string tName = c.TryGetProperty("team", out var tObj) && tObj.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "" : "";
                                string score = c.TryGetProperty("score", out var sc) ? sc.GetString() ?? "0" : "0";
                                string homeAway = c.TryGetProperty("homeAway", out var ha) ? ha.GetString() ?? "" : "";
                                teamScores.Add(new { team = tName, score, homeAway });
                            }
                            pendingPayload["scores"] = teamScores;
                        }
                    }

                    return JsonSerializer.Serialize(pendingPayload);
                }

                // 5. Match is completed (STATUS_FINAL) -> Fetch Full Boxscore Summary
                var summaryJson = await GetGameSummaryJsonAsync(sport, league, eventId);
                if (string.IsNullOrEmpty(summaryJson))
                {
                    // Fallback to scoreboard event data if summary fails
                    return evObj.GetRawText();
                }

                // 6. Carefully prune the summary JSON:
                // Keep 100% of header, boxscore, and gameInfo.
                // Drop videos, articles, news, pickcenter, tickets to save space safely.
                using var sumDoc = JsonDocument.Parse(summaryJson);
                var prunedSummary = new Dictionary<string, object>
                {
                    ["provider"] = "ESPN",
                    ["sport"] = sportName,
                    ["eventId"] = eventId,
                    ["startTimeIso"] = startTimeUtc,
                    ["isFinished"] = true,
                    ["status"] = "Final",
                    ["statusDetail"] = statusDetail
                };

                if (sumDoc.RootElement.TryGetProperty("header", out var hdr))
                {
                    prunedSummary["header"] = hdr;
                }
                if (sumDoc.RootElement.TryGetProperty("boxscore", out var box))
                {
                    prunedSummary["boxscore"] = box;
                }
                if (sumDoc.RootElement.TryGetProperty("gameInfo", out var gi))
                {
                    prunedSummary["gameInfo"] = gi;
                }
                if (sumDoc.RootElement.TryGetProperty("leaders", out var ldr))
                {
                    prunedSummary["leaders"] = ldr;
                }

                return JsonSerializer.Serialize(prunedSummary);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"{betLabel} ESPN API Error fetching match stats for '{matchName}': {ex.Message}");
                return null;
            }
        }

        private bool IsEventMatch(JsonElement ev, string teamA, string teamB)
        {
            if (!ev.TryGetProperty("competitions", out var comps) || comps.GetArrayLength() == 0) return false;
            var comp = comps[0];
            if (!comp.TryGetProperty("competitors", out var competitors) || competitors.GetArrayLength() < 2) return false;

            string normA = NormalizeTeam(teamA);
            string normB = NormalizeTeam(teamB);

            bool matchedA = false;
            bool matchedB = false;

            foreach (var c in competitors.EnumerateArray())
            {
                if (!c.TryGetProperty("team", out var t)) continue;

                string displayName = NormalizeTeam(t.TryGetProperty("displayName", out var dn) ? dn.GetString() : "");
                string name = NormalizeTeam(t.TryGetProperty("name", out var n) ? n.GetString() : "");
                string location = NormalizeTeam(t.TryGetProperty("location", out var loc) ? loc.GetString() : "");
                string shortName = NormalizeTeam(t.TryGetProperty("shortDisplayName", out var sdn) ? sdn.GetString() : "");
                string abbrev = NormalizeTeam(t.TryGetProperty("abbreviation", out var abb) ? abb.GetString() : "");

                if (MatchesAny(normA, displayName, name, location, shortName, abbrev))
                {
                    matchedA = true;
                }
                else if (MatchesAny(normB, displayName, name, location, shortName, abbrev))
                {
                    matchedB = true;
                }
            }

            return matchedA && matchedB;
        }

        private bool MatchesAny(string searchTeam, params string[] candidateNames)
        {
            if (string.IsNullOrEmpty(searchTeam)) return false;

            foreach (var cand in candidateNames)
            {
                if (string.IsNullOrEmpty(cand)) continue;

                if (cand.Equals(searchTeam, StringComparison.OrdinalIgnoreCase)) return true;
                if (cand.Contains(searchTeam, StringComparison.OrdinalIgnoreCase) || searchTeam.Contains(cand, StringComparison.OrdinalIgnoreCase)) return true;

                // Also check individual significant words (e.g. "Hurricanes", "Capitals", "Wings", "Ducks")
                var searchWords = searchTeam.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 4);
                foreach (var sw in searchWords)
                {
                    if (cand.Contains(sw, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }

            return false;
        }

        private string NormalizeTeam(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            return TeamAliasMappingService.RemoveDiacritics(text.Trim().ToLowerInvariant());
        }

        private static string GetSportEmoji(string sport) => sport.ToLowerInvariant() switch
        {
            var s when s.Contains("football") => "🏈",
            var s when s.Contains("hockey") => "🏒",
            var s when s.Contains("basketball") => "🏀",
            var s when s.Contains("baseball") => "⚾",
            _ => "🎯"
        };

        private async Task<string?> GetScoreboardJsonCachedAsync(string sport, string league, string dateStr)
        {
            string cacheKey = string.IsNullOrEmpty(dateStr)
                ? $"espn_scoreboard_{sport}_{league}_current"
                : $"espn_scoreboard_{sport}_{league}_{dateStr}";

            if (_cache.TryGetValue(cacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
            {
                return cached;
            }

            string url = string.IsNullOrEmpty(dateStr)
                ? $"https://site.api.espn.com/apis/site/v2/sports/{sport}/{league}/scoreboard"
                : $"https://site.api.espn.com/apis/site/v2/sports/{sport}/{league}/scoreboard?dates={dateStr}";

            string? json = await ThrottledHttpGetStringAsync(url);
            if (!string.IsNullOrEmpty(json))
            {
                _cache.Set(cacheKey, json, TimeSpan.FromMinutes(5));
            }
            return json;
        }

        private async Task<string?> GetGameSummaryJsonAsync(string sport, string league, string eventId)
        {
            string cacheKey = $"espn_summary_{sport}_{league}_{eventId}";
            if (_cache.TryGetValue(cacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
            {
                return cached;
            }

            string url = $"https://site.api.espn.com/apis/site/v2/sports/{sport}/{league}/summary?event={eventId}";
            string? json = await ThrottledHttpGetStringAsync(url);
            if (!string.IsNullOrEmpty(json))
            {
                _cache.Set(cacheKey, json, TimeSpan.FromHours(1));
            }
            return json;
        }

        private async Task<string?> ThrottledHttpGetStringAsync(string url)
        {
            await _throttleLock.WaitAsync();
            try
            {
                var timeSinceLast = DateTime.UtcNow - _lastRequestTime;
                if (timeSinceLast.TotalMilliseconds < MinRequestDelayMs)
                {
                    int delayNeeded = MinRequestDelayMs - (int)timeSinceLast.TotalMilliseconds;
                    await Task.Delay(delayNeeded);
                }

                _lastRequestTime = DateTime.UtcNow;
                return await _httpClient.GetStringAsync(url);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"ESPN HTTP GET failed for {url}: {ex.Message}");
                return null;
            }
            finally
            {
                _throttleLock.Release();
            }
        }
    }
}
