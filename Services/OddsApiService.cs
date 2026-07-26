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




    public async Task<(BettingApp.Models.OddsPapiSearchResult? Result, string? Error)> SearchOddsComparisonAsync(string teamName, int? betId = null)
    {
        if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrWhiteSpace(teamName)) return (null, "API Key is missing or team name is empty.");

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
                if (mDoc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var m in mDoc.RootElement.EnumerateArray())
                    {
                        var mId = m.GetProperty("marketId").ToString();
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
            string toDate = DateTime.UtcNow.AddDays(6).ToString("yyyy-MM-dd");
            var fixturesUrl = $"https://api.oddspapi.io/v4/fixtures?apiKey={_apiKey}&sportId=10&from={fromDate}&to={toDate}";
            
            if (!_cache.TryGetValue($"OddspapiFixtures_{fromDate}", out string? fJson))
            {
                string betLabel = betId.HasValue ? $"[Bet #{betId.Value}]" : "[Manual Lookup]";
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

            if (!bestMatches.Any()) return (null, $"No matching fixtures found for '{teamName}' in the next 7 days.");
            
            var bestFixture = bestMatches.OrderByDescending(m => m.score).First().fixture;
            
            fixtureId = bestFixture.GetProperty("fixtureId").ToString();
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
    
    private string NormalizeTeamName(string name)
    {
        return name.ToLowerInvariant()
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
