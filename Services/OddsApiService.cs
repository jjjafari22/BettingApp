using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace BettingApp.Services;

public class OddsApiService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly IMemoryCache _cache;
    private readonly TeamAliasMappingService _teamAliasMappingService;

    public OddsApiService(HttpClient httpClient, IConfiguration config, IMemoryCache cache, TeamAliasMappingService teamAliasMappingService)
    {
        _httpClient = httpClient;
        _apiKey = config["OddsApi:ApiKey"] ?? "";
        _cache = cache;
        _teamAliasMappingService = teamAliasMappingService;
    }




    public async Task<(BettingApp.Models.OddsPapiSearchResult? Result, string? Error)> SearchOddsComparisonAsync(string teamName, int? betId = null)
    {
        if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrWhiteSpace(teamName)) return (null, "API Key is missing or team name is empty.");

        try
        {
            string betLabel = betId.HasValue ? $"[Bet #{betId.Value}]" : "[Manual Lookup]";
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            
            // 1. Get markets metadata to map IDs to Names
            var marketsUrl = $"https://api.oddspapi.io/v4/markets?apiKey={_apiKey}&language=en";
            
            if (!_cache.TryGetValue("OddspapiMarketsJson", out string? mJson))
            {
                var mResp = await _httpClient.GetAsync(marketsUrl);
                if (mResp.IsSuccessStatusCode)
                {
                    mJson = await mResp.Content.ReadAsStringAsync();
                    _cache.Set("OddspapiMarketsJson", mJson, TimeSpan.FromHours(24));
                }
                else
                {
                    mJson = "[]";
                }
            }
            
            var rawMarketIdToBaseName = new Dictionary<string, string>();
            var baseMarketDict = new Dictionary<string, BettingApp.Models.OddsPapiMarket>();
            if (!string.IsNullOrEmpty(mJson) && mJson != "[]")
            {
                using var mDoc = JsonDocument.Parse(mJson);
                if (mDoc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var m in mDoc.RootElement.EnumerateArray())
                    {
                        var mId = m.GetProperty("marketId").ToString();
                        var baseName = m.TryGetProperty("marketName", out var mn) ? mn.GetString() ?? "Unknown" : "Unknown";
                        
                        string handicapSuffix = "";
                        if (m.TryGetProperty("handicap", out var hc))
                        {
                            if (hc.ValueKind == JsonValueKind.Number && hc.GetDouble() != 0) handicapSuffix = $" ({hc.GetDouble()})";
                            else if (hc.ValueKind == JsonValueKind.String && hc.GetString() != "0") handicapSuffix = $" ({hc.GetString()})";
                        }
                        
                        if (baseName.Contains("European Handicap", StringComparison.OrdinalIgnoreCase))
                        {
                            baseName = baseName.Replace("European Handicap", "3-Way Handicap", StringComparison.OrdinalIgnoreCase);
                            
                            if (handicapSuffix.StartsWith(" (-") && handicapSuffix.EndsWith(")"))
                            {
                                handicapSuffix = $" (0-{handicapSuffix.Substring(3, handicapSuffix.Length - 4)})";
                            }
                            else if (handicapSuffix.StartsWith(" (") && handicapSuffix.EndsWith(")") && !handicapSuffix.Contains("-"))
                            {
                                handicapSuffix = $" ({handicapSuffix.Substring(2, handicapSuffix.Length - 3)}-0)";
                            }
                        }
                        
                        rawMarketIdToBaseName[mId] = baseName;
                        
                        if (!baseMarketDict.ContainsKey(baseName))
                        {
                            baseMarketDict[baseName] = new BettingApp.Models.OddsPapiMarket { MarketId = baseName, MarketName = baseName };
                        }
                    
                        var baseMarketObj = baseMarketDict[baseName];
                        
                        if (m.TryGetProperty("outcomes", out var outcomes))
                        {
                            foreach (var o in outcomes.EnumerateArray())
                            {
                                var oId = o.GetProperty("outcomeId").ToString();
                                var oName = o.TryGetProperty("outcomeName", out var on) ? on.GetString() ?? "" : "";
                                baseMarketObj.OutcomeNames[oId] = oName + handicapSuffix;
                            }
                        }
                }
                }
            }

            // 2. Find Fixture
            string fromDate = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd");
            string toDate = DateTime.UtcNow.AddDays(8).ToString("yyyy-MM-dd");
            var fixturesUrl = $"https://api.oddspapi.io/v4/fixtures?apiKey={_apiKey}&sportId=10&from={fromDate}&to={toDate}";
            
            if (!_cache.TryGetValue($"OddspapiFixtures_{fromDate}", out string? fJson))
            {
                Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] {betLabel} OddsPapi: Fetching fresh fixtures from API (Cache Miss)");

                var fResp = await _httpClient.GetAsync(fixturesUrl);
                if (!fResp.IsSuccessStatusCode) return (null, $"Fixtures API returned status {fResp.StatusCode}");
                
                fJson = await fResp.Content.ReadAsStringAsync();
                _cache.Set($"OddspapiFixtures_{fromDate}", fJson, TimeSpan.FromHours(1));
            }
            
            using var doc = JsonDocument.Parse(fJson ?? "[]");
            
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return (null, "Fixtures API returned invalid JSON format.");

            string? fixtureId = null;
            string matchName = "";
            DateTime startTime = DateTime.MinValue;

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

            foreach (var f in doc.RootElement.EnumerateArray())
            {
                string p1 = f.TryGetProperty("participant1Name", out var p1n) ? (p1n.GetString() ?? "") : "";
                string p2 = f.TryGetProperty("participant2Name", out var p2n) ? (p2n.GetString() ?? "") : "";
                
                bool homeMatch = homeTokens.Any(t => IsNameMatch(p1, t) || IsNameMatch(p2, t));
                bool awayMatch = string.IsNullOrEmpty(awayTeam) || awayTokens.Any(t => IsNameMatch(p1, t) || IsNameMatch(p2, t));

                // Strict rule: Must match at least one token from BOTH sides!
                if (!homeMatch || !awayMatch)
                {
                    continue; 
                }

                int score = 0;
                
                if (IsNameMatch(p1, homeTeam) || IsNameMatch(p2, homeTeam)) score += 50;
                if (!string.IsNullOrEmpty(awayTeam) && (IsNameMatch(p1, awayTeam) || IsNameMatch(p2, awayTeam))) score += 50;
                
                // Huge bonus for exact match to differentiate "Team" from "Team 2"
                if (IsExactMatch(p1, homeTeam) || IsExactMatch(p2, homeTeam)) score += 100;
                if (!string.IsNullOrEmpty(awayTeam) && (IsExactMatch(p1, awayTeam) || IsExactMatch(p2, awayTeam))) score += 100;

                foreach (var token in homeTokens.Concat(awayTokens))
                {
                    if (IsNameMatch(p1, token) || IsNameMatch(p2, token)) score += 10;
                    
                    if ((token.Contains("kobenhavn", StringComparison.OrdinalIgnoreCase) || token.Contains("copenhagen", StringComparison.OrdinalIgnoreCase)) && 
                        (IsNameMatch(p1, "copenhagen") || IsNameMatch(p2, "copenhagen")))
                    {
                        score += 20;
                    }
                }
                
                if (score > 0)
                {
                    bestMatches.Add((f, score));
                }
            }

            if (!bestMatches.Any()) 
            {
                Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] {betLabel} OddsPapi: Could not find match against '{teamName}'");
                return (null, $"No matching fixtures found for '{teamName}' in the next 7 days.");
            }
            
            var bestFixture = bestMatches.OrderByDescending(m => m.score).First().fixture;
            
            fixtureId = bestFixture.GetProperty("fixtureId").ToString();
            string finalP1 = bestFixture.TryGetProperty("participant1Name", out var fp1) ? (fp1.GetString() ?? "") : "";
            string finalP2 = bestFixture.TryGetProperty("participant2Name", out var fp2) ? (fp2.GetString() ?? "") : "";
            matchName = $"{finalP1} vs {finalP2}";

            Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] {betLabel} OddsPapi: Found Match ID {fixtureId} for {matchName}");
            
            if (bestFixture.TryGetProperty("startTime", out var st))
            {
                if (DateTime.TryParse(st.GetString(), null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var dt))
                {
                    startTime = dt;
                }
            }
            
            bool isLive = false;
            if (bestFixture.TryGetProperty("statusId", out var sid) && sid.ValueKind == JsonValueKind.Number)
            {
                int statusId = sid.GetInt32();
                if (statusId > 0 && statusId != 3)
                {
                    isLive = true;
                }
                else if (statusId == 0 && startTime <= DateTime.UtcNow)
                {
                    // OddsPapi may be slow to update statusId to 1; if it's past start time and still 0 (pre-game), treat as live
                    isLive = true;
                }
            }

            // 3. Fetch Odds for Unibet SE, Betsson, Bet365
            var oddsUrl = $"https://api.oddspapi.io/v4/odds?apiKey={_apiKey}&fixtureId={fixtureId}&bookmakers=unibet.se,betsson,bet365";
            var oResp = await _httpClient.GetAsync(oddsUrl);
            
            // Retry once if rate limited
            if (!oResp.IsSuccessStatusCode)
            {
                await Task.Delay(1000); // Wait 1s
                oResp = await _httpClient.GetAsync(oddsUrl);
                if (!oResp.IsSuccessStatusCode) return (null, $"Odds API returned status {oResp.StatusCode}");
            }

            var oJson = await oResp.Content.ReadAsStringAsync();
            using var oddsDoc = JsonDocument.Parse(oJson);
            
            var result = new BettingApp.Models.OddsPapiSearchResult
            {
                MatchName = matchName,
                StartTime = startTime,
                IsLive = isLive
            };

            if (oddsDoc.RootElement.TryGetProperty("bookmakerOdds", out var bookmakerOdds))
            {
                foreach (var bookmaker in bookmakerOdds.EnumerateObject())
                {
                    var bmName = bookmaker.Name;
                    
                    if (bookmaker.Value.TryGetProperty("markets", out var markets))
                    {
                        foreach (var market in markets.EnumerateObject())
                        {
                            var mId = market.Name;
                            
                            string baseName = rawMarketIdToBaseName.ContainsKey(mId) ? rawMarketIdToBaseName[mId] : $"Market {mId}";
                            
                            // Initialize market dict if not exist
                            if (!result.BookmakerOdds.ContainsKey(baseName))
                            {
                                result.BookmakerOdds[baseName] = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
                                if (baseMarketDict.TryGetValue(baseName, out var mObj))
                                {
                                    result.Markets.Add(mObj);
                                }
                                else
                                {
                                    result.Markets.Add(new BettingApp.Models.OddsPapiMarket { MarketId = baseName, MarketName = baseName });
                                }
                            }
                            
                            if (!result.BookmakerOdds[baseName].ContainsKey(bmName))
                            {
                                result.BookmakerOdds[baseName][bmName] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                            }

                            if (market.Value.TryGetProperty("outcomes", out var outcomes))
                            {
                                foreach (var outcome in outcomes.EnumerateObject())
                                {
                                    var oId = outcome.Name;
                                    string oName = oId;
                                    if (baseMarketDict.TryGetValue(baseName, out var bmObj) && bmObj.OutcomeNames.TryGetValue(oId, out var mappedName))
                                    {
                                        oName = mappedName;
                                    }

                                    if (outcome.Value.TryGetProperty("players", out var players))
                                    {
                                        if (players.TryGetProperty("0", out var playerZero))
                                        {
                                            if (playerZero.TryGetProperty("price", out var price))
                                            {
                                                result.BookmakerOdds[baseName][bmName][oName] = price.GetDouble();
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            
            // Deduplicate outcome names and sort markets by name
            foreach (var market in result.Markets)
            {
                market.OutcomeNames = market.OutcomeNames.GroupBy(x => x.Value).Select(g => g.First()).ToDictionary(x => x.Key, x => x.Value);
            }
            result.Markets.Sort((a, b) => a.MarketName.CompareTo(b.MarketName));

            return (result, null);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] Exception in SearchOddsComparisonAsync: {ex.Message}");
            return (null, $"Exception: {ex.Message}");
        }
    }

    private bool IsNameMatch(string source, string target)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target)) return false;
        
        // Remove common diacritics / normalize
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

        return result.Replace("ø", "o")
                   .Replace("æ", "a")
                   .Replace("å", "a")
                   .Replace("oe", "o")
                   .Replace("ae", "a")
                   .Replace("aa", "a")
                   .Replace(" fc", "")
                   .Replace("fk ", "")
                   .Replace(" united", "")
                   .Replace(" city", "")
                   .Replace("cf ", "")
                   .Replace(" cd", "")
                   .Replace("bk ", "")
                   .Replace(" (w)", "")
                   .Replace(" women", "")
                   .Trim();
    }
}
