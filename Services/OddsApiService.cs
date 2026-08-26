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
                using var mResp = await _httpClient.GetAsync(marketsUrl);
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

                using var fResp = await _httpClient.GetAsync(fixturesUrl);
                if (!fResp.IsSuccessStatusCode) return (null, $"Fixtures API returned status {fResp.StatusCode}");
                
                fJson = await fResp.Content.ReadAsStringAsync();
                _cache.Set($"OddspapiFixtures_{fromDate}", fJson, TimeSpan.FromHours(6));
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

            string normHomeTeam = NormalizeTeamName(homeTeam);
            string normAwayTeam = string.IsNullOrEmpty(awayTeam) ? "" : NormalizeTeamName(awayTeam);
            var normHomeTokens = homeTokens.Select(t => NormalizeTeamName(t)).ToList();
            var normAwayTokens = awayTokens.Select(t => NormalizeTeamName(t)).ToList();

            var bestMatches = new List<(JsonElement fixture, int score)>();

            foreach (var f in doc.RootElement.EnumerateArray())
            {
                string p1 = f.TryGetProperty("participant1Name", out var p1n) ? (p1n.GetString() ?? "") : "";
                string p2 = f.TryGetProperty("participant2Name", out var p2n) ? (p2n.GetString() ?? "") : "";
                
                // Skip Simulated Reality Leagues (SRL) and e-soccer matches which pollute OddsPapi
                if (p1.EndsWith(" SRL", StringComparison.OrdinalIgnoreCase) || p2.EndsWith(" SRL", StringComparison.OrdinalIgnoreCase) ||
                    p1.Contains(" SRL ", StringComparison.OrdinalIgnoreCase) || p2.Contains(" SRL ", StringComparison.OrdinalIgnoreCase) ||
                    p1.Contains("Esoccer", StringComparison.OrdinalIgnoreCase) || p2.Contains("Esoccer", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                
                string normP1 = NormalizeTeamName(p1);
                string normP2 = NormalizeTeamName(p2);
                
                bool homeMatch = normHomeTokens.Any(t => IsNameMatch(normP1, t, true) || IsNameMatch(normP2, t, true));
                bool awayMatch = string.IsNullOrEmpty(normAwayTeam) || normAwayTokens.Any(t => IsNameMatch(normP1, t, true) || IsNameMatch(normP2, t, true));

                bool homeExact = IsExactMatch(normP1, normHomeTeam, true) || IsExactMatch(normP2, normHomeTeam, true);
                bool awayExact = !string.IsNullOrEmpty(normAwayTeam) && (IsExactMatch(normP1, normAwayTeam, true) || IsExactMatch(normP2, normAwayTeam, true));

                // Strict rule: Must match at least one token from BOTH sides!
                if (!homeMatch || !awayMatch)
                {
                    if (!homeMatch && awayMatch)
                    {
                        bool p1IsAway = IsNameMatch(normP1, normAwayTeam, true) || normAwayTokens.Any(t => IsNameMatch(normP1, t, true));
                        string normOtherTeam = p1IsAway ? normP2 : normP1;
                        if (ComputeLevenshteinDistance(normOtherTeam, normHomeTeam) > 3) continue;
                    }
                    else if (!awayMatch && homeMatch)
                    {
                        bool p1IsHome = IsNameMatch(normP1, normHomeTeam, true) || normHomeTokens.Any(t => IsNameMatch(normP1, t, true));
                        string normOtherTeam = p1IsHome ? normP2 : normP1;
                        if (ComputeLevenshteinDistance(normOtherTeam, normAwayTeam) > 3) continue;
                    }
                    else
                    {
                        continue; 
                    }
                }

                int score = 0;
                
                if (IsNameMatch(normP1, normHomeTeam, true) || IsNameMatch(normP2, normHomeTeam, true)) score += 50;
                if (!string.IsNullOrEmpty(normAwayTeam) && (IsNameMatch(normP1, normAwayTeam, true) || IsNameMatch(normP2, normAwayTeam, true))) score += 50;
                
                // Huge bonus for exact match to differentiate "Team" from "Team 2"
                if (IsExactMatch(normP1, normHomeTeam, true) || IsExactMatch(normP2, normHomeTeam, true)) score += 100;
                if (!string.IsNullOrEmpty(normAwayTeam) && (IsExactMatch(normP1, normAwayTeam, true) || IsExactMatch(normP2, normAwayTeam, true))) score += 100;

                foreach (var token in normHomeTokens.Concat(normAwayTokens))
                {
                    if (IsNameMatch(normP1, token, true) || IsNameMatch(normP2, token, true)) score += 10;
                    
                    if ((token.Contains("kobenhavn", StringComparison.OrdinalIgnoreCase) || token.Contains("copenhagen", StringComparison.OrdinalIgnoreCase)) && 
                        (IsNameMatch(normP1, "copenhagen", true) || IsNameMatch(normP2, "copenhagen", true)))
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
                oResp.Dispose();
                await Task.Delay(1000); // Wait 1s
                oResp = await _httpClient.GetAsync(oddsUrl);
                if (!oResp.IsSuccessStatusCode)
                {
                    oResp.Dispose();
                    return (null, $"Odds API returned status {oResp.StatusCode}");
                }
            }

            var oJson = await oResp.Content.ReadAsStringAsync();
            oResp.Dispose();
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
                    
                    if (bookmaker.Value.TryGetProperty("fixturePath", out var fixturePathProp) && fixturePathProp.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var url = fixturePathProp.GetString();
                        if (!string.IsNullOrEmpty(url))
                        {
                            result.BookmakerUrls[bmName] = url;
                        }
                    }
                    
                    if (bookmaker.Value.TryGetProperty("markets", out var markets))
                    {
                        foreach (var market in markets.EnumerateObject())
                        {
                            var mId = market.Name;
                            
                            string baseName = rawMarketIdToBaseName.ContainsKey(mId) ? rawMarketIdToBaseName[mId] : $"Market {mId}";
                            
                            // Initialize market dict if not exist
                            if (!result.BookmakerOdds.ContainsKey(baseName))
                            {
                                result.BookmakerOdds[baseName] = new Dictionary<string, Dictionary<string, BettingApp.Models.OddsData>>(StringComparer.OrdinalIgnoreCase);
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
                                result.BookmakerOdds[baseName][bmName] = new Dictionary<string, BettingApp.Models.OddsData>(StringComparer.OrdinalIgnoreCase);
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
                                        foreach (var playerProp in players.EnumerateObject())
                                        {
                                            if (playerProp.Value.TryGetProperty("price", out var price))
                                            {
                                                var oddsData = new BettingApp.Models.OddsData { Price = price.GetDouble() };
                                                if (playerProp.Value.TryGetProperty("changedAt", out var changedAtProp) && changedAtProp.ValueKind == System.Text.Json.JsonValueKind.String)
                                                {
                                                    if (DateTime.TryParse(changedAtProp.GetString(), out var changedAt))
                                                    {
                                                        oddsData.ChangedAt = changedAt;
                                                    }
                                                }

                                                string finalOName = oName;
                                                string? pName = "";
                                                if (playerProp.Value.TryGetProperty("playerName", out var pNameProp) && pNameProp.ValueKind == System.Text.Json.JsonValueKind.String)
                                                {
                                                    pName = pNameProp.GetString();
                                                }
                                                else if (playerProp.Value.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == System.Text.Json.JsonValueKind.String)
                                                {
                                                    pName = nameProp.GetString();
                                                }
                                                else if (playerProp.Value.TryGetProperty("participantName", out var partNameProp) && partNameProp.ValueKind == System.Text.Json.JsonValueKind.String)
                                                {
                                                    pName = partNameProp.GetString();
                                                }

                                                if (!string.IsNullOrWhiteSpace(pName))
                                                {
                                                    finalOName = $"{oName} ({pName})";
                                                }

                                                result.BookmakerOdds[baseName][bmName][finalOName] = oddsData;
                                                
                                                // Also make sure to add it to OutcomeNames so it shows up in UI tables
                                                if (baseMarketDict.TryGetValue(baseName, out var baseMarketObj) && !baseMarketObj.OutcomeNames.ContainsKey(finalOName))
                                                {
                                                    baseMarketObj.OutcomeNames[finalOName] = finalOName;
                                                }
                                                else if (!baseMarketDict.TryGetValue(baseName, out _) && !result.Markets.Any(m => m.MarketName == baseName && m.OutcomeNames.ContainsKey(finalOName)))
                                                {
                                                    var marketObj = result.Markets.FirstOrDefault(m => m.MarketName == baseName);
                                                    if (marketObj != null && !marketObj.OutcomeNames.ContainsKey(finalOName))
                                                    {
                                                        marketObj.OutcomeNames[finalOName] = finalOName;
                                                    }
                                                }
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
                market.OutcomeNames = market.OutcomeNames
                    .GroupBy(x => x.Value)
                    .Select(g => g.First())
                    .OrderBy(x => 
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(x.Value, @"\(([^,()]+),\s*([^()]+)\)");
                        return match.Success ? match.Value : x.Value;
                    })
                    .ThenBy(x => x.Value.Contains("Under") ? 1 : 0)
                    .ThenBy(x => 
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(x.Value, @"\d+(?:\.\d+)?");
                        return m.Success ? double.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
                    })
                    .ToDictionary(x => x.Key, x => x.Value);
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

    private bool IsNameMatch(string source, string target, bool isNormalized = false)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target)) return false;
        
        // Remove common diacritics / normalize
        string normalizedSource = isNormalized ? source : NormalizeTeamName(source);
        string normalizedTarget = isNormalized ? target : NormalizeTeamName(target);
        
        if (string.IsNullOrEmpty(normalizedSource) || string.IsNullOrEmpty(normalizedTarget)) return false;
        
        return normalizedSource.Contains(normalizedTarget, StringComparison.OrdinalIgnoreCase) || 
               normalizedTarget.Contains(normalizedSource, StringComparison.OrdinalIgnoreCase);
    }
    
    private bool IsExactMatch(string source, string target, bool isNormalized = false)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target)) return false;
        string normalizedSource = isNormalized ? source : NormalizeTeamName(source);
        string normalizedTarget = isNormalized ? target : NormalizeTeamName(target);
        return normalizedSource.Equals(normalizedTarget, StringComparison.OrdinalIgnoreCase);
    }
    
    private string NormalizeTeamName(string name)
    {
        return _teamAliasMappingService.NormalizeTeamName(name, removeStopWords: true);
    }
    
    private int ComputeLevenshteinDistance(string s, string t)
    {
        if (string.IsNullOrEmpty(s)) return string.IsNullOrEmpty(t) ? 0 : t.Length;
        if (string.IsNullOrEmpty(t)) return s.Length;

        int[] v0 = new int[t.Length + 1];
        int[] v1 = new int[t.Length + 1];

        for (int i = 0; i < v0.Length; i++) v0[i] = i;

        for (int i = 0; i < s.Length; i++)
        {
            v1[0] = i + 1;
            for (int j = 0; j < t.Length; j++)
            {
                int cost = (s[i] == t[j]) ? 0 : 1;
                v1[j + 1] = Math.Min(Math.Min(v1[j] + 1, v0[j + 1] + 1), v0[j] + cost);
            }
            for (int j = 0; j < v0.Length; j++) v0[j] = v1[j];
        }

        return v1[t.Length];
    }
}
