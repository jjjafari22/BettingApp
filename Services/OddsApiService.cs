using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace BettingApp.Services;

public class OddsApiService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly IMemoryCache _cache;

    public OddsApiService(HttpClient httpClient, IConfiguration config, IMemoryCache cache)
    {
        _httpClient = httpClient;
        _apiKey = config["OddsApi:ApiKey"] ?? "";
        _cache = cache;
    }

    public async Task<string?> GetMatchScoreJsonAsync(string matchName, DateTime betPlacedAt)
    {
        if (string.IsNullOrEmpty(_apiKey)) return null;

        try
        {
            string[] split = matchName.Split(new[] { " vs ", " - ", " v " }, StringSplitOptions.None);
            string homeTeam = NormalizeTeamName(split[0].Trim());
            string awayTeam = split.Length > 1 ? NormalizeTeamName(split[1].Trim()) : "";

            var awayTeamTokens = string.IsNullOrEmpty(awayTeam) ? Array.Empty<string>() : 
                awayTeam.Split(new[] { ' ', '-', '/' }, StringSplitOptions.RemoveEmptyEntries)
                        .Where(w => w.Length >= 3)
                        .ToArray();

            if (awayTeamTokens.Length == 0 && !string.IsNullOrEmpty(awayTeam))
            {
                awayTeamTokens = new[] { awayTeam };
            }

            string fromDate = betPlacedAt.AddDays(-3).ToString("yyyy-MM-dd");
            string toDate = betPlacedAt.AddDays(4).ToString("yyyy-MM-dd");

            // We include sportId=10 (Soccer) because without it, the API limits date ranges to 2 days.
            var searchUrl = $"https://api.oddspapi.io/v4/fixtures?apiKey={_apiKey}&sportId=10&from={fromDate}&to={toDate}";
            
            if (!_cache.TryGetValue($"OddspapiFixtures_{fromDate}", out string? json))
            {
                var response = await _httpClient.GetAsync(searchUrl);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Oddspapi Fixtures Error: {response.StatusCode}");
                    return null;
                }

                json = await response.Content.ReadAsStringAsync();
                _cache.Set($"OddspapiFixtures_{fromDate}", json, TimeSpan.FromHours(1));
            }
            using var doc = JsonDocument.Parse(json ?? "[]");
            
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                Console.WriteLine($"Oddspapi fixtures returned non-array: {json}");
                return "{\"error\": \"Oddspapi fixtures did not return an array. Possible rate limit or API error.\"}";
            }

            string? bestFixtureId = null;
            JsonElement bestFixture = default;
            
            foreach (var fixture in doc.RootElement.EnumerateArray())
            {
                string p1Name = fixture.TryGetProperty("participant1Name", out var p1n) ? p1n.GetString() ?? "" : "";
                string p2Name = fixture.TryGetProperty("participant2Name", out var p2n) ? p2n.GetString() ?? "" : "";

                bool team1MatchesP1 = IsNameMatch(p1Name, homeTeam);
                bool team1MatchesP2 = IsNameMatch(p2Name, homeTeam);

                bool isMatch = false;

                if (team1MatchesP1)
                {
                    bool team2MatchesP2 = awayTeamTokens.Length == 0;
                    foreach (var token in awayTeamTokens)
                    {
                        if (IsNameMatch(p2Name, token))
                        {
                            team2MatchesP2 = true;
                            break;
                        }
                    }
                    if (team2MatchesP2) isMatch = true;
                }

                if (!isMatch && team1MatchesP2)
                {
                    bool team2MatchesP1 = awayTeamTokens.Length == 0;
                    foreach (var token in awayTeamTokens)
                    {
                        if (IsNameMatch(p1Name, token))
                        {
                            team2MatchesP1 = true;
                            break;
                        }
                    }
                    if (team2MatchesP1) isMatch = true;
                }

                if (isMatch)
                {
                    if (fixture.TryGetProperty("fixtureId", out var fid))
                    {
                        bestFixtureId = fid.GetString() ?? "";
                        bestFixture = fixture;
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(bestFixtureId))
            {
                Console.WriteLine($"Could not find match {matchName} in Oddspapi fixtures");
                return "{\"error\": \"Match not found in Oddspapi fixtures\"}";
            }

            string startTime = bestFixture.TryGetProperty("startTime", out var st) ? (st.GetString() ?? "Unknown") : "Unknown";
            string status = bestFixture.TryGetProperty("statusName", out var sn) ? (sn.GetString() ?? "Unknown") : "Unknown";
            
            return $"{{\"OddsPapiFixtureInfo\": {{\"status\": \"{status}\", \"startTime\": \"{startTime}\"}}}}";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception in GetMatchScoreJsonAsync: {ex.Message}");
            return null;
        }
    }

    private string NormalizeTeamName(string name)
    {
        // Removed destructive normalization so we can test variants in IsNameMatch
        if (string.IsNullOrEmpty(name)) return name;
        return name;
    }

    private bool IsNameMatch(string apiName, string searchName)
    {
        if (string.IsNullOrEmpty(searchName) || string.IsNullOrEmpty(apiName)) return false;
        
        if (apiName.Contains(searchName, StringComparison.OrdinalIgnoreCase) || searchName.Contains(apiName, StringComparison.OrdinalIgnoreCase)) return true;
        
        string v1 = searchName.Replace("ø", "o").Replace("Ø", "O").Replace("æ", "ae").Replace("Æ", "Ae").Replace("å", "aa").Replace("Å", "Aa");
        string apiv1 = apiName.Replace("ø", "o").Replace("Ø", "O").Replace("æ", "ae").Replace("Æ", "Ae").Replace("å", "aa").Replace("Å", "Aa");
        if (apiv1.Contains(v1, StringComparison.OrdinalIgnoreCase) || v1.Contains(apiv1, StringComparison.OrdinalIgnoreCase)) return true;

        string v2 = searchName.Replace("ø", "oe").Replace("Ø", "Oe").Replace("æ", "a").Replace("Æ", "A").Replace("å", "a").Replace("Å", "A");
        string apiv2 = apiName.Replace("ø", "oe").Replace("Ø", "Oe").Replace("æ", "a").Replace("Æ", "A").Replace("å", "a").Replace("Å", "A");
        if (apiv2.Contains(v2, StringComparison.OrdinalIgnoreCase) || v2.Contains(apiv2, StringComparison.OrdinalIgnoreCase)) return true;
        
        return false;
    }

    public async Task<DateTime?> GetEarliestMatchStartTimeAsync(string extractedBetDataJson, DateTime betPlacedAt)
    {
        if (string.IsNullOrEmpty(_apiKey)) return null;

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var extractionResult = JsonSerializer.Deserialize<AiVisionExtractionResult>(extractedBetDataJson, options);
            
            if (extractionResult?.Legs == null || !extractionResult.Legs.Any()) return null;

            DateTime? earliest = null;
            var uniqueMatches = extractionResult.Legs.Select(l => l.Match).Distinct();

            foreach (var matchName in uniqueMatches)
            {
                var rawJson = await GetMatchScoreJsonAsync(matchName, betPlacedAt);
                if (!string.IsNullOrEmpty(rawJson) && rawJson.Contains("\"startTime\":"))
                {
                    try 
                    {
                        using var doc = JsonDocument.Parse(rawJson);
                        if (doc.RootElement.TryGetProperty("OddsPapiFixtureInfo", out var info))
                        {
                            if (info.TryGetProperty("startTime", out var st))
                            {
                                var stStr = st.GetString();
                                if (DateTime.TryParse(stStr, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsedDt))
                                {
                                    if (earliest == null || parsedDt < earliest)
                                    {
                                        earliest = parsedDt;
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }
                await Task.Delay(500); // Respect rate limit
            }

            return earliest;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception in GetEarliestMatchStartTimeAsync: {ex.Message}");
            return null;
        }
    }



    public async Task<BettingApp.Models.OddsPapiSearchResult?> SearchOddsComparisonAsync(string teamName)
    {
        if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrWhiteSpace(teamName)) return null;

        try
        {
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
                foreach (var m in mDoc.RootElement.EnumerateArray())
                {
                    var mId = m.GetProperty("marketId").GetRawText();
                    var baseName = m.TryGetProperty("marketName", out var mn) ? mn.GetString() ?? "Unknown" : "Unknown";
                    
                    rawMarketIdToBaseName[mId] = baseName;
                    
                    if (!baseMarketDict.ContainsKey(baseName))
                    {
                        baseMarketDict[baseName] = new BettingApp.Models.OddsPapiMarket { MarketId = baseName, MarketName = baseName };
                    }
                    
                    var baseMarketObj = baseMarketDict[baseName];
                    
                    string handicapSuffix = "";
                    if (m.TryGetProperty("handicap", out var hc) && hc.ValueKind == JsonValueKind.Number)
                    {
                        handicapSuffix = $" {hc.GetDouble()}";
                    }
                    
                    if (m.TryGetProperty("outcomes", out var outcomes))
                    {
                        foreach (var o in outcomes.EnumerateArray())
                        {
                            var oId = o.GetProperty("outcomeId").GetRawText();
                            var oName = o.TryGetProperty("outcomeName", out var on) ? on.GetString() ?? "" : "";
                            baseMarketObj.OutcomeNames[oId] = oName + handicapSuffix;
                        }
                    }
                }
            }

            // 2. Find Fixture
            string fromDate = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd");
            string toDate = DateTime.UtcNow.AddDays(7).ToString("yyyy-MM-dd");
            var fixturesUrl = $"https://api.oddspapi.io/v4/fixtures?apiKey={_apiKey}&sportId=10&from={fromDate}&to={toDate}";
            
            if (!_cache.TryGetValue($"OddspapiFixtures_{fromDate}", out string? fJson))
            {
                var fResp = await _httpClient.GetAsync(fixturesUrl);
                if (!fResp.IsSuccessStatusCode) return null;
                
                fJson = await fResp.Content.ReadAsStringAsync();
                _cache.Set($"OddspapiFixtures_{fromDate}", fJson, TimeSpan.FromHours(1));
            }
            
            using var doc = JsonDocument.Parse(fJson ?? "[]");
            
            string? fixtureId = null;
            string matchName = "";
            DateTime startTime = DateTime.MinValue;

            string cleanTeamName = teamName.Replace(" vs ", " ", StringComparison.OrdinalIgnoreCase).Replace(" v ", " ", StringComparison.OrdinalIgnoreCase);
            var searchTokens = cleanTeamName.Split(new[] { ' ', '-', '.' }, StringSplitOptions.RemoveEmptyEntries)
                                       .Where(w => w.Length >= 3 && !w.Equals("the", StringComparison.OrdinalIgnoreCase) && !w.Equals("and", StringComparison.OrdinalIgnoreCase))
                                       .ToList();
                                       
            var bestMatches = new List<(JsonElement fixture, int score)>();

            foreach (var f in doc.RootElement.EnumerateArray())
            {
                string p1 = f.TryGetProperty("participant1Name", out var p1n) ? (p1n.GetString() ?? "") : "";
                string p2 = f.TryGetProperty("participant2Name", out var p2n) ? (p2n.GetString() ?? "") : "";
                
                int score = 0;
                
                // Exact substring match gives high score
                if (IsNameMatch(p1, teamName) || IsNameMatch(p2, teamName))
                {
                    score += 100;
                }
                
                // Token matches
                foreach (var token in searchTokens)
                {
                    if (IsNameMatch(p1, token) || IsNameMatch(p2, token)) score += 10;
                    
                    // Special case for Copenhagen/Kobenhavn/København
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

            if (!bestMatches.Any()) return null;
            
            var bestFixture = bestMatches.OrderByDescending(m => m.score).First().fixture;
            
            fixtureId = bestFixture.GetProperty("fixtureId").GetString() ?? "";
            string finalP1 = bestFixture.TryGetProperty("participant1Name", out var fp1) ? (fp1.GetString() ?? "") : "";
            string finalP2 = bestFixture.TryGetProperty("participant2Name", out var fp2) ? (fp2.GetString() ?? "") : "";
            matchName = $"{finalP1} vs {finalP2}";
            
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
            }

            // 3. Fetch Odds for Unibet, Coolbet, Bet365
            var oddsUrl = $"https://api.oddspapi.io/v4/odds?apiKey={_apiKey}&fixtureId={fixtureId}&bookmakers=unibet,coolbet,bet365";
            var oResp = await _httpClient.GetAsync(oddsUrl);
            
            // Retry once if rate limited
            if (!oResp.IsSuccessStatusCode)
            {
                await Task.Delay(1000); // Wait 1s
                oResp = await _httpClient.GetAsync(oddsUrl);
                if (!oResp.IsSuccessStatusCode) return null;
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
                                    if (outcome.Value.TryGetProperty("players", out var players))
                                    {
                                        if (players.TryGetProperty("0", out var playerZero))
                                        {
                                            if (playerZero.TryGetProperty("price", out var price))
                                            {
                                                result.BookmakerOdds[baseName][bmName][oId] = price.GetDouble();
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            
            // Sort markets by name
            result.Markets.Sort((a, b) => a.MarketName.CompareTo(b.MarketName));

            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception in SearchOddsComparisonAsync: {ex.Message}");
            return null;
        }
    }
}
