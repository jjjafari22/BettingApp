using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;

namespace BettingApp.Services
{
    public class AiVisionExtractionResult
    {
        [JsonPropertyName("bookmaker")]
        public string Bookmaker { get; set; } = "";
        
        [JsonPropertyName("isBetBuilder")]
        public bool IsBetBuilder { get; set; }
        
        [JsonPropertyName("isLive")]
        public bool IsLive { get; set; }
        
        [JsonPropertyName("totalOdds")]
        public string TotalOdds { get; set; } = "";

        [JsonPropertyName("stake")]
        public string Stake { get; set; } = "";
        
        [JsonPropertyName("legs")]
        public List<AiVisionLeg> Legs { get; set; } = new();
    }

    public class AiVisionLeg
    {
        [JsonPropertyName("match")]
        public string Match { get; set; } = "";
        
        [JsonPropertyName("sport")]
        public string Sport { get; set; } = "";
        
        [JsonPropertyName("market")]
        public string Market { get; set; } = "";
        
        [JsonPropertyName("selection")]
        public string Selection { get; set; } = "";
        
        [JsonPropertyName("badges")]
        public List<string> Badges { get; set; } = new();


        [JsonPropertyName("odds")]
        public string Odds { get; set; } = "";
        
        [JsonPropertyName("startTime")]
        public DateTime? StartTime { get; set; }
    }

    public class AiOutcomeLegResult
    {
        [JsonPropertyName("match")]
        public string Match { get; set; } = "";
        
        [JsonPropertyName("outcome")]
        public string Outcome { get; set; } = "";
        
        [JsonPropertyName("stats")]
        public string Stats { get; set; } = "";
        
        [JsonPropertyName("matchStartTimeIso")]
        public string? MatchStartTimeIso { get; set; }

        [JsonPropertyName("verificationSource")]
        public string VerificationSource { get; set; } = "Unknown";
    }

    public class AiOutcomeResultData
    {
        [JsonPropertyName("overallStatus")]
        public string OverallStatus { get; set; } = "";
                
        [JsonPropertyName("matchStartTimeIso")]
        public string? MatchStartTimeIso { get; set; }
        
        [JsonPropertyName("fullAnalysis")]
        public string FullAnalysis { get; set; } = "";
        
        [JsonPropertyName("legs")]
        public List<AiOutcomeLegResult> Legs { get; set; } = new();
    }

    public class AiVisionService
    {
        public static System.Collections.Concurrent.ConcurrentDictionary<int, bool> ProcessingAutoReadBetIds { get; } = new();
        public static System.Collections.Concurrent.ConcurrentDictionary<int, bool> ProcessingOutcomeBetIds { get; } = new();
        
        private readonly HttpClient _httpClient;
        private readonly string? _apiKey;
        private readonly FotMobScraperService _fotMob;
        private readonly EspnApiService _espn;
        private readonly PlayerMatchResolverService _matchResolver;
        private readonly IConfiguration _config;
        private static Google.Apis.Auth.OAuth2.GoogleCredential? _cachedCredential;
        private static readonly object _credentialLock = new object();

        // Circuit breaker state: [Gemini-Dev-API] is Primary; if it hits 3 consecutive 429/timeouts, fail over primary to [Vertex-AI] for 60 minutes
        private static int _consecutiveDevApiFailures = 0;
        private static long _useVertexFallbackUntilTicks = 0;
        private const int CircuitBreakerThreshold = 3;
        private static readonly TimeSpan CircuitBreakerDuration = TimeSpan.FromMinutes(60);

        private bool IsUsingDevApiPrimary =>
            !string.IsNullOrWhiteSpace(_apiKey) &&
            DateTime.UtcNow.Ticks >= System.Threading.Interlocked.Read(ref _useVertexFallbackUntilTicks);

        private async Task<string> GetVertexAccessTokenAsync()
        {
            if (_cachedCredential == null)
            {
                lock (_credentialLock)
                {
                    if (_cachedCredential == null)
                    {
                        // 1. Try to get the JSON content directly from Azure Environment Variables
                        string? jsonContent = _config["GOOGLE_CREDENTIALS_JSON"] ?? Environment.GetEnvironmentVariable("GOOGLE_CREDENTIALS_JSON");

                        // 2. Fallback to local Mac path for development
                        if (string.IsNullOrWhiteSpace(jsonContent))
                        {
                            var jsonPath = "/Users/jonasjafari/Projects/BettingApp/castle-gemini-6de5fef6d94f.json";
                            if (System.IO.File.Exists(jsonPath))
                            {
                                jsonContent = System.IO.File.ReadAllText(jsonPath);
                            }
                            else
                            {
                                throw new Exception("Google Vertex AI credentials not found in env vars or local path!");
                            }
                        }

                        // Azure sometimes escapes JSON when pasted into App Settings
                        jsonContent = jsonContent.Trim();
                        if (jsonContent.StartsWith("\"") && jsonContent.EndsWith("\""))
                        {
                            jsonContent = jsonContent.Substring(1, jsonContent.Length - 2);
                            jsonContent = jsonContent.Replace("\\\"", "\"").Replace("\\n", "\n");
                        }

#pragma warning disable CS0618
                        _cachedCredential = GoogleCredential.FromJson(jsonContent).CreateScoped("https://www.googleapis.com/auth/cloud-platform");
#pragma warning restore CS0618
                    }
                }
            }

            return await ((Google.Apis.Auth.OAuth2.ITokenAccess)_cachedCredential).GetAccessTokenForRequestAsync();
        }
        private readonly ILogger<AiVisionService> _logger;

        public AiVisionService(HttpClient httpClient, IConfiguration config, FotMobScraperService fotMob, EspnApiService espn, PlayerMatchResolverService matchResolver, ILogger<AiVisionService> logger)
        {
            _httpClient = httpClient;
            _config = config;
            _apiKey = config["GeminiApiKey"] ?? Environment.GetEnvironmentVariable("GeminiApiKey") ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            _fotMob = fotMob;
            _espn = espn;
            _matchResolver = matchResolver;
            _logger = logger;
        }


        public async Task<(AiVisionExtractionResult? Result, string? Error)> ExtractBetSlipDataAsync(string imageUrl, int? betId = null, System.Threading.CancellationToken callerToken = default)
        {
            var token = await GetVertexAccessTokenAsync();
            if (string.IsNullOrEmpty(token))
            {
                return (null, "Google Vertex Auth Token is missing or invalid. Check credentials.");
            }

            string betLabel = betId.HasValue ? $"[Bet #{betId.Value}] [Auto-Read] " : "[Auto-Read] ";

            // Enforce a strict single 110s timeout across image download, Gemini API (including fast 429 retries), and FotMob match resolution
            using var autoReadTimeoutCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(callerToken);
            autoReadTimeoutCts.CancelAfter(TimeSpan.FromSeconds(110));
            var timeoutToken = autoReadTimeoutCts.Token;

            try
            {
                // 1. Download the image bytes from the URL
                var imageBytes = await _httpClient.GetByteArrayAsync(imageUrl, timeoutToken);
                var base64Image = Convert.ToBase64String(imageBytes);

                // Determine mime type (rough guess based on extension, though Gemini usually figures it out)
                string mimeType = imageUrl.ToLower().EndsWith(".png") ? "image/png" : "image/jpeg";

                // 2. Build the Gemini JSON Payload
                var systemInstruction = $@"You are a high-precision sports betting OCR and data extraction bot (Current Year: {DateTime.UtcNow.Year}).
Analyze this betting slip screenshot carefully. Your highest directive is absolute literal accuracy: READ EXACTLY WHAT IS PRINTED AND NEVER ASSUME.

# CORE OCR DIRECTIVES
1. NEVER ASSUME OR INFER: Do NOT assume that multiple legs in a combo or Bet Builder share the same market! Read each leg independently from scratch.
2. LITERAL OCR BEFORE TRANSLATION: Read the exact words printed under each player or selection before translating. Never invent or add modifiers (such as 'on target') that are not explicitly written on the slip. Never replace a printed player name with a different player from your training-cutoff roster memory ({DateTime.UtcNow.Year} rosters include new rookies, draftees, and transfers).

# MARKET TRANSLATION & RULES
- Player Shots vs Shots on Target:
  * Swedish 'Skott från spelaren' / Danish 'Spillerskud' / Norwegian 'Spillerskudd' -> 'Player Shots' (Total Shots, including off-target / blocked).
  * Swedish 'Skott på mål från spelaren' / Danish 'Skud på mål' / Norwegian 'Skudd på mål' -> 'Player Shots on Target'.
  * STRICT RULE: If 'på mål' (or 'on target') is NOT written on the slip, you MUST output 'Player Shots', NEVER 'Player Shots on Target'!
- Other Translations: If the market is in another language (e.g. Danish 'Kort i alt'), translate it to standard English (e.g. 'Total Cards').
- Promotional Prefixes: If the market includes a promotional prefix (e.g. 'Early Win', 'Early Payout', '2Up'), preserve it in the market name (e.g. 'Early Win - Match Result (1X2)').
- Lines & Handicaps: If the market includes a specific line or handicap (e.g. '(0-1)', '-1.5', '+2.5'), you MUST include that modifier in the market name. Never drop decimals!
- Team Names in Market: If a team name is in the market (e.g. 'FC Midtjylland Total Goals'), include it exactly as written.

# FIELDS TO EXTRACT
1. bookmaker: 'Unibet', 'Bet365', 'Coolbet', 'EpicBet', etc. If the logo is missing, guess based on UI colors:
   - Coolbet: Dark theme, cyan/light-blue rounded odds boxes, bright green 'PLACE BET' button, green-bordered stake input.
   - Unibet: Dark gray theme, bright yellow 'Place bet' (or 'Placer væddemål') button, green numeric keyboard keys.
   - Bet365: Green header, grey/yellow UI elements.
   Output 'Unknown' if impossible to tell. Do NOT leave empty.
2. isLive: boolean. true ONLY if you clearly see an explicit 'LIVE' or 'In-Play' badge, or ongoing current match score ('1-0', '0-0'). Default to false.
3. totalOdds: Final combined odds of the slip as a clean decimal number (e.g. '27.84'). Never include CSS units like 'px'.
4. stake: Amount bet (e.g. '100', '1000'). If the user manually drew or wrote digits over the image with a digital pen, prioritize handwritten digits over printed text!
5. legs: An array of objects for each individual bet:
   - legThoughtProcess: Step 1: Transcribe the exact raw text seen on the slip for this leg. Step 2: Translate strictly according to rules without assuming.
   - match: Team names translated into standard, globally recognized English names (e.g. 'FC Copenhagen' instead of 'FC København', 'Bayern Munich' instead of 'Bayern München'). STRICT DIRECTIVE: If the match or team header is NOT visible or cropped on the screenshot, output null. NEVER guess, assume, or hallucinate the match from player names or past memory!
   - sport: 'Soccer', 'Ice Hockey', 'American Football', 'Basketball', 'Baseball', 'Tennis', etc. Deduce from team names, market types, or tournament. STRICT RULE: For NFL or gridiron football, ALWAYS output 'American Football' (NEVER 'Football', to prevent confusion with soccer). For association football, ALWAYS output 'Soccer'.
   - market: The translated English market name.
   - selection: The bet chosen including player condition (e.g. 'Marcus Rashford - Will Score', 'Erling Haaland - Over 0.5'). Transcribe player names letter-by-letter without autocorrecting or substituting other players. CRITICAL FOR SUBSTITUTION PROMOTIONS: If the leg has a substitution promotion (such as 'Power Sub', 'Super Sub', 'Epic Sub', 'Sub on Play on', or substitution swap arrows icon), you MUST append ' (Power Sub)' to the selection text (e.g. 'Niklas Castro - 2+ Shots on Goal (Anytime) (Power Sub)')!
   - badges: Array of promo labels or visual icons (e.g. 'Power Sub', 'Sub on Play on', 'Super Sub', 'Epic Sub', 'Early Payout', 'Super Boost', 'Live'). IMPORTANT: Substitution icons (such as two swap arrows pointing in opposite directions) next to a player indicate 'Power Sub'.
   - odds: The odds value (e.g. '1.95'). If multiple legs are part of a Bet Builder with shared odds, output the shared odds for the FIRST leg and null for subsequent legs.";

                var fullPrompt = systemInstruction + "\n\nPlease extract the bet details from this screenshot according to the strict system instructions.";
                
                var payload = new
                {
                    contents = new[]
                    {
                        new
                        {
                            role = "user",
                            parts = new object[]
                            {
                                new { text = fullPrompt },
                                new
                                {
                                    inlineData = new
                                    {
                                        mimeType = mimeType,
                                        data = base64Image
                                    }
                                }
                            }
                        }
                    },
                    generationConfig = new
                    {
                        maxOutputTokens = 16384,
                        responseMimeType = "application/json",
                        responseSchema = new
                        {
                            type = "OBJECT",
                            properties = new
                            {
                                thoughtProcess = new { type = "STRING", description = "Your detailed, step-by-step reasoning for extracting the slip. Analyze the image carefully here before filling the rest of the fields." },
                                bookmaker = new { type = "STRING", nullable = true, description = "The name of the bookmaker/sportsbook. If logo is missing, guess from UI colors (Coolbet=cyan odds/green button, Unibet=yellow button/green keyboard). Output 'Unknown' if impossible to tell." },
                                isLive = new { type = "BOOLEAN", description = "True ONLY if explicitly marked as a LIVE or In-Play bet." },
                                totalOdds = new { type = "STRING", nullable = true },
                                stake = new { type = "STRING", nullable = true },
                                legs = new
                                {
                                    type = "ARRAY",
                                    items = new
                                    {
                                        type = "OBJECT",
                                        properties = new
                                        {
                                            legThoughtProcess = new { type = "STRING", description = "Step 1: Transcribe the exact raw text seen on the slip for this leg. Step 2: Translate strictly without assuming." },
                                            match = new { type = "STRING", nullable = true, description = "The match name (e.g. 'Team A vs Team B'). Output null if the match title/header is not visibly printed on the screenshot. Never guess or hallucinate." },
                                            sport = new { type = "STRING", nullable = true, description = "The sport of the match, e.g., 'Soccer', 'Ice Hockey', 'American Football', 'Basketball', 'Baseball', 'Tennis'." },
                                            market = new { type = "STRING", nullable = true },
                                            selection = new { type = "STRING", nullable = true, description = "The selection including condition. If covered by a substitution promotion (Power Sub, Epic Sub, Super Sub, swap arrows icon), append ' (Power Sub)'." },
                                            badges = new { type = "ARRAY", items = new { type = "STRING" } },
                                            odds = new { type = "STRING", nullable = true }
                                        },
                                        required = new[] { "legThoughtProcess", "match", "sport", "market", "selection", "badges", "odds" }
                                    }
                                }
                            },
                            required = new[] { "thoughtProcess", "bookmaker", "isLive", "totalOdds", "stake", "legs" }
                        },
                        thinkingConfig = new { thinkingLevel = "medium" }
                    }
                };

                var jsonPayload = JsonSerializer.Serialize(payload);
                
                // Hardcoded to the latest available Vertex AI enterprise model
                var resolvedModel = "gemini-3.8-flash";

                var apiUrl = $"https://aiplatform.googleapis.com/v1/projects/castle-gemini/locations/global/publishers/google/models/{resolvedModel}:generateContent";
                
                string initialProviderTag = IsUsingDevApiPrimary ? "[Gemini-Dev-API] " : "[Vertex-AI] ";
                _logger.LogInformation($"{betLabel}{initialProviderTag}Calling Gemini (Model: {resolvedModel})...");

                var (response, elapsedMs, usedProviderTag) = await SendWithRetryAsync(apiUrl, resolvedModel, jsonPayload, betLabel, token, timeoutSeconds: 90, callerToken: timeoutToken);
                string responseString = await response.Content.ReadAsStringAsync(timeoutToken);
                
                if (response.IsSuccessStatusCode)
                {
                    LogAiUsage(responseString, $"{betLabel}{usedProviderTag}", elapsedMs, response.StatusCode.ToString());
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError($"{betLabel}{usedProviderTag}AI ERROR: {response.StatusCode} - {responseString}");
                    return (null, $"Gemini API Error ({usedProviderTag.Trim()}): {response?.StatusCode}\nResolved Model: {resolvedModel}\nDetails: {responseString}");
                }

                // 4. Parse the response
                using var doc = JsonDocument.Parse(responseString);
                string textResponse = ExtractGeminiText(doc)?.Trim() ?? "";

                if (!string.IsNullOrEmpty(textResponse))
                {
                    // Sometimes the LLM returns ```json ... ``` despite instructions. Strip it.
                    if (textResponse.StartsWith("```json")) textResponse = textResponse.Substring(7);
                    if (textResponse.StartsWith("```")) textResponse = textResponse.Substring(3);
                    if (textResponse.EndsWith("```")) textResponse = textResponse.Substring(0, textResponse.Length - 3);

                    textResponse = textResponse.Trim();
                    
                    if (!string.IsNullOrEmpty(textResponse))
                    {
                        int startIndex = textResponse.IndexOf('{');
                        int endIndex = textResponse.LastIndexOf('}');
                        if (startIndex >= 0 && endIndex >= startIndex)
                        {
                            textResponse = textResponse.Substring(startIndex, endIndex - startIndex + 1);
                        }
                    }

                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var result = JsonSerializer.Deserialize<AiVisionExtractionResult>(textResponse, options);
                    
                    if (result != null)
                    {
                        if (!string.IsNullOrEmpty(result.TotalOdds))
                        {
                            var match = System.Text.RegularExpressions.Regex.Match(result.TotalOdds, @"\d+([.,]\d+)?");
                            if (match.Success) result.TotalOdds = match.Value;
                        }
                        if (!string.IsNullOrEmpty(result.Stake))
                        {
                            var match = System.Text.RegularExpressions.Regex.Match(result.Stake, @"\d+([.,]\d+)?");
                            if (match.Success) result.Stake = match.Value;
                        }
                    }

                    if (result != null && result.Legs != null)
                    {
                        // Group contiguous legs that belong to the same Bet Builder (shared odds: subsequent legs have empty Odds).
                        // A leg with its own explicit Odds (like a second leg on a combo slip) starts a new group so it resolves its own match!
                        var legGroups = new List<List<AiVisionLeg>>();
                        List<AiVisionLeg>? currentLegGroup = null;

                        for (int i = 0; i < result.Legs.Count; i++)
                        {
                            var leg = result.Legs[i];
                            if (leg == null) continue;

                            bool startNewGroup = currentLegGroup == null
                                || !string.IsNullOrWhiteSpace(leg.Odds)
                                || (!string.IsNullOrWhiteSpace(leg.Match)
                                    && currentLegGroup.Any(l => !string.IsNullOrWhiteSpace(l.Match) && !string.Equals(l.Match.Trim(), leg.Match.Trim(), StringComparison.OrdinalIgnoreCase)));

                            if (startNewGroup)
                            {
                                currentLegGroup = new List<AiVisionLeg>();
                                legGroups.Add(currentLegGroup);
                            }
                            currentLegGroup!.Add(leg);
                        }

                        // Resolve missing matches per group in parallel (at most 1 FotMob lookup per group; Bet Builder sub-legs reuse in 0ms!)
                        var groupResolutionTasks = legGroups.Select(async group =>
                        {
                            string? groupMatch = group.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l.Match))?.Match;

                            if (string.IsNullOrWhiteSpace(groupMatch))
                            {
                                foreach (var leg in group.Where(l => !string.IsNullOrWhiteSpace(l.Selection)))
                                {
                                    if (timeoutToken.IsCancellationRequested)
                                    {
                                        _logger.LogWarning($"{betLabel}Skipping remaining player match resolution due to Auto-Read timeout.");
                                        break;
                                    }

                                    string? resolved = await _matchResolver.ResolvePlayerMatchAsync(leg.Selection, DateTime.UtcNow, betId, timeoutToken);
                                    if (!string.IsNullOrEmpty(resolved))
                                    {
                                        groupMatch = resolved;
                                        leg.Match = resolved;
                                        _logger.LogInformation($"{betLabel}Auto-Resolved missing match to: '{resolved}' for player '{leg.Selection}'");
                                        break;
                                    }
                                }
                            }

                            if (!string.IsNullOrWhiteSpace(groupMatch))
                            {
                                foreach (var leg in group.Where(l => string.IsNullOrWhiteSpace(l.Match)))
                                {
                                    leg.Match = groupMatch;
                                    _logger.LogInformation($"{betLabel}Reused resolved match '{groupMatch}' for leg '{leg.Selection}'");
                                }
                            }

                            return groupMatch;
                        }).ToList();

                        var resolvedGroupMatches = await Task.WhenAll(groupResolutionTasks);
                        string? fallbackSharedMatch = resolvedGroupMatches.FirstOrDefault(m => !string.IsNullOrWhiteSpace(m));

                        // Final fallback: If any leg still has no match (e.g. a non-player market like 'Over 2.5 Goals' with its own odds), inherit fallbackSharedMatch
                        if (!string.IsNullOrEmpty(fallbackSharedMatch))
                        {
                            foreach (var leg in result.Legs.Where(l => l != null && string.IsNullOrWhiteSpace(l.Match)))
                            {
                                leg.Match = fallbackSharedMatch;
                                _logger.LogInformation($"{betLabel}Reused resolved match '{fallbackSharedMatch}' for leg '{leg.Selection}'");
                            }
                        }

                        var groups = result.Legs.Where(l => !string.IsNullOrEmpty(l?.Match)).GroupBy(l => l.Match.Trim().ToLowerInvariant());
                        // It is a Bet Builder if there are multiple legs for the same match AND they share odds (indicated by the AI leaving odds null for the subsequent legs)
                        result.IsBetBuilder = groups.Any(g => g.Count() > 1 && g.Any(l => string.IsNullOrWhiteSpace(l.Odds)));
                    }
                    
                    if (result?.Legs != null)
                    {
                        foreach (var leg in result.Legs)
                        {
                            if (leg == null) continue;
                            bool isPowerSub = false;
                            
                            if (leg.Badges != null && leg.Badges.Any(b => b != null && (
                                b.Contains("Power Sub", StringComparison.OrdinalIgnoreCase) || 
                                b.Contains("Substitute", StringComparison.OrdinalIgnoreCase) ||
                                b.Contains("Super Sub", StringComparison.OrdinalIgnoreCase) ||
                                b.Contains("Epic Sub", StringComparison.OrdinalIgnoreCase) ||
                                b.Contains("Sub on Play", StringComparison.OrdinalIgnoreCase) ||
                                b.Contains("Sub On", StringComparison.OrdinalIgnoreCase))))
                            {
                                isPowerSub = true;
                            }
                            
                            if (!isPowerSub)
                            {
                                string combinedText = $"{(leg.Market ?? "")} {(leg.Selection ?? "")}";
                                if (combinedText.Contains("Power Sub", StringComparison.OrdinalIgnoreCase) || 
                                    combinedText.Contains("Sub on Play", StringComparison.OrdinalIgnoreCase) ||
                                    combinedText.Contains("Super Sub", StringComparison.OrdinalIgnoreCase) ||
                                    combinedText.Contains("Epic Sub", StringComparison.OrdinalIgnoreCase))
                                {
                                    isPowerSub = true;
                                }
                            }

                            if (isPowerSub) 
                            {
                                leg.Selection = leg.Selection ?? "";
                                if (!leg.Selection.Contains("(Power Sub)"))
                                {
                                    leg.Selection += " (Power Sub)";
                                }
                            }
                        }

                        NormalizeBookmakerMarkets(result);
                    }

                    return (result, null);
                }

                return (null, "No candidates returned from Gemini.");
            }
            catch (Exception ex) when (ex is OperationCanceledException || ex is TimeoutException)
            {
                _logger.LogWarning($"{betLabel}AI Auto-Read timed out: {ex.Message}");
                return (null, "AI Auto-Read timed out (> 110s). Please use 'AI Auto-Read' to retry or review manually.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"{betLabel}EXCEPTION in ExtractBetSlipDataAsync: {ex.ToString()}");
                return (null, $"Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Applies deterministic Unibet market normalization to AI-extracted legs.
        /// Unibet only offers "Player Fouls Committed" and never offers "Player Fouls Won/Suffered/Drawn".
        /// Because Unibet Sweden ("Fouls mot spelaren") is literally translated by AI as "Player Fouls Won",
        /// "Player Fouls Suffered", "Player Fouls Drawn", or "Player To Be Fouled", canonicalizing any Unibet
        /// player foul leg to "Player Fouls Committed" ensures Admin UI, OddsPapi matching, and FotMob
        /// auto-settlement all evaluate the correct stat without touching any other bookmaker.
        /// </summary>
        public static void NormalizeBookmakerMarkets(AiVisionExtractionResult? result)
        {
            if (result?.Legs == null) return;

            bool isUnibet = !string.IsNullOrWhiteSpace(result.Bookmaker) &&
                            result.Bookmaker.Contains("Unibet", StringComparison.OrdinalIgnoreCase);
            if (!isUnibet) return;

            foreach (var leg in result.Legs)
            {
                if (leg == null) continue;

                string market = leg.Market?.Trim() ?? "";
                if (string.IsNullOrEmpty(market)) continue;

                if (!IsPlayerFoulLeg(leg, out bool selectionHasPlayerName)) continue;

                if (selectionHasPlayerName || !HasSpecificPersonName(market, leg.Match))
                {
                    leg.Market = "Player Fouls Committed";
                }
                else
                {
                    // Preserve embedded player name if it was only placed in Market (e.g. "Ethan Ampadu Total Fouls Won" -> "Ethan Ampadu Total Fouls Committed")
                    leg.Market = System.Text.RegularExpressions.Regex.Replace(
                        market,
                        @"\b(Fouls?\s+(Won|Suffered|Drawn|Conceded)|To\s+Be\s+Fouled)\b",
                        "Fouls Committed",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                }

                if (!string.IsNullOrEmpty(leg.Selection))
                {
                    leg.Selection = System.Text.RegularExpressions.Regex.Replace(
                        leg.Selection,
                        @"\b(Fouls?\s+(Won|Suffered|Drawn|Conceded)|To\s+Be\s+Fouled)\b",
                        "Fouls Committed",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                }
            }
        }

        private static bool IsPlayerFoulLeg(AiVisionLeg leg, out bool selectionHasPlayerName)
        {
            selectionHasPlayerName = HasSpecificPersonName(leg.Selection, leg.Match);

            string market = leg.Market?.Trim() ?? "";
            if (!market.Contains("foul", StringComparison.OrdinalIgnoreCase))
                return false;

            if (market.Contains("interval", StringComparison.OrdinalIgnoreCase))
                return false;

            if (market.Contains("player", StringComparison.OrdinalIgnoreCase))
                return true;

            if (selectionHasPlayerName)
                return true;

            if (HasSpecificPersonName(market, leg.Match) &&
                (market.Contains("won", StringComparison.OrdinalIgnoreCase) ||
                 market.Contains("suffered", StringComparison.OrdinalIgnoreCase) ||
                 market.Contains("drawn", StringComparison.OrdinalIgnoreCase) ||
                 market.Contains("fouled", StringComparison.OrdinalIgnoreCase) ||
                 market.Contains("conceded", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return false;
        }

        private static bool HasSpecificPersonName(string? text, string? match)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            string cleaned = text;
            if (!string.IsNullOrWhiteSpace(match))
            {
                var teams = match.Split(new[] { " vs ", " v ", " - ", " @ " }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var team in teams)
                {
                    string t = team.Trim();
                    if (t.Length >= 3)
                    {
                        cleaned = System.Text.RegularExpressions.Regex.Replace(
                            cleaned,
                            System.Text.RegularExpressions.Regex.Escape(t),
                            "",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    }
                }
            }

            cleaned = System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                @"\(\s*(Power\s+Sub|Super\s+Sub|Epic\s+Sub|Sub\s+on\s+Play\s+on|Sub\s+On|incl\.\s*overtime|Settled\s+using\s+Opta\s+data)\s*\)",
                "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            cleaned = System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                @"\b(player's|players|player|over|under|yes|no|total|team|home|away|fouls|foul|fouled|won|drawn|suffered|conceded|committed|win|against|by|to|be|or|at|least|anytime|alternative|1st|2nd|first|second|half|full|time|match|in|of|number|most|more|fc|afc|cf|sc|ac|united|city|town|rovers|albion|wanderers|hotspur|athletic|atletico|sporting|real|borussia|dynamo)\b",
                "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"[0-9\.\+\-:\(\)\/']", "").Trim();
            return cleaned.Length >= 3;
        }

        public async Task<string?> ConfirmOutcomeAsync(string extractedBetDataJson, DateTime betPlacedAt, DateTime? matchStartTime = null, int? betId = null, string thinkingLevel = "medium")
        {
            var token = await GetVertexAccessTokenAsync();
            if (string.IsNullOrEmpty(token)) return "Error: Gemini Auth Token missing.";

            bool addedHere = false;
            if (betId.HasValue)
            {
                addedHere = ProcessingOutcomeBetIds.TryAdd(betId.Value, true);
            }

            try
            {
                string betLabel = betId.HasValue ? $"[Bet #{betId.Value}] [Outcome Check] " : "[Outcome Check] ";

                                var prompt = $@"You are a sports betting expert analyzing a bet slip uploaded on {betPlacedAt:yyyy-MM-dd HH:mm} UTC (Year: {betPlacedAt.Year}). Current UTC time: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC.
{extractedBetDataJson}

# CORE DIRECTIVES
- You must grade each leg independently using the provided JSON data (your primary source of truth).
- If JSON is missing or lacks specific stats, use Google Search strictly following the rules below.
- DO NOT hallucinate, guess, or invent stats. If data is missing or ambiguous, output 'Unknown'.

# 1. API VERIFICATION (JSON)
- If JSON (FotMob or ESPN) is provided, the match is correct even if the start time is slightly before the upload date. Do not reject it.
- Your `fullAnalysis` must start with API Status (e.g. 'API Status: Scores found (1-0)').
- Use FULL-TIME (FT) stats unless the market explicitly says '1st Half' or 'Half Time'.
- MATCH FINISHED LOGIC: For FotMob, a match is definitively finished ONLY if `header.status.reason.short` is 'FT', 'AET', or 'PEN'. For ESPN, a match is finished ONLY if `status` is 'Final' or `isFinished: true`. If JSON indicates 'Scheduled', 'In Progress', or `isFinished: false`, grade all legs as 'Pending'!
- EXTRA TIME: Events in Extra Time (e.g. 111th minute) DO NOT COUNT unless the market says 'To Qualify', 'To Lift Trophy', or 'Including Extra Time'. Regular time is 90 mins + injury time. For example, if a player receives a yellow card in Extra Time, a standard 'Player Booked' bet is LOST.

# 2. GOOGLE SEARCH STRATEGY (2-STEP VERIFICATION)
When evaluating matches without API JSON (e.g. European hockey, lower leagues, MMA, Formula 1):
- STEP 1 (CHECK MATCH STATUS & START TIME FIRST):
  Before searching for player stats, first verify whether the match scheduled for {betPlacedAt:yyyy-MM-dd} (Year: {betPlacedAt.Year}) has actually FINISHED!
  Always include the year '{betPlacedAt.Year}' in your search query (e.g. 'Buffalo Sabres vs Florida Panthers score {betPlacedAt.Year}' or '{betPlacedAt:yyyy-MM-dd}').
  * If the match is UPCOMING or CURRENTLY IN PROGRESS (Live / 1st/2nd/3rd period / 1st/2nd half), STOP! Do NOT search for player box scores. Grade all legs as 'Pending' and return the match start time in `matchStartTimeIso`.
  * STALE / UNINDEXED NICHE MATCHES: Compare the match start time against the Current UTC time ({DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC). If more than 3.5 hours have passed since the match started (or >4.5 hours for American Football/Baseball/MMA) and Google Search still has no verified final score (or only shows pre-match/abandoned live-tracker pages), the match window has already passed without indexed coverage. Do NOT grade it as 'Pending'—grade the leg as 'Unknown' so it escalates to manual review instead of polling indefinitely.
- STEP 2 (PLAYER PROPS ONLY FOR COMPLETED MATCHES):
  Only if Step 1 confirmed the match on {betPlacedAt:yyyy-MM-dd} is 100% FINAL / COMPLETED, search for player box scores for that specific {betPlacedAt.Year} match.
  Append reputable domains:
  - NFL/NBA/NHL/MLB: '(site:espn.com OR site:cbssports.com OR site:foxsports.com OR site:sports.yahoo.com OR site:nhl.com)'
  - Soccer: '(site:espn.com/soccer OR site:skysports.com/football OR site:bbc.com/sport/football OR site:foxsports.com/soccer)'
  - CS/Esports: 'site:hltv.org/matches'
  Quote player names (e.g. 'Sabres vs Panthers ""Sam Bennett"" points {betPlacedAt.Year}'). Do NOT search FlashScore, LiveScore, or SofaScore.

# 3. STRICT ANTI-HALLUCINATION & YEAR MATCHING
- EXACT YEAR AND DATE: The bet was placed for the match on {betPlacedAt:yyyy-MM-dd} (Year: {betPlacedAt.Year}). You MUST strictly verify the match took place in {betPlacedAt.Year}! NEVER use historical box scores or recaps from past years (e.g. 2023, 2024, 2025). If Google returns an article from a past year because today's match is live or has no recap yet, REJECT IT. If the match is in progress, mark 'Pending'. If no verified data for {betPlacedAt.Year} exists, mark 'Unknown'.
- NEVER use prediction articles, previews, or odds sites (lines.com, actionnetwork).
- TEAMS: Verify the exact Home vs Away order.

# 4. PLAYER PROPS & VOIDS
- RULE 1: If a player plays 0 minutes / does not play (`didNotPlay: true`, not in squad, left on bench), outcome is ALWAYS 'Void' (even for live/Power Sub).
- RULE 2 (SOCCER ONLY): For `sport: 'Soccer'` with `isLive: false`, the player MUST START. If they are subbed on later, it is 'Void'.
- RULE 3 (LIVE BETS & NON-SOCCER SPORTS): If `isLive: true`, OR for non-Soccer sports (Basketball, Ice Hockey, American Football, etc.), the starter rule does NOT apply; if the player plays/participates in the match at all (even off the bench), the bet stands.
- POWER SUB: If selection contains '(Power Sub)' (or market/selection mentions 'Epic Sub' / 'Super Sub' / 'Power Sub') and the named player STARTED, stats of their substitute are ADDED to theirs. Find this in the 'Substitution' events array ('swap'). If the named player didn't start, Power Sub is ignored (evaluate via rules above).

# 5. MARKETS & LINES
- ASIAN LINES (.0): Any line ending in exactly '.0', '.25', or '.75' is Asian. If it lands exactly on a '.0' line (tie), outcome is 'Void' (Refund), unless it explicitly says '3-way'.
- SPLIT ASIAN LINES (Half-Win/Half-Loss): If a line like '-0.5, -1.0' or '2.25' results in a Half-Win or Half-Loss, mark it as 'Unknown'.
- LIVE ASIAN HANDICAPS: If a market has a score like '(0-1)', subtract this starting score from the final score BEFORE applying the handicap.
- '1' AND '2' TOTALS: If a market says 'Total Goals 1' with selection 'Over 1.0', the '1' might be a truncation of the Asian Line (1.0), not Team 1. If no specific team is named, treat it as TOTAL match goals.

# 6. FORMATTING & SCHEDULING
- If the match has NOT STARTED or is CURRENTLY IN PROGRESS (e.g. `isFinished: false`, or `status` is 'Scheduled' / 'In Progress'), grade all legs as 'Pending'. DO NOT grade live matches as 'Unknown' or 'Void' just because final stats are missing.
- `matchStartTimeIso`: DO NOT PUT THIS AT THE ROOT! You MUST put this inside EACH object in the `legs` array! For EVERY leg (whether finished, live, or pending), return the absolute UTC start time of that specific match (e.g. '{betPlacedAt.Year}-10-12T17:00:00Z'). Parse it directly from FotMob/ESPN JSON if available; if not available, search Google for the match start time.
- `stats`: Start with EXACTLY ONE of: 'Verified via FotMob: ', 'Verified via ESPN: ', 'FotMob lacked stat; Verified via Google Search: ', 'ESPN lacked stat; Verified via Google Search: ', or 'Verified via Google Search: '. Include EXACTLY ONE source URL if you used Google. BET BUILDERS: Evaluate each leg COMPLETELY INDEPENDENTLY! You MUST write a unique, specific 'stats' reasoning for EACH leg. Do NOT copy and paste the same stats across multiple legs.
- `verificationSource`: You MUST provide EXACTLY ONE of the following strict categories: 'FotMob_Verified', 'ESPN_Verified', 'FotMob_MissingStat', 'ESPN_MissingStat', 'Google_Verified', or 'Unknown'.
- `outcome`: Strictly use EXACTLY ONE of: 'Won', 'Lost', 'Void', 'Pending', or 'Unknown'. NO EMOJIS! NO EXTRA TEXT!";

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var extractionResult = JsonSerializer.Deserialize<AiVisionExtractionResult>(extractedBetDataJson, options);
                
                var partsList = new List<Dictionary<string, object>>
                {
                    new Dictionary<string, object> { { "text", "Please determine the outcome of this bet according to the strict system instructions and the attached sports JSON data." } }
                };

                bool anyLegNeedsGoogleSearch = false;
                bool allLegsConfirmedUnstarted = extractionResult?.Legs != null && extractionResult.Legs.Count > 0;
                var unstartedMatchInfo = new Dictionary<string, (string Provider, DateTime StartTimeUtc)>(StringComparer.OrdinalIgnoreCase);

                if (extractionResult?.Legs != null)
                {
                    var groupedLegs = extractionResult.Legs.GroupBy(l => l.Match);
                    foreach (var group in groupedLegs)
                    {
                        string matchName = group.Key ?? "";
                        string legSport = group.FirstOrDefault()?.Sport ?? "Soccer";

                        string? rawJson = null;
                        string providerLabel = "";

                        DateTime? legStartTime = group.FirstOrDefault(l => l.StartTime > DateTime.MinValue)?.StartTime ?? matchStartTime;

                        // 1. Soccer -> FotMob
                        if (legSport.Equals("Soccer", StringComparison.OrdinalIgnoreCase))
                        {
                            rawJson = await _fotMob.GetMatchStatsJsonAsync(matchName, betPlacedAt, betId, legStartTime);
                            providerLabel = "FotMob";
                        }
                        else
                        {
                            // 2. US Sports (American Football, Ice Hockey, Basketball, Baseball) & Tennis -> ESPN
                            rawJson = await _espn.GetMatchStatsJsonAsync(matchName, legSport, betPlacedAt, legStartTime, betId);
                            providerLabel = "ESPN";
                        }

                        if (!string.IsNullOrEmpty(rawJson))
                        {
                            partsList.Add(new Dictionary<string, object>
                            {
                                { "text", $"=== {providerLabel.ToUpperInvariant()} RAW JSON FOR MATCH {matchName} ===\n{rawJson}\n=========================" }
                            });

                            if (allLegsConfirmedUnstarted && TryExtractUnstartedMatchStartTime(rawJson, providerLabel, out DateTime parsedStartUtc))
                            {
                                unstartedMatchInfo[matchName] = (providerLabel, parsedStartUtc);
                            }
                            else
                            {
                                allLegsConfirmedUnstarted = false;
                            }
                        }
                        else
                        {
                            _logger.LogInformation($"{betLabel}⚠️ {providerLabel} data not found for '{matchName}' -> Falling back to Google Search.");
                            // Neither provider found data for this match -> Leg requires Google Search fallback
                            anyLegNeedsGoogleSearch = true;
                            allLegsConfirmedUnstarted = false;
                        }
                        
                        // Add a small delay to avoid rate limits on slips with multiple matches
                        await Task.Delay(1000);
                    }
                }

                // Short-circuit: If every leg on the slip is confirmed by FotMob/ESPN to not have started yet and has a valid UTC start time,
                // return the scheduled start times immediately without calling Gemini ($0 AI cost, 0ms AI latency).
                if (allLegsConfirmedUnstarted && !anyLegNeedsGoogleSearch && extractionResult?.Legs != null && unstartedMatchInfo.Count > 0)
                {
                    var shortCircuitResult = new AiOutcomeResultData
                    {
                        OverallStatus = "MATCH NOT STARTED",
                        FullAnalysis = "API Status: Match has not started yet.",
                        Legs = extractionResult.Legs.Select(l =>
                        {
                            string mName = l.Match ?? "";
                            var info = unstartedMatchInfo.TryGetValue(mName, out var found) ? found : unstartedMatchInfo.Values.First();
                            string isoStr = info.StartTimeUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
                            return new AiOutcomeLegResult
                            {
                                Match = mName,
                                Outcome = "Pending",
                                VerificationSource = info.Provider == "FotMob" ? "FotMob_Verified" : "ESPN_Verified",
                                MatchStartTimeIso = isoStr,
                                Stats = $"Verified via {info.Provider}: Match scheduled for {info.StartTimeUtc:yyyy-MM-dd HH:mm} UTC (Not started yet)."
                            };
                        }).ToList()
                    };

                    _logger.LogInformation($"{betLabel}⚡ All legs confirmed unstarted via FotMob/ESPN -> Skipping Gemini AI call ($0 AI cost). Status: MATCH NOT STARTED");
                    return JsonSerializer.Serialize(shortCircuitResult);
                }

                // Insert the system instructions directly into the user prompt to bypass the Extended Thinking bug
                partsList.Insert(0, new Dictionary<string, object> { { "text", prompt } });

                var schemaJson = @"{
                    ""type"": ""OBJECT"",
                    ""properties"": {
                        ""fullAnalysis"": { ""type"": ""STRING"", ""nullable"": true },
                        ""legs"": {
                            ""type"": ""ARRAY"",
                            ""items"": {
                                ""type"": ""OBJECT"",
                                ""properties"": {
                                    ""match"": { ""type"": ""STRING"", ""nullable"": true },
                                    ""stats"": { ""type"": ""STRING"", ""nullable"": true },
                                    ""outcome"": { ""type"": ""STRING"", ""nullable"": true },
                                    ""verificationSource"": { ""type"": ""STRING"", ""nullable"": true },
                                    ""matchStartTimeIso"": { ""type"": ""STRING"", ""nullable"": true }
                                }
                            }
                        }
                    }
                }";

                var payload = new Dictionary<string, object>
                {
                    ["contents"] = new[]
                    {
                        new Dictionary<string, object>
                        {
                            ["role"] = "user",
                            ["parts"] = partsList
                        }
                    }
                };

                // Only attach Google Search Grounding if at least one leg could not be found via FotMob or ESPN!
                // This guarantees $0 search fee on all Soccer and ESPN-supported matches!
                if (anyLegNeedsGoogleSearch)
                {
                    payload["tools"] = new[]
                    {
                        new Dictionary<string, object>
                        {
                            ["googleSearch"] = new Dictionary<string, object>()
                        }
                    };
                }

                // If any leg falls back to Google Search, elevate thinking to "high" for rigorous cross-referencing and verification.
                // For standard API-verified matches (FotMob/ESPN), use "medium" to save tokens and prevent quota throttling.
                string effectiveThinkingLevel = anyLegNeedsGoogleSearch ? "high" : thinkingLevel;

                payload["generationConfig"] = new Dictionary<string, object>
                {
                    ["maxOutputTokens"] = 16384,
                    ["responseMimeType"] = "application/json",
                    ["responseSchema"] = JsonSerializer.Deserialize<object>(schemaJson, options)!,
                    ["thinkingConfig"] = new Dictionary<string, object>
                    {
                        ["thinkingLevel"] = effectiveThinkingLevel
                    }
                };


                var jsonPayload = JsonSerializer.Serialize(payload);
                
                // Hardcoded to the latest available Vertex AI enterprise model
                var resolvedModel = "gemini-3.8-flash";

                var url = $"https://aiplatform.googleapis.com/v1/projects/castle-gemini/locations/global/publishers/google/models/{resolvedModel}:generateContent";
                
                string initialProviderTag = IsUsingDevApiPrimary ? "[Gemini-Dev-API] " : "[Vertex-AI] ";
                if (anyLegNeedsGoogleSearch)
                {
                    _logger.LogInformation($"{betLabel}{initialProviderTag}🔍 Calling Gemini with Google Search Grounding ENABLED (Model: {resolvedModel}, Thinking: {effectiveThinkingLevel})...");
                }
                else
                {
                    _logger.LogInformation($"{betLabel}{initialProviderTag}🛡️ Calling Gemini with Google Search Grounding DISABLED - $0 Search Cost (Model: {resolvedModel}, Thinking: {effectiveThinkingLevel})...");
                }
                
                var (response, elapsedMs, usedProviderTag) = await SendWithRetryAsync(url, resolvedModel, jsonPayload, betLabel, token, timeoutSeconds: 300);
                if (!response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    return $"Error checking outcome ({usedProviderTag.Trim()}): {response.StatusCode} - {responseContent}";
                }

                var json = await response.Content.ReadAsStringAsync();
                
                LogAiUsage(json, $"{betLabel}{usedProviderTag}", elapsedMs, response.StatusCode.ToString());

                using var doc = JsonDocument.Parse(json);
                var text = ExtractGeminiText(doc);

                if (!string.IsNullOrEmpty(text))
                {
                    int startIndex = text.IndexOf('{');
                    int endIndex = text.LastIndexOf('}');
                    if (startIndex >= 0 && endIndex >= startIndex)
                    {
                        text = text.Substring(startIndex, endIndex - startIndex + 1);
                    }
                }

                string? finalJson = text?.Trim();
                if (!string.IsNullOrEmpty(finalJson))
                {
                    try
                    {
                        var resOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var resultObj = JsonSerializer.Deserialize<AiOutcomeResultData>(finalJson, resOptions);
                        if (resultObj != null && resultObj.Legs != null && resultObj.Legs.Count > 0)
                        {
                            var outcomes = resultObj.Legs.Select(l => l.Outcome?.ToUpperInvariant() ?? "").ToList();
                            
                            var originalBet = JsonSerializer.Deserialize<AiVisionExtractionResult>(extractedBetDataJson, resOptions);
                            bool isBetBuilder = originalBet?.IsBetBuilder ?? false;

                            bool hasVoid = outcomes.Any(o => o == "VOID");
                            bool hasWonOrLost = outcomes.Any(o => o == "WON" || o == "LOST");
                            bool hasUnknown = outcomes.Any(o => o == "UNKNOWN");
                            bool hasPending = outcomes.Any(o => o == "PENDING");
                            bool hasLost = outcomes.Any(o => o == "LOST");

                            string pendingStatus = "MATCH IN PROGRESS";
                            if (hasPending && resultObj.Legs != null) 
                            {
                                var pendingLegs = resultObj.Legs.Where(l => l.Outcome?.ToUpperInvariant() == "PENDING").ToList();
                                bool allFuture = true;
                                foreach (var leg in pendingLegs)
                                {
                                    if (DateTime.TryParse(leg.MatchStartTimeIso, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out DateTime st))
                                    {
                                        if (st <= DateTime.UtcNow) { allFuture = false; break; }
                                    }
                                    else
                                    {
                                        allFuture = false; break;
                                    }
                                }
                                if (allFuture && pendingLegs.Count > 0) pendingStatus = "MATCH NOT STARTED";
                            }

                            // Group legs to evaluate sub-betbuilders and single legs independently.
                            // We chunk them contiguously. If adjacent legs have the same match, they form a BetBuilder group.
                            // We also use originalBet's odds to detect if a new distinct bet starts for the same match.
                            var groupsByMatch = new List<List<AiOutcomeLegResult>>();
                            List<AiOutcomeLegResult>? currentGroup = null;
                            string? currentMatch = null;

                            for (int i = 0; i < resultObj.Legs!.Count; i++)
                            {
                                var leg = resultObj.Legs[i];
                                var origLeg = (originalBet?.Legs != null && i < originalBet.Legs.Count) ? originalBet.Legs[i] : null;
                                string match = leg.Match ?? "";

                                bool startNewGroup = false;
                                if (currentGroup == null || match != currentMatch)
                                {
                                    startNewGroup = true;
                                }
                                else if (origLeg != null && !string.IsNullOrWhiteSpace(origLeg.Odds))
                                {
                                    // Even if the match is the same, explicit odds mean a new bet on the slip
                                    startNewGroup = true;
                                }

                                if (startNewGroup)
                                {
                                    currentGroup = new List<AiOutcomeLegResult>();
                                    groupsByMatch.Add(currentGroup);
                                    currentMatch = match;
                                }
                                
                                currentGroup!.Add(leg);
                            }
                            
                            bool anyGroupDefinitivelyLost = false;

                            foreach (var group in groupsByMatch)
                            {
                                bool gHasPending = group.Any(l => l.Outcome?.ToUpperInvariant() == "PENDING");
                                bool gHasUnknown = group.Any(l => l.Outcome?.ToUpperInvariant() == "UNKNOWN");
                                bool gHasVoid = group.Any(l => l.Outcome?.ToUpperInvariant() == "VOID");
                                bool gHasLost = group.Any(l => l.Outcome?.ToUpperInvariant() == "LOST");

                                // A group is definitively lost if it is fully finished, has no voids, and has at least one loss.
                                if (!gHasPending && !gHasUnknown && !gHasVoid && gHasLost)
                                {
                                    anyGroupDefinitivelyLost = true;
                                    break;
                                }
                            }

                            // Determine Overall Status
                            if (anyGroupDefinitivelyLost)
                            {
                                // If ANY group is definitively lost, the entire betslip is immediately LOST
                                resultObj.OverallStatus = "LOST";
                            }
                            else if (hasUnknown)
                            {
                                resultObj.OverallStatus = "UNKNOWN";
                            }
                            else if (hasPending)
                            {
                                resultObj.OverallStatus = pendingStatus;
                            }
                            else if (hasVoid)
                            {
                                if (isBetBuilder)
                                {
                                    resultObj.OverallStatus = "UNKNOWN"; // Bookmaker BB void rules vary, requires manual review
                                }
                                else
                                {
                                    if (outcomes.All(o => o == "VOID")) resultObj.OverallStatus = "VOID";
                                    else resultObj.OverallStatus = "UNKNOWN"; // Needs manual odds recalculation for won/void mix
                                }
                            }
                            else if (outcomes.All(o => o == "WON"))
                            {
                                resultObj.OverallStatus = "WON";
                            }
                            else
                            {
                                resultObj.OverallStatus = "UNKNOWN";
                            }
                            
                            finalJson = JsonSerializer.Serialize(resultObj, new JsonSerializerOptions { WriteIndented = false });
                        }
                        
                        string status = resultObj?.OverallStatus ?? "UNKNOWN";
                        bool hasStartTime = !string.IsNullOrEmpty(resultObj?.MatchStartTimeIso);
                        
                        if (status == "MATCH IN PROGRESS" && hasStartTime)
                        {
                            _logger.LogInformation($"{betLabel}AI: Found start time -> {resultObj!.MatchStartTimeIso}");
                        }
                        else
                        {
                            _logger.LogInformation($"{betLabel}AI: Checked outcome -> Status: {status}");
                        }
                    }
                    catch { } // ignore parsing errors
                }

                return finalJson;
            }
            catch (Exception ex)
            {
                return $"Exception checking outcome: {ex.Message}";
            }
            finally
            {
                if (addedHere && betId.HasValue)
                {
                    ProcessingOutcomeBetIds.TryRemove(betId.Value, out _);
                }
            }
        }

        private void LogAiUsage(string jsonResponse, string betLabel, long elapsedMs = 0, string? status = null)
        {
            try 
            {
                using var doc = JsonDocument.Parse(jsonResponse);
                if (doc.RootElement.TryGetProperty("usageMetadata", out var usage))
                {
                    int total = usage.TryGetProperty("totalTokenCount", out var tt) ? tt.GetInt32() : 0;
                    int prompt = usage.TryGetProperty("promptTokenCount", out var pt) ? pt.GetInt32() : 0;
                    int output = usage.TryGetProperty("candidatesTokenCount", out var ct) ? ct.GetInt32() : 0;
                    int thinking = usage.TryGetProperty("thoughtsTokenCount", out var th) ? th.GetInt32() : 0;
                    
                    bool hasThought = jsonResponse.Contains("\"thoughtSignature\"");
                    string thinkMsg = thinking > 0 ? $" (Thinking: {thinking})" : (hasThought ? " (Thinking: active)" : "");
                    string timingMsg = elapsedMs > 0 ? $" ({elapsedMs}ms, {status ?? "OK"})" : "";

                    _logger.LogInformation($"{betLabel}📊 AI Usage{timingMsg} -> Total: {total} | Input: {prompt} | Output: {output}{thinkMsg}");
                }
            }
            catch { }
        }

        private string? ExtractGeminiText(JsonDocument doc)
        {
            if (doc.RootElement.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
            {
                var candidate = candidates[0];
                if (candidate.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts) && parts.GetArrayLength() > 0)
                {
                    // If thinking is enabled, the actual text is usually the last part
                    var textProp = parts[parts.GetArrayLength() - 1];
                    if (textProp.TryGetProperty("text", out var text))
                    {
                        return text.GetString();
                    }
                    else
                    {
                        return $"Error: Gemini returned a part without a 'text' property. Raw part: {textProp.ToString()}";
                    }
                }
                else if (candidate.TryGetProperty("finishReason", out var finishReason))
                {
                    string reason = finishReason.GetString() ?? "Unknown";
                    _logger.LogWarning($"[AI WARNING] Gemini generation stopped due to: {reason}");
                    return $"Error: Gemini generation stopped due to {reason}";
                }
            }
            else if (doc.RootElement.TryGetProperty("promptFeedback", out var feedback))
            {
                if (feedback.TryGetProperty("blockReason", out var blockReason))
                {
                    return $"Error: Gemini blocked the prompt due to {blockReason.GetString()}";
                }
            }
            else if (doc.RootElement.TryGetProperty("error", out var errorObj))
            {
                if (errorObj.TryGetProperty("message", out var errMsg))
                {
                    return $"Error: Gemini API error - {errMsg.GetString()}";
                }
            }
            
            return null;
        }

        private void RecordGatewayFailure(bool usedDevApi, string logLabel, int attemptIndex, string reason)
        {
            if (usedDevApi)
            {
                int failures = System.Threading.Interlocked.Increment(ref _consecutiveDevApiFailures);
                if (failures >= CircuitBreakerThreshold)
                {
                    var untilUtc = DateTime.UtcNow.Add(CircuitBreakerDuration);
                    long prevTicks = System.Threading.Interlocked.Exchange(ref _useVertexFallbackUntilTicks, untilUtc.Ticks);
                    if (prevTicks < DateTime.UtcNow.Ticks)
                    {
                        _logger.LogWarning($"{logLabel}⚠️ [AI Circuit Breaker] [Gemini-Dev-API] hit {failures} consecutive 429/timeouts ({reason}). Switching primary to [Vertex-AI] for {CircuitBreakerDuration.TotalMinutes:0} minutes (until {untilUtc:HH:mm:ss} UTC).");
                    }
                }
            }
            else if (attemptIndex >= 1 && !string.IsNullOrWhiteSpace(_apiKey))
            {
                if (System.Threading.Interlocked.Exchange(ref _useVertexFallbackUntilTicks, 0) > DateTime.UtcNow.Ticks)
                {
                    System.Threading.Interlocked.Exchange(ref _consecutiveDevApiFailures, 0);
                    _logger.LogWarning($"{logLabel}⚠️ [AI Circuit Breaker] [Vertex-AI] fallback also failed ({reason}). Resetting primary back to [Gemini-Dev-API].");
                }
            }
        }

        private async Task<(HttpResponseMessage Response, long ElapsedMs, string ProviderTag)> SendWithRetryAsync(string vertexUrl, string resolvedModel, string jsonPayload, string logLabel = "", string token = "", int timeoutSeconds = 180, System.Threading.CancellationToken callerToken = default)
        {
            int maxRetries = 3;
            bool hasDevApiKey = !string.IsNullOrWhiteSpace(_apiKey);
            string devApiUrl = hasDevApiKey
                ? $"https://generativelanguage.googleapis.com/v1beta/models/{resolvedModel}:generateContent?key={_apiKey}"
                : vertexUrl;
            bool startWithDevApi = IsUsingDevApiPrimary;
            bool forceFallbackAfterTimeout = false;

            bool UseDevApiForAttempt(int attempt) =>
                hasDevApiKey && (startWithDevApi
                    ? (!forceFallbackAfterTimeout && attempt < maxRetries - 1)
                    : (forceFallbackAfterTimeout || attempt == maxRetries - 1));

            for (int i = 0; i < maxRetries; i++)
            {
                bool useDevApi = UseDevApiForAttempt(i);
                string currentUrl = useDevApi ? devApiUrl : vertexUrl;
                string providerTag = useDevApi ? "[Gemini-Dev-API] " : "[Vertex-AI] ";

                var attemptSw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    using var cts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(callerToken);
                    cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                    var request = new HttpRequestMessage(HttpMethod.Post, currentUrl);
                    request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                    request.Headers.ConnectionClose = true;
                    if (!useDevApi && !string.IsNullOrEmpty(token))
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    }
                    
                    var response = await _httpClient.SendAsync(request, cts.Token);
                    attemptSw.Stop();
                    
                    if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable || response.StatusCode == (System.Net.HttpStatusCode)429)
                    {
                        RecordGatewayFailure(useDevApi, logLabel, i, response.StatusCode.ToString());
                        if (i < maxRetries - 1 && !callerToken.IsCancellationRequested)
                        {
                            int delaySec = 2 * (i + 1);
                            string nextProvider = UseDevApiForAttempt(i + 1) ? "[Gemini-Dev-API]" : "[Vertex-AI]";
                            _logger.LogWarning($"{logLabel}{providerTag}Gemini API returned {response.StatusCode} in {attemptSw.ElapsedMilliseconds}ms. Retrying via {nextProvider} (fresh connection) in {delaySec}s... (Attempt {i+1}/{maxRetries-1})");
                            response.Dispose();
                            await Task.Delay(delaySec * 1000, callerToken);
                            continue;
                        }
                    }
                    
                    if (response.IsSuccessStatusCode)
                    {
                        if (useDevApi)
                        {
                            System.Threading.Interlocked.Exchange(ref _consecutiveDevApiFailures, 0);
                            if (System.Threading.Interlocked.Exchange(ref _useVertexFallbackUntilTicks, 0) > DateTime.UtcNow.Ticks)
                            {
                                _logger.LogInformation($"{logLabel}✅ [AI Circuit Breaker] [Gemini-Dev-API] recovered. Restored [Gemini-Dev-API] as primary.");
                            }
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"{logLabel}{providerTag}Gemini API responded with status {response.StatusCode} in {attemptSw.ElapsedMilliseconds}ms");
                    }
                    return (response, attemptSw.ElapsedMilliseconds, providerTag);
                }
                catch (OperationCanceledException)
                {
                    attemptSw.Stop();
                    if (callerToken.IsCancellationRequested)
                    {
                        _logger.LogWarning($"{logLabel}{providerTag}Gemini API request reached overall deadline after {attemptSw.ElapsedMilliseconds}ms. Aborting retries.");
                        throw new TimeoutException($"Gemini API timed out after {attemptSw.ElapsedMilliseconds / 1000}s.");
                    }
                    RecordGatewayFailure(useDevApi, logLabel, i, "Timeout");
                    if (i == 0)
                    {
                        forceFallbackAfterTimeout = true;
                    }
                    if (i < maxRetries - 1)
                    {
                        int delaySec = 2 * (i + 1);
                        string nextProvider = UseDevApiForAttempt(i + 1) ? "[Gemini-Dev-API]" : "[Vertex-AI]";
                        _logger.LogWarning($"{logLabel}{providerTag}Gemini API request timed out after {attemptSw.ElapsedMilliseconds}ms (> {timeoutSeconds}s). Retrying via {nextProvider} in {delaySec}s... (Attempt {i+1}/{maxRetries-1})");
                        await Task.Delay(delaySec * 1000, callerToken);
                        continue;
                    }
                    _logger.LogError($"{logLabel}{providerTag}Gemini API request timed out after {maxRetries} attempts of {timeoutSeconds}s each.");
                    throw new TimeoutException($"Gemini API timed out after {maxRetries} attempts of {timeoutSeconds}s.");
                }
                catch (Exception ex) when (i < maxRetries - 1 && !callerToken.IsCancellationRequested)
                {
                    attemptSw.Stop();
                    string nextProvider = UseDevApiForAttempt(i + 1) ? "[Gemini-Dev-API]" : "[Vertex-AI]";
                    _logger.LogWarning($"{logLabel}{providerTag}Gemini API call failed ({ex.Message}). Retrying via {nextProvider} in 3s... (Attempt {i+1}/{maxRetries-1})");
                    await Task.Delay(3000, callerToken);
                    continue;
                }
            }
            throw new Exception("Max retries exceeded.");
        }

        public static bool TryExtractUnstartedMatchStartTime(string rawJson, string providerLabel, out DateTime utcStartTime)
        {
            utcStartTime = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(rawJson)) return false;

            try
            {
                using var doc = JsonDocument.Parse(rawJson);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return false;

                if (string.Equals(providerLabel, "FotMob", StringComparison.OrdinalIgnoreCase))
                {
                    if (!root.TryGetProperty("general", out var general) || general.ValueKind != JsonValueKind.Object)
                        return false;

                    bool isNotStarted = general.TryGetProperty("started", out var stProp) && stProp.ValueKind == JsonValueKind.False;
                    bool isFinished = general.TryGetProperty("finished", out var finProp) && finProp.ValueKind == JsonValueKind.True;
                    if (!isNotStarted || isFinished) return false;

                    string? timeStr = null;
                    if (general.TryGetProperty("matchTimeUTCDate", out var mtdProp) && mtdProp.ValueKind == JsonValueKind.String)
                    {
                        timeStr = mtdProp.GetString();
                    }
                    else if (general.TryGetProperty("matchTimeUTC", out var mtProp) && mtProp.ValueKind == JsonValueKind.String)
                    {
                        timeStr = mtProp.GetString();
                        if (!string.IsNullOrEmpty(timeStr))
                        {
                            // e.g. "Mon, Oct 26, 2026, 15:00 UTC" -> "Oct 26, 2026 15:00Z"
                            int commaIdx = timeStr.IndexOf(',');
                            if (commaIdx >= 0 && commaIdx < 5) timeStr = timeStr.Substring(commaIdx + 1).Trim();
                            timeStr = timeStr.Replace(", ", " ").Replace(" UTC", "Z");
                        }
                    }

                    if (!string.IsNullOrEmpty(timeStr) &&
                        DateTime.TryParse(timeStr, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out utcStartTime))
                    {
                        return utcStartTime > DateTime.UtcNow.AddMinutes(-5);
                    }
                    return false;
                }
                else if (string.Equals(providerLabel, "ESPN", StringComparison.OrdinalIgnoreCase))
                {
                    bool isFinished = root.TryGetProperty("isFinished", out var finProp) && finProp.ValueKind == JsonValueKind.True;
                    if (isFinished) return false;

                    string status = root.TryGetProperty("status", out var stProp) && stProp.ValueKind == JsonValueKind.String
                        ? (stProp.GetString() ?? "")
                        : "";
                    if (!string.Equals(status, "Scheduled", StringComparison.OrdinalIgnoreCase))
                        return false;

                    if (root.TryGetProperty("startTimeIso", out var isoProp) && isoProp.ValueKind == JsonValueKind.String)
                    {
                        string? isoStr = isoProp.GetString();
                        if (!string.IsNullOrEmpty(isoStr) &&
                            DateTime.TryParse(isoStr, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out utcStartTime))
                        {
                            return utcStartTime > DateTime.UtcNow.AddMinutes(-5);
                        }
                    }
                    return false;
                }
            }
            catch
            {
                // If API format ever changes, gracefully return false so ConfirmOutcomeAsync falls back to Gemini AI
            }

            return false;
        }

    }
}
