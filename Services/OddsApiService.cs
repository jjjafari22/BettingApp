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
    private readonly EmailSender? _emailSender;
    private readonly MarketMappingService? _marketMapper;
    private static readonly SemaphoreSlim _apiRateLimiter = new SemaphoreSlim(10, 10);

    public OddsApiService(
        HttpClient httpClient,
        IConfiguration config,
        IMemoryCache cache,
        TeamAliasMappingService teamAliasMappingService,
        ILogger<OddsApiService> logger,
        EmailSender? emailSender = null,
        MarketMappingService? marketMapper = null)
    {
        _httpClient = httpClient;
        _apiKey = config["OddsApi:ApiKey"] ?? "";
        _cache = cache;
        _teamAliasMappingService = teamAliasMappingService;
        _logger = logger;
        _emailSender = emailSender;
        _marketMapper = marketMapper;
    }




    public static int ResolveSportId(string? sport)
    {
        if (string.IsNullOrWhiteSpace(sport)) return 10; // Default Soccer
        if (sport.Contains("American Football", StringComparison.OrdinalIgnoreCase) ||
            sport.Contains("NFL", StringComparison.OrdinalIgnoreCase)) return -1;
        if (sport.Contains("Table Tennis", StringComparison.OrdinalIgnoreCase) ||
            sport.Contains("Bordtennis", StringComparison.OrdinalIgnoreCase) ||
            sport.Contains("Ping Pong", StringComparison.OrdinalIgnoreCase)) return -1;
        if (sport.Contains("Tennis", StringComparison.OrdinalIgnoreCase)) return 12;
        if (sport.Contains("Hockey", StringComparison.OrdinalIgnoreCase)) return 15;
        if (sport.Contains("Soccer", StringComparison.OrdinalIgnoreCase) || 
            sport.Contains("Football", StringComparison.OrdinalIgnoreCase) || 
            sport.Contains("Fotball", StringComparison.OrdinalIgnoreCase)) return 10;
        return -1;
    }

    private async Task<(string? Json, string CacheKey)> GetFixturesJsonAsync(int sportId = 10, CancellationToken cancellationToken = default)
    {
        string fromDate = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd");
        string toDate = DateTime.UtcNow.AddDays(7).ToString("yyyy-MM-dd");
        string cacheKey = $"OddspapiFixtures_{sportId}_{fromDate}";
        var fixturesUrl = $"https://api.oddspapi.io/v4/fixtures?apiKey={_apiKey}&sportId={sportId}&from={fromDate}&to={toDate}";
        
        string? json = await HttpCacheHelper.GetOrCreateAsync(_cache, cacheKey, TimeSpan.FromHours(6), () => _httpClient.GetAsync(fixturesUrl, cancellationToken), _logger, cancellationToken);
        return (json, cacheKey);
    }

    private async Task<string?> GetMarketsJsonAsync(int sportId = 10, CancellationToken cancellationToken = default)
    {
        var marketsUrl = $"https://api.oddspapi.io/v4/markets?apiKey={_apiKey}&sportId={sportId}&language=en";
        return await HttpCacheHelper.GetOrCreateAsync(_cache, $"OddspapiMarketsJson_{sportId}", TimeSpan.FromHours(24), () => _httpClient.GetAsync(marketsUrl, cancellationToken), _logger, cancellationToken);
    }

    private async Task<List<BettingApp.Models.OddsPapiFixtureDto>?> GetParsedFixturesAsync(int sportId = 10, CancellationToken cancellationToken = default)
    {
        var (fJson, cacheKey) = await GetFixturesJsonAsync(sportId, cancellationToken);
                
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
                    StatusId = f.TryGetProperty("statusId", out var sid) && sid.ValueKind == JsonValueKind.Number ? sid.GetInt32() : -1,
                    HasOdds = f.TryGetProperty("hasOdds", out var ho) && ho.ValueKind == JsonValueKind.True
                };
                        
                dto.NormP1 = NormalizeTeamName(dto.Participant1Name);
                dto.NormP2 = NormalizeTeamName(dto.Participant2Name);
                dto.HasModifier = sportId == 12
                    ? HasTennisSpecialModifier(dto.Participant1Name, dto.Participant2Name, dto.TournamentName)
                    : (HasSpecialModifier(dto.Participant1Name) || HasSpecialModifier(dto.Participant2Name) || HasSpecialModifier(dto.TournamentName) || dto.Participant1Name.Contains("Esoccer", StringComparison.OrdinalIgnoreCase) || dto.Participant2Name.Contains("Esoccer", StringComparison.OrdinalIgnoreCase));

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

    private async Task<(Dictionary<string, string> rawMarketIdToBaseName, Dictionary<string, BettingApp.Models.OddsPapiMarket> cachedBaseMarketDict)?> GetParsedMarketsAsync(int sportId = 10, CancellationToken cancellationToken = default)
    {
        string parsedCacheKey = $"OddspapiMarketsParsed_{sportId}";
        if (!_cache.TryGetValue(parsedCacheKey, out (Dictionary<string, string> rawMarketIdToBaseName, Dictionary<string, BettingApp.Models.OddsPapiMarket> cachedBaseMarketDict) cachedMarkets))
        {
            string? mJson = await GetMarketsJsonAsync(sportId, cancellationToken);
                
            if (mJson == null) return null;
                
            var newRawMarketIdToBaseName = new Dictionary<string, string>();
            var newBaseMarketDict = new Dictionary<string, BettingApp.Models.OddsPapiMarket>();
            var rawCatalogMarketNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                
            if (!string.IsNullOrEmpty(mJson) && mJson != "[]")
            {
                using var mDoc = JsonDocument.Parse(mJson);
                if (mDoc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var m in mDoc.RootElement.EnumerateArray())
                    {
                        var mId = m.GetProperty("marketId").ToString();
                        var baseName = m.TryGetProperty("marketName", out var mn) ? mn.GetString() ?? "Unknown" : "Unknown";
                        if (!string.IsNullOrWhiteSpace(baseName) && !baseName.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                        {
                            rawCatalogMarketNames.Add(baseName.Trim());
                        }
                            
                        string handicapSuffix = "";
                        if (m.TryGetProperty("handicap", out var hc))
                        {
                            if (hc.ValueKind == JsonValueKind.Number && hc.GetDouble() != 0) handicapSuffix = $" ({hc.GetDouble().ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)})";
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
                        bool isPlayerPointsMerge = false;
                        bool isPlayerShotsMerge = false;
                        bool isPlayerFoulsMerge = false;

                        if (baseName.Equals("Player Shots On Goal (incl. overtime)", StringComparison.OrdinalIgnoreCase) ||
                            baseName.Equals("Player Shots on Goal", StringComparison.OrdinalIgnoreCase))
                        {
                            baseName = "Over Under Player Shots On Goal (incl. overtime)";
                            isPlayerShotsMerge = true;
                        }
                        else if (baseName.Equals("Player Fouls Committed (incl. overtime)", StringComparison.OrdinalIgnoreCase) ||
                                 baseName.Equals("Player To Commit A Foul (incl. overtime)", StringComparison.OrdinalIgnoreCase))
                        {
                            baseName = "Over Under Player Fouls Committed (incl. overtime)";
                            isPlayerFoulsMerge = true;
                        }
                        else if (baseName.Equals("Player Fouls Won (incl. overtime)", StringComparison.OrdinalIgnoreCase) ||
                                 baseName.Equals("Player To Win A Foul (incl. overtime)", StringComparison.OrdinalIgnoreCase))
                        {
                            baseName = "Over Under Player Fouls Won (incl. overtime)";
                            isPlayerFoulsMerge = true;
                        }
                        else if (baseName.Equals("Player Fouls Drawn (incl. overtime)", StringComparison.OrdinalIgnoreCase))
                        {
                            baseName = "Over Under Player Fouls Drawn (incl. overtime)";
                            isPlayerFoulsMerge = true;
                        }
                        else if (baseName.Equals("Player Fouls Suffered (incl. overtime)", StringComparison.OrdinalIgnoreCase))
                        {
                            baseName = "Over Under Player Fouls Suffered (incl. overtime)";
                            isPlayerFoulsMerge = true;
                        }
                        else if (baseName.Equals("Player To Be Fouled (incl. overtime)", StringComparison.OrdinalIgnoreCase))
                        {
                            baseName = "Over Under Player To Be Fouled (incl. overtime)";
                            isPlayerFoulsMerge = true;
                        }
                        else if (baseName.Equals("Player Goals or Assists (incl. overtime)", StringComparison.OrdinalIgnoreCase) ||
                                 baseName.Equals("Player Goal or Assist (incl. overtime)", StringComparison.OrdinalIgnoreCase) ||
                                 baseName.Equals("Player Goal or Assist", StringComparison.OrdinalIgnoreCase))
                        {
                            baseName = "Over Under Player Goals or Assists (incl. overtime)";
                            isPlayerGoalsMerge = true;
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
                        else if (baseName.Equals("Player Points (incl. overtime)", StringComparison.OrdinalIgnoreCase))
                        {
                            baseName = "Over Under Player Points (incl. overtime)";
                            isPlayerPointsMerge = true;
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

                                string outcomeHandicapSuffix = handicapSuffix;
                                if ((isPlayerGoalsMerge || isPlayerAssistsMerge || isPlayerTacklesMerge || isPlayerPointsMerge || isPlayerShotsMerge || isPlayerFoulsMerge) && string.IsNullOrEmpty(outcomeHandicapSuffix))
                                {
                                    if (oName == "1" || oName.Equals("Yes", StringComparison.OrdinalIgnoreCase))
                                    {
                                        oName = "Over";
                                        outcomeHandicapSuffix = " 0.5";
                                    }
                                    else if (oName.Equals("No", StringComparison.OrdinalIgnoreCase))
                                    {
                                        oName = "Under";
                                        outcomeHandicapSuffix = " 0.5";
                                    }
                                }

                                string combined = oName + outcomeHandicapSuffix;
                                combined = System.Text.RegularExpressions.Regex.Replace(combined, @"^(Over|Under)\s*\(([^)]+)\)$", "$1 $2", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                baseMarketObj.OutcomeNames[oId] = combined;
                            }
                        }
                    }
                }
            }

            if (rawCatalogMarketNames.Count > 0)
            {
                await NotifyIfNewMarketsDetectedAsync(sportId, rawCatalogMarketNames);
            }
                
            cachedMarkets = (newRawMarketIdToBaseName, newBaseMarketDict);
            _cache.Set(parsedCacheKey, cachedMarkets, TimeSpan.FromHours(24));
        }
        return cachedMarkets;
    }

    private async Task NotifyIfNewMarketsDetectedAsync(int sportId, IEnumerable<string> rawCatalogMarketNames)
    {
        try
        {
            var newMarkets = OddsPapiMarketCatalogTracker.DetectAndRecordNewMarkets(sportId, rawCatalogMarketNames);
            if (newMarkets.Count == 0) return;

            string sportLabel = sportId == 15 ? "Ice Hockey" : (sportId == 12 ? "Tennis" : (sportId == 10 ? "Soccer" : $"Sport {sportId}"));
            var mapper = _marketMapper ?? new MarketMappingService();

            var evaluatedMarkets = newMarkets
                .Select(m => (MarketName: m, IsPreWired: IsMarketPreWired(m, mapper)))
                .ToList();

            _logger.LogInformation($"OddsPapi: Detected {newMarkets.Count} new market(s) for {sportLabel}: {string.Join(", ", newMarkets)}");

            if (_emailSender != null)
            {
                string subject = $"🆕 OddsPapi Added {newMarkets.Count} New Market(s) for {sportLabel}";
                string html = EmailTemplates.BuildNewOddsPapiMarketsTemplate(sportLabel, sportId, evaluatedMarkets);
                await _emailSender.SendAdminNotificationEmailAsync(subject, html);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"OddsPapi: Failed to process new market notification for sportId={sportId}.");
        }
    }

    private static bool IsMarketPreWired(string oddsPapiMarketName, MarketMappingService mapper)
    {
        var probeMarkets = new List<BettingApp.Models.OddsPapiMarket>
        {
            new BettingApp.Models.OddsPapiMarket { MarketId = "probe", MarketName = oddsPapiMarketName }
        };

        // Check whether common canonical betslip market names or the market's own name resolves to this OddsPapi market
        var sampleProbes = new[]
        {
            oddsPapiMarketName,
            "Player Fouls Won",
            "To Score or Assist",
            "Player to Score or be Booked",
            "Player Shots on Target",
            "Player Tackles",
            "Player Assists",
            "Anytime Goalscorer"
        };

        return sampleProbes.Any(probe =>
            OddsMatchingLogic.FindBestMarket(probeMarkets, probe, "Team A vs Team B", "Player Name - Over 0.5", mapper) != null);
    }

    public async Task WarmupCacheAsync(int? specificSportId = null)
    {
        var sportsToWarm = specificSportId.HasValue ? new[] { specificSportId.Value } : new[] { 10, 15, 12 };
        
        for (int i = 0; i < sportsToWarm.Length; i++)
        {
            var sportId = sportsToWarm[i];
            if (i > 0)
            {
                await Task.Delay(1500);
            }

            string sportLabel = sportId == 15 ? "Ice Hockey" : (sportId == 12 ? "Tennis" : "Soccer");
            _logger.LogInformation($"OddsPapi: Background worker warming up cache for {sportLabel} (sportId={sportId}) Fixtures (6h) and Markets (24h)...");
            try
            {
                await GetParsedFixturesAsync(sportId);
                await GetParsedMarketsAsync(sportId);
                _logger.LogInformation($"OddsPapi: {sportLabel} cache warmup completed successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"OddsPapi: Failed during background cache warmup for {sportLabel} (sportId={sportId}).");
            }
        }
    }

    public async Task<(BettingApp.Models.OddsPapiSearchResult? Result, string? Error)> SearchOddsComparisonAsync(string teamName, int? betId = null, bool isLiveRequest = false, string? sport = null, CancellationToken cancellationToken = default, bool bypassOddsCache = false)
    {
        if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrWhiteSpace(teamName)) return (null, "API Key is missing or team name is empty.");
        
        int sportId = ResolveSportId(sport);
        if (sportId <= 0)
        {
            return (null, $"Castle doesn't yet pay for odds for {sport}.");
        }

        // Fast Path: If match fixture AND parsed odds result are already cached (and the cached fixture hasn't finished >4h ago), return immediately without acquiring any rate limiter!
        if (!bypassOddsCache && _cache.TryGetValue($"OddspapiMatch4_{sportId}_{teamName}", out (string fid, string mn, DateTime st, bool il, string? fsid, int sid) quickMatch))
        {
            bool quickStaleFinished = quickMatch.sid != 1 && quickMatch.st > DateTime.MinValue && quickMatch.st < DateTime.UtcNow.AddHours(-4);
            if (!quickStaleFinished && _cache.TryGetValue($"OddspapiParsedResult_{quickMatch.fid}", out BettingApp.Models.OddsPapiSearchResult? cachedParsed) && cachedParsed != null)
            {
                if (cachedParsed.StatusId == 0 && cachedParsed.StartTime > DateTime.MinValue && cachedParsed.StartTime <= DateTime.UtcNow && cachedParsed.StartTime >= DateTime.UtcNow.AddHours(-4))
                {
                    cachedParsed.IsLive = true;
                }
                return (cachedParsed, null);
            }
        }

        await _apiRateLimiter.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string betLabel = betId.HasValue ? $"[Bet #{betId.Value}]" : "[Manual Lookup]";
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            
            // 2. Find Fixture
            string? fixtureId = null;
            string matchName = "";
            DateTime startTime = DateTime.MinValue;
            bool isLive = false;
            string? flashscoreId = null;
            int statusId = -1;
            
            bool skipParse = _cache.TryGetValue($"OddspapiMatch4_{sportId}_{teamName}", out (string fid, string mn, DateTime st, bool il, string? fsid, int sid) cachedMatch) &&
                             !(cachedMatch.sid != 1 && cachedMatch.st > DateTime.MinValue && cachedMatch.st < DateTime.UtcNow.AddHours(-4));
            if (skipParse)
            {
                fixtureId = cachedMatch.fid;
                matchName = cachedMatch.mn;
                startTime = cachedMatch.st;
                isLive = cachedMatch.il;
                flashscoreId = cachedMatch.fsid;
                statusId = cachedMatch.sid;
                if (statusId == 0 && startTime > DateTime.MinValue && startTime <= DateTime.UtcNow && startTime >= DateTime.UtcNow.AddHours(-4))
                {
                    isLive = true;
                }
            }
            else
            {
                var parsedFixtures = await GetParsedFixturesAsync(sportId, cancellationToken);
                if (parsedFixtures == null) return (null, "Fixtures API failed or returned 429");

            string[] split = teamName.Split(new[] { " vs ", " v ", " - " }, StringSplitOptions.None);
            string homeTeam = split[0].Trim();
            string awayTeam = split.Length > 1 ? split[1].Trim() : "";

            var dateMatch = System.Text.RegularExpressions.Regex.Match(awayTeam, @"\((?:Starts:\s*)?([^)]+)\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (dateMatch.Success)
            {
                awayTeam = awayTeam.Substring(0, dateMatch.Index).Trim();
            }

            string normHomeTeam = NormalizeTeamName(homeTeam);
            string normAwayTeam = string.IsNullOrEmpty(awayTeam) ? "" : NormalizeTeamName(awayTeam);
            var normHomeTokens = ExtractSignificantTokens(normHomeTeam);
            var normAwayTokens = string.IsNullOrEmpty(normAwayTeam) ? new List<string>() : ExtractSignificantTokens(normAwayTeam);

            var bestMatches = new List<(BettingApp.Models.OddsPapiFixtureDto fixture, int score, bool isStraightOrder)>();

            foreach (var f in parsedFixtures)
            {
                // Skip Youth/Women's/SRL matches if they weren't explicitly requested
                bool qHasMod = sportId == 12
                    ? HasTennisSpecialModifier(homeTeam, awayTeam, "")
                    : (HasSpecialModifier(homeTeam) || HasSpecialModifier(awayTeam));
                if (!qHasMod && f.HasModifier)
                {
                    continue;
                }

                if (sportId == 12)
                {
                    bool queryIsDoubles = homeTeam.Contains('/') || awayTeam.Contains('/');
                    bool fixtureIsDoubles = f.Participant1Name.Contains('/') || f.Participant2Name.Contains('/');
                    if (queryIsDoubles != fixtureIsDoubles)
                    {
                        continue;
                    }
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
                        if (!IsSecondaryTeamFuzzyMatch(normOtherTeam, normHomeTeam, normHomeTokens)) continue;
                    }
                    else if (!awayMatch && homeMatch)
                    {
                        bool p1IsHome = IsNameMatch(normP1, normHomeTeam, true) || normHomeTokens.Any(t => IsNameMatch(normP1, t, true));
                        string normOtherTeam = p1IsHome ? normP2 : normP1;
                        if (!IsSecondaryTeamFuzzyMatch(normOtherTeam, normAwayTeam, normAwayTokens)) continue;
                    }
                    else
                    {
                        continue; 
                    }
                }

                if (sportId == 12 && !string.IsNullOrEmpty(normAwayTeam))
                {
                    bool homeMatchesP1 = TeamAliasMappingService.IsTeamMatch(homeTeam, f.Participant1Name) || normHomeTokens.Any(t => IsNameMatch(normP1, t, true));
                    bool homeMatchesP2 = TeamAliasMappingService.IsTeamMatch(homeTeam, f.Participant2Name) || normHomeTokens.Any(t => IsNameMatch(normP2, t, true));
                    bool awayMatchesP1 = TeamAliasMappingService.IsTeamMatch(awayTeam, f.Participant1Name) || normAwayTokens.Any(t => IsNameMatch(normP1, t, true));
                    bool awayMatchesP2 = TeamAliasMappingService.IsTeamMatch(awayTeam, f.Participant2Name) || normAwayTokens.Any(t => IsNameMatch(normP2, t, true));

                    bool alignedNormal = (homeMatchesP1 && (awayMatchesP2 || IsSecondaryTeamFuzzyMatch(normP2, normAwayTeam, normAwayTokens))) ||
                                         (awayMatchesP2 && IsSecondaryTeamFuzzyMatch(normP1, normHomeTeam, normHomeTokens));
                    bool alignedSwapped = (homeMatchesP2 && (awayMatchesP1 || IsSecondaryTeamFuzzyMatch(normP1, normAwayTeam, normAwayTokens))) ||
                                          (awayMatchesP1 && IsSecondaryTeamFuzzyMatch(normP2, normHomeTeam, normHomeTokens));

                    if (!alignedNormal && !alignedSwapped) continue;
                }

                int score = 0;
                
                if (IsNameMatch(normP1, normHomeTeam, true) || IsNameMatch(normP2, normHomeTeam, true) ||
                    (sportId == 12 && (TeamAliasMappingService.IsTeamMatch(homeTeam, f.Participant1Name) || TeamAliasMappingService.IsTeamMatch(homeTeam, f.Participant2Name))))
                {
                    score += 50;
                }
                if (!string.IsNullOrEmpty(normAwayTeam) && (IsNameMatch(normP1, normAwayTeam, true) || IsNameMatch(normP2, normAwayTeam, true) ||
                    (sportId == 12 && (TeamAliasMappingService.IsTeamMatch(awayTeam, f.Participant1Name) || TeamAliasMappingService.IsTeamMatch(awayTeam, f.Participant2Name)))))
                {
                    score += 50;
                }
                
                // Huge bonus for exact match to differentiate "Team" from "Team 2"
                if (IsExactMatch(normP1, normHomeTeam, true) || IsExactMatch(normP2, normHomeTeam, true) ||
                    (sportId == 12 && (TeamAliasMappingService.IsTeamMatch(homeTeam, f.Participant1Name) || TeamAliasMappingService.IsTeamMatch(homeTeam, f.Participant2Name))))
                {
                    score += 100;
                }
                if (!string.IsNullOrEmpty(normAwayTeam) && (IsExactMatch(normP1, normAwayTeam, true) || IsExactMatch(normP2, normAwayTeam, true) ||
                    (sportId == 12 && (TeamAliasMappingService.IsTeamMatch(awayTeam, f.Participant1Name) || TeamAliasMappingService.IsTeamMatch(awayTeam, f.Participant2Name)))))
                {
                    score += 100;
                }

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
                    // Prioritize live and upcoming matches with odds over finished matches.
                    // Also treat StatusId == 0 fixtures whose kickoff was >4h ago in the 6h fixture cache as finished.
                    bool isStaleFinished = f.StatusId == 0 && f.StartTime.HasValue && f.StartTime.Value < DateTime.UtcNow.AddHours(-4);
                    if (f.StatusId == 1) score += 300; // Live match gets huge priority
                    else if (f.StatusId == 0 && !isStaleFinished) score += 200; // Upcoming (or recently kicked off within 4h) match gets priority
                    else if (f.StatusId == 2 || isStaleFinished) score -= 300; // Finished match gets deprioritized
                    if (f.HasOdds) score += 100;

                    bool p1MatchesHome = IsNameMatch(normP1, normHomeTeam, true) || normHomeTokens.Any(t => IsNameMatch(normP1, t, true)) ||
                                         (sportId == 12 && TeamAliasMappingService.IsTeamMatch(homeTeam, f.Participant1Name));
                    bool p2MatchesAway = !string.IsNullOrEmpty(normAwayTeam) &&
                                         (IsNameMatch(normP2, normAwayTeam, true) || normAwayTokens.Any(t => IsNameMatch(normP2, t, true)) ||
                                          (sportId == 12 && TeamAliasMappingService.IsTeamMatch(awayTeam, f.Participant2Name)));
                    bool isStraightOrder = !string.IsNullOrEmpty(normAwayTeam) && p1MatchesHome && p2MatchesAway;

                    bestMatches.Add((f, score, isStraightOrder));
                }
            }

            if (!bestMatches.Any()) 
            {
                _logger.LogInformation($" {betLabel} OddsPapi: Could not find match against '{teamName}'");
                return (null, $"No matching fixtures found for '{teamName}' in the next 7 days.");
            }
            
            // Break score ties by closest StartTime to now (soonest upcoming / most recently played), then Home/Away straight alignment
            var nowUtc = DateTime.UtcNow;
            var bestFixture = bestMatches
                .OrderByDescending(m => m.score)
                .ThenBy(m => m.fixture.StartTime.HasValue ? Math.Abs((m.fixture.StartTime.Value - nowUtc).TotalSeconds) : double.MaxValue)
                .ThenByDescending(m => m.isStraightOrder)
                .First().fixture;
            
            fixtureId = bestFixture.FixtureId;
            string finalP1 = bestFixture.Participant1Name;
            string finalP2 = bestFixture.Participant2Name;
            matchName = $"{finalP1} vs {finalP2}";

            _logger.LogInformation($" {betLabel} OddsPapi: Found Match ID {fixtureId} for {matchName}");
            
            if (bestFixture.StartTime.HasValue)
            {
                startTime = bestFixture.StartTime.Value;
            }
            
            statusId = bestFixture.StatusId;
            if (statusId == 1)
            {
                isLive = true;
            }
            else if (statusId == 0 && startTime <= DateTime.UtcNow && startTime >= DateTime.UtcNow.AddHours(-4))
            {
                // OddsPapi may be slow to update statusId to 1; if it's past start time, within 4 hours, and still 0 (pre-game), treat as live
                isLive = true;
            }
            else
            {
                isLive = false;
            }

            flashscoreId = bestFixture.FlashscoreId;
            
            _cache.Set($"OddspapiMatch4_{sportId}_{teamName}", (fixtureId, matchName, startTime, isLive, flashscoreId, statusId), TimeSpan.FromHours(12));
            }
            if (bypassOddsCache)
            {
                _cache.Remove($"OddspapiParsedResult_{fixtureId}");
                _cache.Remove($"OddspapiOdds_{fixtureId}");
            }
            else if (_cache.TryGetValue($"OddspapiParsedResult_{fixtureId}", out BettingApp.Models.OddsPapiSearchResult? cachedResult) && cachedResult != null)
            {
                if (cachedResult.StatusId == 0 && cachedResult.StartTime > DateTime.MinValue && cachedResult.StartTime <= DateTime.UtcNow && cachedResult.StartTime >= DateTime.UtcNow.AddHours(-4))
                {
                    cachedResult.IsLive = true;
                }
                return (cachedResult, null);
            }
            
            // 1. Get markets metadata to map IDs to Names
            var parsedMarkets = await GetParsedMarketsAsync(sportId, cancellationToken);
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
            TimeSpan cacheDuration = isLiveRequest ? TimeSpan.FromSeconds(15) : TimeSpan.FromSeconds(75);
            string? oJson = await HttpCacheHelper.GetOrCreateAsync(_cache, cacheKeyOdds, cacheDuration, () => _httpClient.GetAsync(oddsUrl, cancellationToken), _logger, cancellationToken);
            
            if (oJson == null) return (null, "Odds API failed or returned 429");
            using var oddsDoc = JsonDocument.Parse(oJson ?? "{}");

            var result = new BettingApp.Models.OddsPapiSearchResult
            {
                FixtureId = fixtureId,
                MatchName = matchName,
                StartTime = startTime,
                IsLive = isLive,
                StatusId = statusId,
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
                                                var oddsData = new BettingApp.Models.OddsData
                                                {
                                                    Price = price.GetDouble(),
                                                    MarketId = mId,
                                                    OutcomeId = oId,
                                                    PlayerId = playerProp.Name
                                                };

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
            
            _cache.Set($"OddspapiParsedResult_{fixtureId}", result, cacheDuration);

            return (result, null);
        }
        catch (OperationCanceledException)
        {
            return (null, "Odds lookup canceled.");
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

    private static bool HasTennisSpecialModifier(string p1, string p2, string tournament)
    {
        // In Tennis, participants are individual players and WTA/ITF tournaments have "Women" in the tournament name,
        // so "Women" must NOT be treated as a filter modifier. Only filter out Simulated Reality League (SRL) / Virtual matches.
        return HasSrlModifier(p1) || HasSrlModifier(p2) || HasSrlModifier(tournament);
    }

    private static bool HasSrlModifier(string input)
    {
        if (string.IsNullOrEmpty(input)) return false;
        return System.Text.RegularExpressions.Regex.IsMatch(input, @"\b(?:srl|virtual|esports|cyber)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
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

    private static List<string> ExtractSignificantTokens(string normTeam)
    {
        if (string.IsNullOrWhiteSpace(normTeam)) return new List<string>();
        var tokens = normTeam.Split(new[] { ' ', '-', '.', ',', '/' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3 &&
                        !w.Equals("the", StringComparison.OrdinalIgnoreCase) &&
                        !TeamAliasMappingService.IsStopWord(w))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!tokens.Any())
        {
            tokens = normTeam.Split(new[] { ' ', '-', '.', ',', '/' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length >= 2 && !TeamAliasMappingService.IsStopWord(w))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        if (!tokens.Any()) tokens.Add(normTeam);
        return tokens;
    }

    private static string StripArabicArticle(string normTeam)
    {
        if (string.IsNullOrWhiteSpace(normTeam)) return "";
        return System.Text.RegularExpressions.Regex.Replace(normTeam, @"\b(al|el)\b", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Replace("  ", " ").Trim();
    }

    private bool IsSecondaryTeamFuzzyMatch(string normOtherTeam, string normQueryTeam, List<string> queryTokens)
    {
        if (string.IsNullOrWhiteSpace(normOtherTeam) || string.IsNullOrWhiteSpace(normQueryTeam)) return false;

        if (ComputeLevenshteinDistance(normOtherTeam, normQueryTeam) <= 3) return true;

        string strippedOther = StripArabicArticle(normOtherTeam);
        string strippedQuery = StripArabicArticle(normQueryTeam);
        if (!string.IsNullOrEmpty(strippedOther) && !string.IsNullOrEmpty(strippedQuery) &&
            ComputeLevenshteinDistance(strippedOther, strippedQuery) <= 3)
        {
            return true;
        }

        bool isArabicContext = TeamAliasMappingService.HasArabicArticle(normOtherTeam) || TeamAliasMappingService.HasArabicArticle(normQueryTeam);
        var otherTokens = ExtractSignificantTokens(normOtherTeam);
        foreach (var ot in otherTokens.Where(t => t.Length >= 4))
        {
            foreach (var qt in queryTokens.Where(t => t.Length >= 4))
            {
                int maxDist = Math.Min(ot.Length, qt.Length) >= 6 ? 2 : 1;
                if (ComputeLevenshteinDistance(ot, qt) <= maxDist) return true;

                if (isArabicContext)
                {
                    string skelO = TeamAliasMappingService.GetArabicTransliterationSkeleton(ot);
                    string skelQ = TeamAliasMappingService.GetArabicTransliterationSkeleton(qt);
                    if (skelO.Length >= 3 && skelO.Equals(skelQ, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
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
