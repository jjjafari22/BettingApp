using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace BettingApp.Services;

public class OddsApiService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly IMemoryCache _cache;
    private readonly TeamAliasMappingService _teamAliasMappingService;
    private readonly ILogger<OddsApiService> _logger;
    private static readonly SemaphoreSlim _apiRateLimiter = new SemaphoreSlim(10, 10);

    public OddsApiService(HttpClient httpClient, IConfiguration config, IMemoryCache cache, TeamAliasMappingService teamAliasMappingService, ILogger<OddsApiService> logger)
    {
        _httpClient = httpClient;
        _apiKey = config["OddsApi:ApiKey"] ?? "";
        _cache = cache;
        _teamAliasMappingService = teamAliasMappingService;
        _logger = logger;
    }




        private async Task<(string? Json, string CacheKey)> GetFixturesJsonAsync()
    {
        string fromDate = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd");
        string toDate = DateTime.UtcNow.AddDays(4).ToString("yyyy-MM-dd");
        string cacheKey = $"OddspapiFixtures_{fromDate}";
        var fixturesUrl = $"https://api.oddspapi.io/v4/fixtures?apiKey={_apiKey}&sportId=10&from={fromDate}&to={toDate}";
        
        string? json = await HttpCacheHelper.GetOrCreateAsync(_cache, cacheKey, TimeSpan.FromHours(6), () => _httpClient.GetAsync(fixturesUrl), _logger);
        return (json, cacheKey);
    }

    private async Task<string?> GetMarketsJsonAsync()
    {
        var marketsUrl = $"https://api.oddspapi.io/v4/markets?apiKey={_apiKey}&language=en";
        return await HttpCacheHelper.GetOrCreateAsync(_cache, "OddspapiMarketsJson", TimeSpan.FromHours(24), () => _httpClient.GetAsync(marketsUrl), _logger);
    }


    private async Task<List<BettingApp.Models.OddsPapiFixtureDto>?> GetParsedFixturesAsync()
    {
        var (fJson, cacheKey) = await GetFixturesJsonAsync();
                
        if (fJson == null) return null;
                
        if (!_cache.TryGetValue(cacheKey + "_Parsed", out List<BettingApp.Models.OddsPapiFixtureDto>? parsedFixtures) || parsedFixtures == null)
                {
            using var doc = JsonDocument.Parse(fJson ?? "[]");
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

            parsedFixtures = new List<BettingApp.Models.OddsPapiFixtureDto>();
            foreach (var f in doc.RootElement.EnumerateArray())
                    {
                var dto = new BettingApp.Models.OddsPapiFixtureDto
                {
                            FixtureId = f.TryGetProperty("fixtureId", out var fid) ? fid.ToString() : "",
                            Participant1Name = f.TryGetProperty("participant1Name", out var p1n) ? (p1n.GetString() ?? "") : "",
                            Participant2Name = f.TryGetProperty("participant2Name", out var p2n) ? (p2n.GetString() ?? "") : "",
                            TournamentName = f.TryGetProperty("tournamentName", out var tn) ? (tn.GetString() ?? "") : "",
                            StatusId = f.TryGetProperty("statusId", out var sid) && sid.ValueKind == JsonValueKind.Number ? sid.GetInt32() : -1
        };
                        
                dto.NormP1 = NormalizeTeamName(dto.Participant1Name);
                dto.NormP2 = NormalizeTeamName(dto.Participant2Name);
                dto.HasModifier = HasSpecialModifier(dto.Participant1Name) || HasSpecialModifier(dto.Participant2Name) || HasSpecialModifier(dto.TournamentName) || dto.Participant1Name.Contains("Esoccer", StringComparison.OrdinalIgnoreCase) || dto.Participant2Name.Contains("Esoccer", StringComparison.OrdinalIgnoreCase);

                if (f.TryGetProperty("startTime", out var st) && DateTime.TryParse(st.GetString(), null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var dt))
                {
                    dto.StartTime = dt;
        }
                        
                if (f.TryGetProperty("externalProviders", out var ep) && ep.TryGetProperty("flashscoreId", out var fsid) && fsid.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    dto.FlashscoreId = fsid.GetString();
        }
                        
                parsedFixtures.Add(dto);
            }
            _cache.Set(cacheKey + "_Parsed", parsedFixtures, TimeSpan.FromHours(6));
        }
        return parsedFixtures;
    }

    private async Task<(Dictionary<string, string> rawMarketIdToBaseName, Dictionary<string, BettingApp.Models.OddsPapiMarket> cachedBaseMarketDict)?> GetParsedMarketsAsync()
    {
        if (!_cache.TryGetValue("OddspapiMarketsParsed", out (Dictionary<string, string> rawMarketIdToBaseName, Dictionary<string, BettingApp.Models.OddsPapiMarket> cachedBaseMarketDict) cachedMarkets))
        {
            string? mJson = await GetMarketsJsonAsync();
                
            if (mJson == null) return null;
                
            var newRawMarketIdToBaseName = new Dictionary<string, string>();
            var newBaseMarketDict = new Dictionary<string, BettingApp.Models.OddsPapiMarket>();
                
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
                        bool isPlayerGoalsMerge = false;
                        bool isPlayerAssistsMerge = false;
                        bool isPlayerTacklesMerge = false;
                        if (baseName.Equals("Player Shots On Goal (incl. overtime)", StringComparison.OrdinalIgnoreCase))
                        {
                                baseName = "Over Under Player Shots On Goal (incl. overtime)";
                    }
                        else if (baseName.Equals("Player Fouls Committed (incl. overtime)", StringComparison.OrdinalIgnoreCase))
                        {
                                baseName = "Over Under Player Fouls Committed (incl. overtime)";
                    }
                        else if (baseName.Equals("Player Shots (incl. overtime)", StringComparison.OrdinalIgnoreCase))
                        {
                                baseName = "Over Under Player Shots (incl. overtime)";
                    }
                        else if (baseName.Equals("Player Assists (incl. overtime)", StringComparison.OrdinalIgnoreCase))
                        {
                                baseName = "Over Under Player Assists (incl. overtime)";
                                isPlayerAssistsMerge = true;
                    }
                        else if (baseName.Equals("Player Tackles (incl. overtime)", StringComparison.OrdinalIgnoreCase))
                        {
                                baseName = "Over Under Player Tackles (incl. overtime)";
                                isPlayerTacklesMerge = true;
                    }
                        else if (baseName.Equals("Player Goals (incl. overtime)", StringComparison.OrdinalIgnoreCase) || 
                                     baseName.Equals("Anytime Goal Scorer", StringComparison.OrdinalIgnoreCase))
                        {
                                baseName = "Over Under Player Goals (incl. overtime)";
                                isPlayerGoalsMerge = true;
                    }

                        newRawMarketIdToBaseName[mId] = baseName;
                            
                        if (!newBaseMarketDict.ContainsKey(baseName))
                        {
                            newBaseMarketDict[baseName] = new BettingApp.Models.OddsPapiMarket { MarketId = baseName, MarketName = baseName };
                    }
                        
                        var baseMarketObj = newBaseMarketDict[baseName];
                            
                        if (m.TryGetProperty("outcomes", out var outcomes))
                        {
                            foreach (var o in outcomes.EnumerateArray())
                            {
                                var oId = o.GetProperty("outcomeId").ToString();
                                var oName = o.TryGetProperty("outcomeName", out var on) ? on.GetString() ?? "" : "";
                                    
                                var match = System.Text.RegularExpressions.Regex.Match(oName, @"^(\d+)\+$");
                                if (match.Success && int.TryParse(match.Groups[1].Value, out int num))
                                {
                                    double overVal = num - 0.5;
                                    oName = $"Over {overVal.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}";
                            }

                                if ((isPlayerGoalsMerge || isPlayerAssistsMerge || isPlayerTacklesMerge) && string.IsNullOrEmpty(handicapSuffix))
                                {
                                    if (oName == "1" || oName.Equals("Yes", StringComparison.OrdinalIgnoreCase))
                                    {
                                        oName = "Over";
                                        handicapSuffix = " 0.5";
                                }
                            }

                                string combined = oName + handicapSuffix;
                                    combined = System.Text.RegularExpressions.Regex.Replace(combined, @"^(Over|Under)\s*\(([^)]+)\)$", "$1 $2", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                baseMarketObj.OutcomeNames[oId] = combined;
                        }
                    }
                }
            }
        }
                
                cachedMarkets = (newRawMarketIdToBaseName, newBaseMarketDict);
                _cache.Set("OddspapiMarketsParsed", cachedMarkets, TimeSpan.FromHours(24));
            }
        return cachedMarkets;
    }

    public async Task WarmupCacheAsync()
    {
        _logger.LogInformation("OddsPapi: Background worker warming up cache for Fixtures (6h) and Markets (24h)...");
        try
        {
            // The background worker ensures the JSON is fetched AND actively parses it so the UI thread doesn't have to.
            await GetParsedFixturesAsync();
            await GetParsedMarketsAsync();
            
            _logger.LogInformation("OddsPapi: Cache warmup completed successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OddsPapi: Failed during background cache warmup.");
        }
    }

    public async Task<(BettingApp.Models.OddsPapiSearchResult? Result, string? Error)> SearchOddsComparisonAsync(string teamName, int? betId = null, bool isLiveRequest = false, string? sport = null)
    {
        if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrWhiteSpace(teamName)) return (null, "API Key is missing or team name is empty.");
        
        if (!string.IsNullOrWhiteSpace(sport) && !sport.Contains("Soccer", StringComparison.OrdinalIgnoreCase))
        {
            return (null, $"Castle doesn't yet pay for odds for {sport}.");
        }

        await _apiRateLimiter.WaitAsync();
        try
        {
            string betLabel = betId.HasValue ? $"[Bet #{betId.Value}]" : "[Manual Lookup]";
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            
            // 2. Find Fixture
            string? fixtureId = null;
            string matchName = "";
            DateTime startTime = DateTime.MinValue;
            bool isLive = false;
            string? flashscoreId = null;
            
            bool skipParse = _cache.TryGetValue($"OddspapiMatch3_{teamName}", out (string fid, string mn, DateTime st, bool il, string? fsid) cachedMatch);
            if (skipParse)
            {
                fixtureId = cachedMatch.fid;
                matchName = cachedMatch.mn;
                startTime = cachedMatch.st;
                isLive = cachedMatch.il;
                flashscoreId = cachedMatch.fsid;
            }
            else
            {
                var parsedFixtures = await GetParsedFixturesAsync();
                if (parsedFixtures == null) return (null, "Fixtures API failed or returned 429");

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

            var bestMatches = new List<(BettingApp.Models.OddsPapiFixtureDto fixture, int score)>();

            foreach (var f in parsedFixtures)
            {
                // Skip Youth/Women's/SRL matches if they weren't explicitly requested
                bool qHasMod = HasSpecialModifier(homeTeam) || HasSpecialModifier(awayTeam);
                if (!qHasMod && f.HasModifier)
                {
                    continue;
                }
                
                string normP1 = f.NormP1;
                string normP2 = f.NormP2;
                
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
                _logger.LogInformation($" {betLabel} OddsPapi: Could not find match against '{teamName}'");
                return (null, $"No matching fixtures found for '{teamName}' in the next 7 days.");
            }
            
            var bestFixture = bestMatches.OrderByDescending(m => m.score).First().fixture;
            
            fixtureId = bestFixture.FixtureId;
            string finalP1 = bestFixture.Participant1Name;
            string finalP2 = bestFixture.Participant2Name;
            matchName = $"{finalP1} vs {finalP2}";

            _logger.LogInformation($" {betLabel} OddsPapi: Found Match ID {fixtureId} for {matchName}");
            
            if (bestFixture.StartTime.HasValue)
            {
                startTime = bestFixture.StartTime.Value;
            }
            
            int statusId = bestFixture.StatusId;
            if (statusId > 0 && statusId != 3)
            {
                isLive = true;
            }
            else if (statusId == 0 && startTime <= DateTime.UtcNow)
            {
                // OddsPapi may be slow to update statusId to 1; if it's past start time and still 0 (pre-game), treat as live
                isLive = true;
            }

            flashscoreId = bestFixture.FlashscoreId;
            
            _cache.Set($"OddspapiMatch3_{teamName}", (fixtureId, matchName, startTime, isLive, flashscoreId), TimeSpan.FromHours(12));
            }
            if (_cache.TryGetValue($"OddspapiParsedResult_{fixtureId}", out BettingApp.Models.OddsPapiSearchResult? cachedResult) && cachedResult != null)
            {
                return (cachedResult, null);
            }
            
            // 1. Get markets metadata to map IDs to Names
            var parsedMarkets = await GetParsedMarketsAsync();
            if (parsedMarkets == null) return (null, "Markets API failed or returned 429");
            var cachedMarkets = parsedMarkets.Value;

            var rawMarketIdToBaseName = cachedMarkets.rawMarketIdToBaseName;
            var baseMarketDict = new Dictionary<string, BettingApp.Models.OddsPapiMarket>();
            foreach (var kvp in cachedMarkets.cachedBaseMarketDict)
            {
                baseMarketDict[kvp.Key] = new BettingApp.Models.OddsPapiMarket 
                { 
                    MarketId = kvp.Value.MarketId, 
                    MarketName = kvp.Value.MarketName, 
                    OutcomeNames = new Dictionary<string, string>(kvp.Value.OutcomeNames, StringComparer.OrdinalIgnoreCase) 
                };
            }

            // 3. Fetch Odds for Unibet SE, Betsson, Bet365, Pinnacle, Coolbet
            var oddsUrl = $"https://api.oddspapi.io/v4/odds?apiKey={_apiKey}&fixtureId={fixtureId}&bookmakers=unibet.se,betsson,bet365,pinnacle%2B30,coolbet";
            
            string cacheKeyOdds = $"OddspapiOdds_{fixtureId}";
            TimeSpan cacheDuration = isLiveRequest ? TimeSpan.Zero : TimeSpan.FromSeconds(75);
            string? oJson = await HttpCacheHelper.GetOrCreateAsync(_cache, cacheKeyOdds, cacheDuration, () => _httpClient.GetAsync(oddsUrl), _logger);
            
            if (oJson == null) return (null, "Odds API failed or returned 429");
            using var oddsDoc = JsonDocument.Parse(oJson ?? "{}");

            var result = new BettingApp.Models.OddsPapiSearchResult
            {
                MatchName = matchName,
                StartTime = startTime,
                IsLive = isLive,
                FlashscoreId = flashscoreId
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

                                                if (result.BookmakerUrls.TryGetValue(bmName, out var fixtureUrl))
                                                {
                                                    oddsData.BetslipUrl = fixtureUrl;
                                                }

                                                bool pActive = !playerProp.Value.TryGetProperty("active", out var aProp) || aProp.ValueKind != System.Text.Json.JsonValueKind.False;
                                                bool bmSuspended = bookmaker.Value.TryGetProperty("suspended", out var sProp) && sProp.ValueKind == System.Text.Json.JsonValueKind.True;
                                                bool mActive = !market.Value.TryGetProperty("marketActive", out var mProp) || mProp.ValueKind != System.Text.Json.JsonValueKind.False;
                                                
                                                oddsData.IsSuspended = bmSuspended || !mActive || !pActive;

                                                if (playerProp.Value.TryGetProperty("limit", out var limitProp) && limitProp.ValueKind == System.Text.Json.JsonValueKind.Number)
                                                {
                                                    oddsData.Limit = limitProp.GetDouble();
                                                }

                                                if (playerProp.Value.TryGetProperty("changedAt", out var changedAtProp) && changedAtProp.ValueKind == System.Text.Json.JsonValueKind.String)
                                                {
                                                    if (DateTime.TryParse(changedAtProp.GetString(), null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var changedAt))
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
                // Prune outcomes that have no odds at all (e.g. empty generic markets)
                var activeOutcomes = market.OutcomeNames.Where(kv => 
                    result.BookmakerOdds.TryGetValue(market.MarketName, out var bmDict) && 
                    bmDict.Any(bm => bm.Value.ContainsKey(kv.Value))
                ).ToList();

                market.OutcomeNames = activeOutcomes
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
            
            _cache.Set($"OddspapiParsedResult_{fixtureId}", result, TimeSpan.FromSeconds(15));

            return (result, null);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Exception in SearchOddsComparisonAsync: {ex.Message}");
            return (null, $"Exception: {ex.Message}");
        }
        finally
        {
            _apiRateLimiter.Release();
        }
    }
    private bool HasSpecialModifier(string input)
    {
        if (string.IsNullOrEmpty(input)) return false;
        var text = input.ToLowerInvariant();
        return text.Contains("women") || text.Contains("(w)") || text.Contains("femenil") || 
               text.Contains("u21") || text.Contains("u23") || text.Contains("u19") || 
               text.Contains("u18") || text.Contains("u20") || text.Contains("reserves") || text.Contains("youth") ||
               text.Contains(" srl");
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
