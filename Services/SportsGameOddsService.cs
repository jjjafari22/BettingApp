using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
using BettingApp.Models;

namespace BettingApp.Services;

public class SportsGameOddsService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly IMemoryCache _cache;
    private readonly TeamAliasMappingService _teamAliasMappingService;

    public SportsGameOddsService(HttpClient httpClient, IConfiguration config, IMemoryCache cache, TeamAliasMappingService teamAliasMappingService)
    {
        _httpClient = httpClient;
        _apiKey = config["SportsGameOdds:ApiKey"] ?? "";
        _cache = cache;
        _teamAliasMappingService = teamAliasMappingService;
    }

    public async Task<(OddsPapiSearchResult? Result, string? Error)> SearchOddsComparisonAsync(string teamName, int? betId = null)
    {
        if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrWhiteSpace(teamName)) return (null, "API Key is missing or team name is empty.");

        try
        {
            string betLabel = betId.HasValue ? $"[Bet #{betId.Value}]" : "[Manual Lookup]";
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            
            var allEvents = new List<JsonElement>();

            if (!_cache.TryGetValue("SgoEvents", out string? eJson))
            {
                Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] {betLabel} SportsGameOdds: Fetching fresh events from API (Cache Miss)");

                string fromDate = Uri.EscapeDataString(DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-ddTHH:mm:ssZ"));
                string toDate = Uri.EscapeDataString(DateTime.UtcNow.AddDays(8).ToString("yyyy-MM-ddTHH:mm:ssZ"));
                
                string? cursor = null;
                bool hasMore = true;
                while (hasMore)
                {
                    var eventsUrl = $"https://api.sportsgameodds.com/v2/events?oddsAvailable=true&limit=100&startsAfter={fromDate}&startsBefore={toDate}&apiKey={_apiKey}";
                    if (!string.IsNullOrEmpty(cursor))
                    {
                        eventsUrl += $"&cursor={cursor}";
                    }

                    var eResp = await _httpClient.GetAsync(eventsUrl);
                    if (!eResp.IsSuccessStatusCode) return (null, $"SportsGameOdds API returned status {eResp.StatusCode}");
                    
                    var pageJson = await eResp.Content.ReadAsStringAsync();
                    using var pageDoc = JsonDocument.Parse(pageJson);
                    
                    if (pageDoc.RootElement.TryGetProperty("data", out var dataArray) && dataArray.ValueKind == JsonValueKind.Array)
                    {
                        allEvents.AddRange(dataArray.EnumerateArray().Select(e => e.Clone()));
                    }

                    if (pageDoc.RootElement.TryGetProperty("nextCursor", out var nextCursorNode) && nextCursorNode.ValueKind == JsonValueKind.String)
                    {
                        cursor = nextCursorNode.GetString();
                        hasMore = !string.IsNullOrEmpty(cursor);
                    }
                    else
                    {
                        hasMore = false;
                    }
                }
                
                var serializedEvents = JsonSerializer.Serialize(allEvents, options);
                _cache.Set("SgoEvents", serializedEvents, TimeSpan.FromSeconds(60));
                eJson = serializedEvents;
            }
            else
            {
                using var cachedDoc = JsonDocument.Parse(eJson ?? "[]");
                allEvents.AddRange(cachedDoc.RootElement.EnumerateArray().Select(e => e.Clone()));
            }

            string[] split = teamName.Split(new[] { " vs ", " v ", " - " }, StringSplitOptions.None);
            string homeTeam = split[0].Trim();
            string awayTeam = split.Length > 1 ? split[1].Trim() : "";

            var dateMatch = System.Text.RegularExpressions.Regex.Match(awayTeam, @"\((?:Starts:\s*)?([^)]+)\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (dateMatch.Success)
            {
                awayTeam = awayTeam.Substring(0, dateMatch.Index).Trim();
            }

            var homeTokens = homeTeam.Split(new[] { ' ', '-', '.' }, StringSplitOptions.RemoveEmptyEntries)
                                     .Where(w => w.Length >= 3 && !w.Equals("the", StringComparison.OrdinalIgnoreCase)).ToList();
            var awayTokens = awayTeam.Split(new[] { ' ', '-', '.' }, StringSplitOptions.RemoveEmptyEntries)
                                     .Where(w => w.Length >= 3 && !w.Equals("the", StringComparison.OrdinalIgnoreCase)).ToList();

            if (!homeTokens.Any()) homeTokens.Add(homeTeam);
            if (!awayTokens.Any() && !string.IsNullOrEmpty(awayTeam)) awayTokens.Add(awayTeam);

            var bestMatches = new List<(JsonElement fixture, int score)>();

            foreach (var f in allEvents)
            {
                if (!f.TryGetProperty("teams", out var teams)) continue;

                string p1 = "";
                string p2 = "";

                if (teams.TryGetProperty("home", out var homeNode) && homeNode.TryGetProperty("names", out var homeNames))
                {
                    p1 = homeNames.TryGetProperty("long", out var hl) ? hl.GetString() ?? "" : "";
                    if (string.IsNullOrEmpty(p1) && homeNames.TryGetProperty("medium", out var hm)) p1 = hm.GetString() ?? "";
                }

                if (teams.TryGetProperty("away", out var awayNode) && awayNode.TryGetProperty("names", out var awayNames))
                {
                    p2 = awayNames.TryGetProperty("long", out var al) ? al.GetString() ?? "" : "";
                    if (string.IsNullOrEmpty(p2) && awayNames.TryGetProperty("medium", out var am)) p2 = am.GetString() ?? "";
                }

                bool homeMatch = homeTokens.Any(t => IsNameMatch(p1, t) || IsNameMatch(p2, t));
                bool awayMatch = string.IsNullOrEmpty(awayTeam) || awayTokens.Any(t => IsNameMatch(p1, t) || IsNameMatch(p2, t));

                if (!homeMatch || !awayMatch) continue;

                int score = 0;
                
                if (IsNameMatch(p1, homeTeam) || IsNameMatch(p2, homeTeam)) score += 50;
                if (!string.IsNullOrEmpty(awayTeam) && (IsNameMatch(p1, awayTeam) || IsNameMatch(p2, awayTeam))) score += 50;
                
                if (IsExactMatch(p1, homeTeam) || IsExactMatch(p2, homeTeam)) score += 100;
                if (!string.IsNullOrEmpty(awayTeam) && (IsExactMatch(p1, awayTeam) || IsExactMatch(p2, awayTeam))) score += 100;

                foreach (var token in homeTokens.Concat(awayTokens))
                {
                    if (IsNameMatch(p1, token) || IsNameMatch(p2, token)) score += 10;
                }
                
                if (score > 0)
                {
                    bestMatches.Add((f, score));
                }
            }

            if (!bestMatches.Any()) 
            {
                Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] {betLabel} SportsGameOdds: Could not find match against '{teamName}'");
                return (null, $"No matching fixtures found for '{teamName}'.");
            }
            
            var bestFixture = bestMatches.OrderByDescending(m => m.score).First().fixture;
            
            string finalP1 = "";
            string finalP2 = "";
            if (bestFixture.TryGetProperty("teams", out var bestTeams))
            {
                if (bestTeams.TryGetProperty("home", out var h) && h.TryGetProperty("names", out var hn) && hn.TryGetProperty("long", out var hl)) finalP1 = hl.GetString() ?? "";
                if (bestTeams.TryGetProperty("away", out var a) && a.TryGetProperty("names", out var an) && an.TryGetProperty("long", out var al)) finalP2 = al.GetString() ?? "";
            }
            
            string matchName = $"{finalP1} vs {finalP2}";
            DateTime startTime = DateTime.MinValue;
            bool isLive = false;

            if (bestFixture.TryGetProperty("status", out var statusNode))
            {
                if (statusNode.TryGetProperty("startsAt", out var st) && DateTime.TryParse(st.GetString(), null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var dt))
                {
                    startTime = dt;
                }
                if (statusNode.TryGetProperty("live", out var liveNode))
                {
                    isLive = liveNode.GetBoolean();
                }
            }

            var result = new OddsPapiSearchResult
            {
                MatchName = matchName,
                StartTime = startTime,
                IsLive = isLive
            };

            var baseMarketDict = new Dictionary<string, OddsPapiMarket>();

            if (bestFixture.TryGetProperty("odds", out var oddsNode))
            {
                foreach (var oddProperty in oddsNode.EnumerateObject())
                {
                    var odd = oddProperty.Value;
                    string marketName = odd.TryGetProperty("marketName", out var mn) ? mn.GetString() ?? "Unknown" : "Unknown";
                    string sideId = odd.TryGetProperty("sideID", out var sd) ? sd.GetString() ?? "unknown" : "unknown";
                    
                    // SGO sometimes has "fairSpread", "bookSpread", "fairOverUnder", "bookOverUnder"
                    string suffix = "";
                    if (odd.TryGetProperty("bookSpread", out var bs)) suffix = $" ({bs.GetString()})";
                    else if (odd.TryGetProperty("bookOverUnder", out var bou)) suffix = $" ({bou.GetString()})";

                    string outcomeName = sideId + suffix;

                    if (!baseMarketDict.ContainsKey(marketName))
                    {
                        var mObj = new OddsPapiMarket { MarketId = marketName, MarketName = marketName };
                        baseMarketDict[marketName] = mObj;
                        result.Markets.Add(mObj);
                        result.BookmakerOdds[marketName] = new Dictionary<string, Dictionary<string, BettingApp.Models.OddsData>>(StringComparer.OrdinalIgnoreCase);
                    }

                    baseMarketDict[marketName].OutcomeNames[outcomeName] = outcomeName;

                    if (odd.TryGetProperty("byBookmaker", out var byBookmakerNode))
                    {
                        var allowedBookmakers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "coolbet", "bet365", "unibet", "pinnacle" };
                        foreach (var bmProperty in byBookmakerNode.EnumerateObject())
                        {
                            string bmName = bmProperty.Name;
                            if (!allowedBookmakers.Contains(bmName)) continue;

                            if (!result.BookmakerOdds[marketName].ContainsKey(bmName))
                            {
                                result.BookmakerOdds[marketName][bmName] = new Dictionary<string, BettingApp.Models.OddsData>(StringComparer.OrdinalIgnoreCase);
                            }

                            if (bmProperty.Value.TryGetProperty("odds", out var priceNode))
                            {
                                string americanOdds = priceNode.GetString() ?? "";
                                double decimalPrice = ConvertAmericanToDecimal(americanOdds);
                                if (decimalPrice > 0)
                                {
                                    result.BookmakerOdds[marketName][bmName][outcomeName] = new BettingApp.Models.OddsData { Price = decimalPrice };
                                }
                            }
                        }
                    }
                }
            }

            // Clean up empty bookmakers
            foreach (var m in result.Markets)
            {
                var bookmakers = result.BookmakerOdds[m.MarketName].Keys.ToList();
                foreach (var bm in bookmakers)
                {
                    if (!result.BookmakerOdds[m.MarketName][bm].Any())
                    {
                        result.BookmakerOdds[m.MarketName].Remove(bm);
                    }
                }
            }

            result.Markets.Sort((a, b) => a.MarketName.CompareTo(b.MarketName));

            return (result, null);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] Exception in SportsGameOddsService: {ex.Message}");
            return (null, $"Exception: {ex.Message}");
        }
    }

    private double ConvertAmericanToDecimal(string americanOdds)
    {
        if (string.IsNullOrEmpty(americanOdds)) return 0;
        if (double.TryParse(americanOdds.Replace("+", ""), out double odds))
        {
            if (odds > 0) return Math.Round(1 + (odds / 100), 2);
            if (odds < 0) return Math.Round(1 + (100 / Math.Abs(odds)), 2);
            if (odds == 0) return 1.0;
        }
        return 0;
    }

    private bool IsNameMatch(string source, string target)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target)) return false;
        
        string normalizedSource = NormalizeTeamName(source);
        string normalizedTarget = NormalizeTeamName(target);
        
        return normalizedSource.Contains(normalizedTarget, StringComparison.OrdinalIgnoreCase) || 
               normalizedTarget.Contains(normalizedSource, StringComparison.OrdinalIgnoreCase);
    }
    
    private bool IsExactMatch(string source, string target)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target)) return false;
        return NormalizeTeamName(source).Equals(NormalizeTeamName(target), StringComparison.OrdinalIgnoreCase);
    }
    
    private string NormalizeTeamName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        
        string result = TeamAliasMappingService.RemoveDiacritics(name).ToLowerInvariant();
        result = _teamAliasMappingService.ApplyTeamAliases(result);

        return result.Replace("ø", "o").Replace("æ", "a").Replace("å", "a").Replace("oe", "o")
                   .Replace("ae", "a").Replace("aa", "a").Replace(" fc", "").Replace("fk ", "")
                   .Replace(" united", "").Replace(" city", "").Replace("cf ", "").Replace(" cd", "")
                   .Replace("bk ", "").Replace(" (w)", "").Replace(" women", "").Trim();
    }
}
