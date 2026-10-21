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

            // Tennis (ATP and WTA)
            if (s.Contains("tennis") || s == "atp" || s == "wta")
            {
                return ("tennis", s.Contains("wta") ? "wta" : "atp");
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

            // Tennis uses a tournament-based structure (tournaments -> groupings -> competitions -> athletes)
            if (sport == "tennis")
            {
                return await GetTennisMatchStatsJsonAsync(matchName, sportName, league, betPlacedAt, betStartsAt, betId, betLabel);
            }

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
                var targetTime = betStartsAt ?? betPlacedAt ?? DateTime.UtcNow;
                var baseDate = targetTime.Date;
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
                double minDiffMinutes = double.MaxValue;

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
                                string evDateStr = ev.TryGetProperty("date", out var d) ? d.GetString() ?? "" : "";
                                double diff = 0;
                                if (DateTime.TryParse(evDateStr, out var evDt))
                                {
                                    diff = Math.Abs((evDt - targetTime).TotalMinutes);
                                }

                                if (diff < minDiffMinutes)
                                {
                                    minDiffMinutes = diff;
                                    matchedEvent = ev.Clone();
                                    matchedDate = string.IsNullOrEmpty(dateStr) ? "current week" : dateStr;
                                }
                            }
                        }
                    }

                    // If we found a match within 4 hours of target time, short-circuit
                    if (matchedEvent.HasValue && minDiffMinutes < 240) break;
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
                // Keep 100% of scores, linescores, team stats, and player boxscore stats/starter flags,
                // while stripping bulky logo URLs, athlete headshots/links/GUIDs, featuredAthletes, and NHL's duplicate 'skaters' group.
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
                    prunedSummary["header"] = PruneHeader(hdr);
                }
                if (sumDoc.RootElement.TryGetProperty("boxscore", out var box))
                {
                    prunedSummary["boxscore"] = PruneBoxscore(box);
                }
                if (sumDoc.RootElement.TryGetProperty("gameInfo", out var gi))
                {
                    prunedSummary["gameInfo"] = PruneGameInfo(gi);
                }

                return JsonSerializer.Serialize(prunedSummary);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"{betLabel} ESPN API Error fetching match stats for '{matchName}': {ex.Message}");
                return null;
            }
        }

        private static object PruneHeader(JsonElement hdr)
        {
            var result = new Dictionary<string, object>();
            if (hdr.TryGetProperty("id", out var id)) result["id"] = id;
            if (hdr.TryGetProperty("season", out var season)) result["season"] = season;
            if (hdr.TryGetProperty("competitions", out var comps) && comps.ValueKind == JsonValueKind.Array)
            {
                var compList = new List<object>();
                foreach (var c in comps.EnumerateArray())
                {
                    var cDict = new Dictionary<string, object>();
                    if (c.TryGetProperty("id", out var cid)) cDict["id"] = cid;
                    if (c.TryGetProperty("date", out var cdate)) cDict["date"] = cdate;
                    if (c.TryGetProperty("neutralSite", out var ns)) cDict["neutralSite"] = ns;
                    if (c.TryGetProperty("status", out var st) && st.TryGetProperty("type", out var stType))
                    {
                        cDict["status"] = new Dictionary<string, object> { ["type"] = stType };
                    }
                    if (c.TryGetProperty("competitors", out var competitors) && competitors.ValueKind == JsonValueKind.Array)
                    {
                        var compTeams = new List<object>();
                        foreach (var comp in competitors.EnumerateArray())
                        {
                            var ct = new Dictionary<string, object>();
                            if (comp.TryGetProperty("id", out var ctid)) ct["id"] = ctid;
                            if (comp.TryGetProperty("homeAway", out var ha)) ct["homeAway"] = ha;
                            if (comp.TryGetProperty("winner", out var w)) ct["winner"] = w;
                            if (comp.TryGetProperty("score", out var sc)) ct["score"] = sc;
                            if (comp.TryGetProperty("linescores", out var ls)) ct["linescores"] = ls;
                            if (comp.TryGetProperty("team", out var t))
                            {
                                var tDict = new Dictionary<string, object>();
                                if (t.TryGetProperty("id", out var tid)) tDict["id"] = tid;
                                if (t.TryGetProperty("location", out var loc)) tDict["location"] = loc;
                                if (t.TryGetProperty("name", out var nm)) tDict["name"] = nm;
                                if (t.TryGetProperty("nickname", out var nn)) tDict["nickname"] = nn;
                                if (t.TryGetProperty("abbreviation", out var abbr)) tDict["abbreviation"] = abbr;
                                if (t.TryGetProperty("displayName", out var dn)) tDict["displayName"] = dn;
                                ct["team"] = tDict;
                            }
                            compTeams.Add(ct);
                        }
                        cDict["competitors"] = compTeams;
                    }
                    compList.Add(cDict);
                }
                result["competitions"] = compList;
            }
            return result;
        }

        private static object PruneBoxscore(JsonElement box)
        {
            var result = new Dictionary<string, object>();

            if (box.TryGetProperty("teams", out var teams) && teams.ValueKind == JsonValueKind.Array)
            {
                var teamList = new List<object>();
                foreach (var t in teams.EnumerateArray())
                {
                    var tEntry = new Dictionary<string, object>();
                    if (t.TryGetProperty("homeAway", out var ha)) tEntry["homeAway"] = ha;
                    if (t.TryGetProperty("team", out var teamObj))
                    {
                        var cleanTeam = new Dictionary<string, object>();
                        if (teamObj.TryGetProperty("displayName", out var dn)) cleanTeam["displayName"] = dn;
                        if (teamObj.TryGetProperty("name", out var nm)) cleanTeam["name"] = nm;
                        if (teamObj.TryGetProperty("abbreviation", out var ab)) cleanTeam["abbreviation"] = ab;
                        tEntry["team"] = cleanTeam;
                    }
                    if (t.TryGetProperty("statistics", out var stats)) tEntry["statistics"] = stats;
                    teamList.Add(tEntry);
                }
                result["teams"] = teamList;
            }

            if (box.TryGetProperty("players", out var players) && players.ValueKind == JsonValueKind.Array)
            {
                var playerTeams = new List<object>();
                foreach (var pt in players.EnumerateArray())
                {
                    var ptDict = new Dictionary<string, object>();
                    if (pt.TryGetProperty("team", out var teamObj))
                    {
                        var cleanTeam = new Dictionary<string, object>();
                        if (teamObj.TryGetProperty("displayName", out var dn)) cleanTeam["displayName"] = dn;
                        if (teamObj.TryGetProperty("name", out var nm)) cleanTeam["name"] = nm;
                        if (teamObj.TryGetProperty("abbreviation", out var ab)) cleanTeam["abbreviation"] = ab;
                        ptDict["team"] = cleanTeam;
                    }

                    if (pt.TryGetProperty("statistics", out var statGroups) && statGroups.ValueKind == JsonValueKind.Array)
                    {
                        var groupNames = statGroups.EnumerateArray()
                            .Select(sg => sg.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "")
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);

                        // In NHL, ESPN includes 'forwards', 'defenses', AND 'skaters' (which duplicates all forwards + defenses).
                        bool skipDuplicateSkaters = groupNames.Contains("skaters") && (groupNames.Contains("forwards") || groupNames.Contains("defenses"));

                        var cleanGroups = new List<object>();
                        foreach (var sg in statGroups.EnumerateArray())
                        {
                            string gName = sg.TryGetProperty("name", out var gn) ? gn.GetString() ?? "" : "";
                            if (skipDuplicateSkaters && gName.Equals("skaters", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            var sgDict = new Dictionary<string, object>();
                            if (!string.IsNullOrEmpty(gName)) sgDict["name"] = gName;
                            if (sg.TryGetProperty("labels", out var lbls)) sgDict["labels"] = lbls;
                            if (sg.TryGetProperty("keys", out var keys)) sgDict["keys"] = keys;
                            if (sg.TryGetProperty("totals", out var tots)) sgDict["totals"] = tots;

                            if (sg.TryGetProperty("athletes", out var aths) && aths.ValueKind == JsonValueKind.Array)
                            {
                                var cleanAthletes = new List<object>();
                                foreach (var a in aths.EnumerateArray())
                                {
                                    var aDict = new Dictionary<string, object>();
                                    if (a.TryGetProperty("athlete", out var athObj))
                                    {
                                        var cleanAth = new Dictionary<string, object>();
                                        if (athObj.TryGetProperty("displayName", out var dn)) cleanAth["displayName"] = dn;
                                        if (athObj.TryGetProperty("shortName", out var sn)) cleanAth["shortName"] = sn;
                                        if (athObj.TryGetProperty("jersey", out var j)) cleanAth["jersey"] = j;
                                        if (athObj.TryGetProperty("position", out var pos))
                                        {
                                            if (pos.TryGetProperty("abbreviation", out var posAb)) cleanAth["position"] = posAb;
                                            else if (pos.TryGetProperty("name", out var posNm)) cleanAth["position"] = posNm;
                                        }
                                        if (athObj.TryGetProperty("active", out var act)) cleanAth["active"] = act;
                                        if (athObj.TryGetProperty("scratched", out var scr)) cleanAth["scratched"] = scr;
                                        aDict["athlete"] = cleanAth;
                                    }
                                    if (a.TryGetProperty("starter", out var st)) aDict["starter"] = st;
                                    if (a.TryGetProperty("didNotPlay", out var dnp)) aDict["didNotPlay"] = dnp;
                                    if (a.TryGetProperty("ejected", out var ej) && ej.ValueKind == JsonValueKind.True) aDict["ejected"] = ej;
                                    if (a.TryGetProperty("reason", out var rsn)) aDict["reason"] = rsn;
                                    if (a.TryGetProperty("stats", out var stArr)) aDict["stats"] = stArr;
                                    cleanAthletes.Add(aDict);
                                }
                                sgDict["athletes"] = cleanAthletes;
                            }
                            cleanGroups.Add(sgDict);
                        }
                        ptDict["statistics"] = cleanGroups;
                    }
                    playerTeams.Add(ptDict);
                }
                result["players"] = playerTeams;
            }

            return result;
        }

        private static object PruneGameInfo(JsonElement gi)
        {
            var result = new Dictionary<string, object>();
            if (gi.TryGetProperty("attendance", out var att)) result["attendance"] = att;
            if (gi.TryGetProperty("venue", out var v) && v.TryGetProperty("fullName", out var fn))
            {
                result["venue"] = new Dictionary<string, object> { ["fullName"] = fn };
            }
            return result;
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
            var s when s.Contains("tennis") => "🎾",
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

        private async Task<string?> GetTennisMatchStatsJsonAsync(
            string matchName,
            string sportName,
            string initialLeague,
            DateTime? betPlacedAt,
            DateTime? betStartsAt,
            int? betId,
            string betLabel)
        {
            try
            {
                string[] split = matchName.Split(new[] { " vs ", " - ", " v ", " at ", " @ " }, StringSplitOptions.None);
                if (split.Length < 2) return null;

                string playerA = NormalizeTeam(split[0]);
                string playerB = NormalizeTeam(split[1]);

                var targetTime = betStartsAt ?? betPlacedAt ?? DateTime.UtcNow;
                var baseDate = targetTime.Date;
                var datesToCheck = new List<string>
                {
                    baseDate.ToString("yyyyMMdd"),
                    baseDate.AddDays(1).ToString("yyyyMMdd"),
                    baseDate.AddDays(-1).ToString("yyyyMMdd")
                };

                if (!betStartsAt.HasValue)
                {
                    datesToCheck.Add(baseDate.AddDays(2).ToString("yyyyMMdd"));
                    datesToCheck.Add(baseDate.AddDays(3).ToString("yyyyMMdd"));
                    datesToCheck.Add(baseDate.AddDays(4).ToString("yyyyMMdd"));
                    datesToCheck.Add(baseDate.AddDays(5).ToString("yyyyMMdd"));
                }

                // If initialLeague is specified as "wta", search WTA first then ATP. Otherwise ATP first then WTA.
                var leaguesToCheck = initialLeague == "wta" ? new[] { "wta", "atp" } : new[] { "atp", "wta" };

                JsonElement? bestComp = null;
                string matchedTourney = "";
                string matchedLeague = "";
                double minDiffMinutes = double.MaxValue;

                foreach (var league in leaguesToCheck)
                {
                    foreach (var dateStr in datesToCheck)
                    {
                        var scoreboardJson = await GetScoreboardJsonCachedAsync("tennis", league, dateStr);
                        if (string.IsNullOrEmpty(scoreboardJson)) continue;

                        using var doc = JsonDocument.Parse(scoreboardJson);
                        if (!doc.RootElement.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array) continue;

                        foreach (var ev in events.EnumerateArray())
                        {
                            string tourney = ev.TryGetProperty("name", out var tn) ? tn.GetString() ?? "" : "";
                            if (!ev.TryGetProperty("groupings", out var groupings) || groupings.ValueKind != JsonValueKind.Array) continue;

                            foreach (var g in groupings.EnumerateArray())
                            {
                                if (!g.TryGetProperty("competitions", out var comps) || comps.ValueKind != JsonValueKind.Array) continue;

                                foreach (var c in comps.EnumerateArray())
                                {
                                    if (IsTennisMatch(c, playerA, playerB))
                                    {
                                        string dateStrUtc = c.TryGetProperty("date", out var d) ? d.GetString() ?? "" : "";
                                        double diff = 0;
                                        if (DateTime.TryParse(dateStrUtc, out var dt))
                                        {
                                            diff = Math.Abs((dt - targetTime).TotalMinutes);
                                        }

                                        if (diff < minDiffMinutes)
                                        {
                                            minDiffMinutes = diff;
                                            bestComp = c.Clone();
                                            matchedTourney = tourney;
                                            matchedLeague = league;
                                        }
                                    }
                                }
                            }
                        }

                        if (bestComp.HasValue && minDiffMinutes < 1440) break; // Found within 24h of target time
                    }

                    if (bestComp.HasValue) break;
                }

                if (!bestComp.HasValue)
                {
                    _logger.LogInformation($"{betLabel} ❌ [ESPN] Tennis match '{matchName}' not found in ATP/WTA scoreboards.");
                    return null;
                }

                var comp = bestComp.Value;
                string eventId = comp.TryGetProperty("id", out var idElem) ? idElem.GetString() ?? "" : "";
                string startTimeUtc = comp.TryGetProperty("date", out var dElem) ? dElem.GetString() ?? "" : "";

                string statusName = "";
                bool isCompleted = false;
                string statusDetail = "";
                if (comp.TryGetProperty("status", out var stObj) && stObj.TryGetProperty("type", out var typ))
                {
                    statusName = typ.TryGetProperty("name", out var sn) ? sn.GetString() ?? "" : "";
                    isCompleted = typ.TryGetProperty("completed", out var cBool) && cBool.GetBoolean();
                    statusDetail = typ.TryGetProperty("detail", out var det) ? det.GetString() ?? "" : "";
                }

                bool isFinished = isCompleted && (statusName == "STATUS_FINAL" || statusName == "STATUS_RETIRED" || statusName == "STATUS_WALKOVER" || statusName == "STATUS_DEFAULT");

                _logger.LogInformation($"{betLabel} ✅ [ESPN] Found Tennis Match ID {eventId} ('{matchName}') in {matchedTourney} ({matchedLeague.ToUpperInvariant()}) | Status: {statusName} ({statusDetail}) | StartTime: {startTimeUtc}");

                var matchNotes = new List<string>();
                if (comp.TryGetProperty("notes", out var notesArr) && notesArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var n in notesArr.EnumerateArray())
                    {
                        if (n.TryGetProperty("text", out var txt)) matchNotes.Add(txt.GetString() ?? "");
                    }
                }

                string roundName = comp.TryGetProperty("round", out var rObj) && rObj.TryGetProperty("displayName", out var rdn) ? rdn.GetString() ?? "" : "";
                string venueName = comp.TryGetProperty("venue", out var vObj) && vObj.TryGetProperty("fullName", out var vn) ? vn.GetString() ?? "" : "";
                string courtName = comp.TryGetProperty("venue", out var vObj2) && vObj2.TryGetProperty("court", out var cn) ? cn.GetString() ?? "" : "";

                var competitorsList = new List<object>();
                if (comp.TryGetProperty("competitors", out var competitors) && competitors.ValueKind == JsonValueKind.Array)
                {
                    foreach (var c in competitors.EnumerateArray())
                    {
                        string pName = "";
                        string pCountry = "";
                        if (c.TryGetProperty("athlete", out var a))
                        {
                            pName = a.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "" : "";
                            if (a.TryGetProperty("flag", out var fl) && fl.TryGetProperty("alt", out var cAlt))
                            {
                                pCountry = cAlt.GetString() ?? "";
                            }
                        }

                        bool winner = c.TryGetProperty("winner", out var w) && w.GetBoolean();
                        var sets = new List<object>();
                        if (c.TryGetProperty("linescores", out var linescores) && linescores.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var ls in linescores.EnumerateArray())
                            {
                                double scoreVal = ls.TryGetProperty("value", out var v) ? v.GetDouble() : 0;
                                int? tiebreak = ls.TryGetProperty("tiebreak", out var tb) ? tb.GetInt32() : null;
                                bool setWinner = ls.TryGetProperty("winner", out var sw) && sw.GetBoolean();
                                sets.Add(new { setScore = scoreVal, tiebreak, wonSet = setWinner });
                            }
                        }

                        competitorsList.Add(new
                        {
                            player = pName,
                            country = pCountry,
                            winner,
                            sets
                        });
                    }
                }

                var payload = new Dictionary<string, object>
                {
                    ["provider"] = "ESPN",
                    ["sport"] = "Tennis",
                    ["league"] = matchedLeague.ToUpperInvariant(),
                    ["tournament"] = matchedTourney,
                    ["round"] = roundName,
                    ["venue"] = $"{venueName} ({courtName})".Trim(),
                    ["eventId"] = eventId,
                    ["match"] = matchName,
                    ["startTimeIso"] = startTimeUtc,
                    ["isFinished"] = isFinished,
                    ["status"] = isFinished ? "Final" : (statusName.Contains("PROGRESS") ? "In Progress" : "Scheduled"),
                    ["statusDetail"] = statusDetail,
                    ["resultSummary"] = matchNotes.Count > 0 ? string.Join("; ", matchNotes) : "",
                    ["competitors"] = competitorsList,
                    ["rawCompetition"] = comp
                };

                if (!isFinished)
                {
                    payload["WARNING"] = "MATCH NOT FINISHED YET. DO NOT SEARCH GOOGLE OR GRADE LEGS AS WON/LOST. MARK AS PENDING.";
                }

                return JsonSerializer.Serialize(payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"{betLabel} ESPN API Error fetching tennis stats for '{matchName}': {ex.Message}");
                return null;
            }
        }

        private bool IsTennisMatch(JsonElement comp, string playerA, string playerB)
        {
            if (!comp.TryGetProperty("competitors", out var competitors) || competitors.GetArrayLength() < 2) return false;

            bool matchedA = false;
            bool matchedB = false;

            foreach (var c in competitors.EnumerateArray())
            {
                if (!c.TryGetProperty("athlete", out var a)) continue;

                string displayName = NormalizeTeam(a.TryGetProperty("displayName", out var dn) ? dn.GetString() : "");
                string fullName = NormalizeTeam(a.TryGetProperty("fullName", out var fn) ? fn.GetString() : "");
                string shortName = NormalizeTeam(a.TryGetProperty("shortName", out var sn) ? sn.GetString() : "");

                if (MatchesAny(playerA, displayName, fullName, shortName))
                {
                    matchedA = true;
                }
                else if (MatchesAny(playerB, displayName, fullName, shortName))
                {
                    matchedB = true;
                }
            }

            return matchedA && matchedB;
        }
    }
}
