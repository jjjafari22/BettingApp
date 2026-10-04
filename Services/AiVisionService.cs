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
        
        private readonly HttpClient _httpClient;
        private readonly string? _apiKey;
        private readonly FotMobScraperService _fotMob;
        private readonly IConfiguration _config;
        private static Google.Apis.Auth.OAuth2.GoogleCredential? _cachedCredential;
        private static readonly object _credentialLock = new object();

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

        public AiVisionService(HttpClient httpClient, IConfiguration config, FotMobScraperService fotMob, ILogger<AiVisionService> logger)
        {
            _httpClient = httpClient;
            _config = config;
            _apiKey = config["GeminiApiKey"];
            _fotMob = fotMob;
            _logger = logger;
        }

        public async Task<(AiVisionExtractionResult? Result, string? Error)> ExtractBetSlipDataAsync(string imageUrl, int? betId = null)
        {
            var token = await GetVertexAccessTokenAsync();
            if (string.IsNullOrEmpty(token))
            {
                return (null, "Google Vertex Auth Token is missing or invalid. Check credentials.");
            }

            try
            {
                // 1. Download the image bytes from the URL
                var imageBytes = await _httpClient.GetByteArrayAsync(imageUrl);
                var base64Image = Convert.ToBase64String(imageBytes);

                // Determine mime type (rough guess based on extension, though Gemini usually figures it out)
                string mimeType = imageUrl.ToLower().EndsWith(".png") ? "image/png" : "image/jpeg";

                // 2. Build the Gemini JSON Payload
                var systemInstruction = "You are a sports betting OCR bot. Look at this betting slip screenshot and extract the bet details. " +
                             "The slip may contain a single bet or a combo (parlay/accumulator) with multiple bets (legs). " +
                             "Extract: " +
                             "1) bookmaker (e.g. 'Unibet', 'Bet365', 'Coolbet', 'EpicBet'). CRITICAL: If the logo is missing, you MUST guess based on UI colors: \n" +
                             "   - Coolbet: Dark theme, odds inside cyan/light-blue rounded boxes, 'PLACE BET' button is bright green, 'YOUR STAKE' input has a bright green border, potential return is yellow/orange.\n" +
                             "   - Unibet: Dark gray theme, 'Place bet' (or 'Placer væddemål') button is a bright yellow block, numeric keyboard has bright green keys, often has a dark green banner at the top.\n" +
                             "   - Bet365: Green header, grey/yellow UI elements.\n" +
                             "   If you genuinely cannot guess, output 'Unknown'. Do NOT leave it empty! " +
                             "2) isLive (boolean, true ONLY if you clearly see an explicit 'LIVE' or 'In-Play' badge, or an ongoing current match score (e.g. '1-0', '0-0') printed prominently near the teams. CRITICAL: Do NOT confuse market modifiers like '1st Half' or lines like 'Over 2.5' as live indicators! Default to false unless you are absolutely certain it is live). " +
                             "3) totalOdds (the final combined odds of the slip. CRITICAL: Never include UI CSS units like 'px'! Extract ONLY the clean numerical value). " +
                             "4) stake (the amount bet, e.g. '100', '1000'). CRITICAL: Often the user will manually draw or write their stake over the image with a digital pen. You MUST look for manual handwritten digits over the image indicating the stake and prioritize that over printed text! " +
                             "5) legs: an array of objects representing each individual bet, containing: " +
                             "   - match (e.g. 'Arsenal vs Man City'). CRITICAL: You MUST translate the team names into their standard, globally recognized English names (e.g. you MUST output 'FC Copenhagen' instead of 'FC København', and 'Bayern Munich' instead of 'Bayern München'). This is required for our Odds API to find the match. " +
                             "   - market (e.g. 'Asian Handicap (0-1)', 'Total Cards'). CRITICAL: If the market is in another language (e.g. Danish 'Kort i alt'), translate it to English. CRITICAL: If the market includes a specific line, handicap, or point spread (e.g., '(0-1)', '-1.5', '+2.5'), you MUST include that numerical modifier in the market name! Do not leave it out! CRITICAL: If the market name includes a team name (e.g., 'FC Midtjylland Total Goals'), you MUST include the team name exactly as written. Do NOT summarize it! CRITICAL: NEVER drop decimals from numbers in the market or selection. CRITICAL: Output ONLY the final translated market name. NEVER output your reasoning or 'Let's check' in this field! " +
                             "   - selection (the specific bet chosen, e.g. 'Arsenal' or 'Under 2.5'). CRITICAL: If this is a player prop, you MUST include the exact condition (e.g. 'Marcus Rashford - Will Score'). Do NOT just write the player's name! CRITICAL: Output ONLY the final selection string. NEVER output your reasoning or thoughts in this field! " +
                             "   - badges (an array of strings). CRITICAL: Look carefully for any special promo labels, text, or visual icons near the bet (e.g., 'Power Sub', 'Sub on Play on', 'Super Sub', 'Early Payout', 'Super Boost'). IMPORTANT FOR POWER SUB: Some bookmakers do not write the text, but instead use a visual icon next to the player (such as two arrows pointing in opposite directions, a 'swap' symbol, or a substitution icon). If you see a visual icon that clearly represents a player substitution, you MUST add 'Power Sub' to this array. Be careful not to confuse generic UI arrows (like dropdown arrows) with a substitution icon! " +
                             "   - odds (e.g. '1.95'). CRITICAL: If multiple legs are part of a Bet Builder (grouped together for the same match) and visually share a single combined odds value, you MUST output that shared odds value for the FIRST leg, and output null for the subsequent legs in that Bet Builder.";

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
                        maxOutputTokens = 8192,
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
                                            legThoughtProcess = new { type = "STRING", description = "Your detailed, step-by-step reasoning for extracting and translating this specific leg." },
                                            match = new { type = "STRING", nullable = true },
                                            market = new { type = "STRING", nullable = true },
                                            selection = new { type = "STRING", nullable = true },
                                            badges = new { type = "ARRAY", items = new { type = "STRING" } },
                                            odds = new { type = "STRING", nullable = true }
                                        },
                                        required = new[] { "legThoughtProcess", "match", "market", "selection", "badges", "odds" }
                                    }
                                }
                            },
                            required = new[] { "thoughtProcess", "bookmaker", "isLive", "totalOdds", "stake", "legs" }
                        },
                        thinkingConfig = new { thinkingBudget = 1024 }
                    }
                };

                var jsonPayload = JsonSerializer.Serialize(payload);
                
                // Hardcoded to the latest available Vertex AI enterprise model
                var resolvedModel = "gemini-3.8-flash";

                var apiUrl = $"https://aiplatform.googleapis.com/v1/projects/castle-gemini/locations/global/publishers/google/models/{resolvedModel}:generateContent";
                
                string betLabel = betId.HasValue ? $"[Bet #{betId.Value}] " : "[Extraction] ";
                _logger.LogInformation($" {betLabel}AI Auto-Read calling Gemini (Model: {resolvedModel})...");

                var response = await SendWithRetryAsync(apiUrl, jsonPayload, betLabel, token);
                string responseString = await response.Content.ReadAsStringAsync();
                
                if (response.IsSuccessStatusCode)
                {
                    LogAiUsage(responseString, betLabel);
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError($"{betLabel}AI ERROR: {response.StatusCode} - {responseString}");
                    return (null, $"Gemini API Error: {response?.StatusCode}\nResolved Model: {resolvedModel}\nDetails: {responseString}");
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
                        foreach (var leg in result.Legs)
                        {
                            if (leg != null && string.IsNullOrWhiteSpace(leg.Match) && !string.IsNullOrWhiteSpace(leg.Selection))
                            {
                                // We pass DateTime.UtcNow since ExtractBetSlipDataAsync doesn't have the explicit betPlacedAt from DB yet, 
                                // but the bet slip is freshly uploaded so UtcNow is perfectly fine!
                                string? resolvedMatch = await _fotMob.ResolvePlayerMatchAsync(leg.Selection, DateTime.UtcNow, betId);
                                if (!string.IsNullOrEmpty(resolvedMatch))
                                {
                                    leg.Match = resolvedMatch;
                                    _logger.LogInformation($" {betLabel}FotMob Auto-Resolved missing match to: '{resolvedMatch}' for player '{leg.Selection}'");
                                }
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
                                    combinedText.Contains("Super Sub", StringComparison.OrdinalIgnoreCase))
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
                    }

                    return (result, null);
                }

                return (null, "No candidates returned from Gemini.");
            }

            catch (Exception ex)
            {
                string betLabel = betId.HasValue ? $"[Bet #{betId.Value}] " : "[Extraction] ";
                _logger.LogError($"{betLabel}EXCEPTION in ExtractBetSlipDataAsync: {ex.ToString()}");
                return (null, $"Exception: {ex.Message}");
            }
        }

        public async Task<string?> ConfirmOutcomeAsync(string extractedBetDataJson, DateTime betPlacedAt, DateTime? matchStartTime = null, int? betId = null)
        {
            var token = await GetVertexAccessTokenAsync();
            if (string.IsNullOrEmpty(token)) return "Error: Gemini Auth Token missing.";

            try
            {
                string betLabel = betId.HasValue ? $"[Bet #{betId.Value}]" : "[Test/Manual]";

                                var prompt = $@"You are a sports betting expert analyzing a bet slip uploaded at {betPlacedAt:yyyy-MM-dd HH:mm}.
{extractedBetDataJson}

# CORE DIRECTIVES
- You must grade each leg independently using the provided JSON data (your primary source of truth).
- If JSON is missing or lacks specific stats, use Google Search strictly following the rules below.
- DO NOT hallucinate, guess, or invent stats. If data is missing or ambiguous, output 'Unknown'.

# 1. API VERIFICATION (JSON)
- If JSON is provided, the match is correct even if the start time is slightly before the upload date. Do not reject it.
- Your `fullAnalysis` must start with API Status (e.g. 'API Status: Scores found (1-0)').
- Use FULL-TIME (FT) stats unless the market explicitly says '1st Half' or 'Half Time'.
- MATCH FINISHED LOGIC: A match is definitively finished ONLY if `header.status.reason.short` is 'FT', 'AET', or 'PEN', or if the match clearly reached full time and is not ongoing. FotMob's `general.finished` and `header.status.finished` flags are sometimes incorrectly true while the match is still live! You MUST ignore `finished: true` if `header.status.ongoing` is true, if `liveTime` is present, or if `reason.short` indicates a live minute (e.g. '83\\''). Do not grade bets as finished prematurely!
- EXTRA TIME: Events in Extra Time (e.g. 111th minute) DO NOT COUNT unless the market says 'To Qualify', 'To Lift Trophy', or 'Including Extra Time'. Regular time is 90 mins + injury time. For example, if a player receives a yellow card in Extra Time, a standard 'Player Booked' bet is LOST.

# 2. GOOGLE SEARCH STRATEGY & SITES
- SITES: To ensure conclusive results, use the 'OR' operator to check multiple reputable domains simultaneously! Do NOT search FlashScore, LiveScore, or SofaScore.
   - NFL/NBA/NHL/MLB: Append '(site:espn.com OR site:cbssports.com OR site:foxsports.com OR site:sports.yahoo.com)'
   - Soccer/Football: Append '(site:espn.com/soccer OR site:skysports.com/football OR site:bbc.com/sport/football OR site:foxsports.com/soccer)'
   - CS/Esports: Append 'site:hltv.org/matches'
- PLAYER PROPS: Text snippets rarely have full box scores. You MUST force Google to find the exact player by wrapping their name in quotes (e.g., 'Eagles vs Cowboys ""Jalen Hurts"" passing yards').

# 3. GOOGLE SEARCH ANTI-HALLUCINATION
- NEVER use prediction articles, previews, or odds sites (lines.com, actionnetwork).
- You MUST explicitly read the ACTUAL FINAL SCORE and/or EXACT PLAYER STAT from the snippet. If the search reveals the match has NOT STARTED or is CURRENTLY IN PROGRESS, output 'Pending'. If the match is FINISHED but stats are vague or missing, output 'Unknown'. DO NOT invent '4-0' or guess player stats based on team scores.
- EXACT DATE: You must verify the match date is within 5 days of {betPlacedAt:yyyy-MM-dd}. Reject old/historical matches and mark 'Unknown'. Do NOT grade based on unverified dates.
- EXACT TIME: If the leg has a 'startTime', find the exact UTC match. If no 'startTime', find the FIRST match chronologically on/after the upload time.
- TEAMS: Verify the exact Home vs Away order.

# 4. PLAYER PROPS & VOIDS
- RULE 1: If a player plays 0 minutes (not in squad, left on bench), outcome is ALWAYS 'Void' (even for live/Power Sub).
- RULE 2: If `isLive: false`, the player MUST START. If they are subbed on later, it is 'Void'.
- RULE 3: If `isLive: true`, the starter rule does not apply; if they play at all, the bet stands.
- POWER SUB: If selection contains '(Power Sub)' and the named player STARTED, stats of their substitute are ADDED to theirs. Find this in the 'Substitution' events array ('swap'). If the named player didn't start, Power Sub is ignored (evaluate via rules above).

# 5. MARKETS & LINES
- ASIAN LINES (.0): Any line ending in exactly '.0', '.25', or '.75' is Asian. If it lands exactly on a '.0' line (tie), outcome is 'Void' (Refund), unless it explicitly says '3-way'.
- SPLIT ASIAN LINES (Half-Win/Half-Loss): If a line like '-0.5, -1.0' or '2.25' results in a Half-Win or Half-Loss, mark it as 'Unknown'.
- LIVE ASIAN HANDICAPS: If a market has a score like '(0-1)', subtract this starting score from the final score BEFORE applying the handicap.
- '1' AND '2' TOTALS: If a market says 'Total Goals 1' with selection 'Over 1.0', the '1' might be a truncation of the Asian Line (1.0), not Team 1. If no specific team is named, treat it as TOTAL match goals.

# 6. FORMATTING & SCHEDULING
- If the match has NOT STARTED or is CURRENTLY IN PROGRESS (e.g. `general.started` is false, or `header.status.finished` is false), grade all legs as 'Pending'. DO NOT grade live matches as 'Unknown' or 'Void' just because final stats are missing.
- `matchStartTimeIso`: Return the absolute EARLIEST UTC start time across all PENDING/UNFINISHED legs (e.g. '2026-07-25T19:00:00Z'). Ignore finished legs. Parse it directly if provided in JSON; only Google Search if missing.
- `stats`: Start with EXACTLY ONE of: 'Verified via FotMob: ', 'FotMob lacked stat; Verified via Google Search: ', or 'Verified via Google Search: '. Include EXACTLY ONE source URL if you used Google. COMBO BETS: Evaluate each leg COMPLETELY INDEPENDENTLY! You MUST write a unique, specific 'stats' reasoning for EACH leg. Do NOT copy and paste the same stats across multiple legs. For example, if Leg 1 is Goalscorer and Leg 2 is Match Result, Leg 2's stats MUST discuss the match score, NOT the goalscorer.
- `outcome`: Strictly use EXACTLY ONE of: 'Won', 'Lost', 'Void', 'Pending', or 'Unknown'. NO EMOJIS! NO EXTRA TEXT!";

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var extractionResult = JsonSerializer.Deserialize<AiVisionExtractionResult>(extractedBetDataJson, options);
                
                var partsList = new List<Dictionary<string, object>>
                {
                    new Dictionary<string, object> { { "text", "Please determine the outcome of this bet according to the strict system instructions and the attached FotMob JSON data." } }
                };

                if (extractionResult?.Legs != null)
                {
                    var groupedLegs = extractionResult.Legs.GroupBy(l => l.Match);
                    foreach (var group in groupedLegs)
                    {
                        string matchName = group.Key ?? "";
                        
                        var rawJson = await _fotMob.GetMatchStatsJsonAsync(matchName, betPlacedAt, betId);
                        if (!string.IsNullOrEmpty(rawJson))
                        {
                            partsList.Add(new Dictionary<string, object>
                            {
                                { "text", $"=== FOTMOB RAW JSON FOR MATCH {matchName} ===\n{rawJson}\n=========================" }
                            });
                        }
                        else
                        {
                            // Explicit empty else block to prevent the delay from binding to it
                        }
                        
                        // Add a small delay to avoid rate limits on combo bets
                        await Task.Delay(1000);
                    }
                }

                // Insert the system instructions directly into the user prompt to bypass the Extended Thinking bug
                partsList.Insert(0, new Dictionary<string, object> { { "text", prompt } });

                var schemaJson = @"{
                    ""type"": ""OBJECT"",
                    ""properties"": {
                        ""matchStartTimeIso"": { ""type"": ""STRING"", ""nullable"": true },
                        ""fullAnalysis"": { ""type"": ""STRING"", ""nullable"": true },
                        ""legs"": {
                            ""type"": ""ARRAY"",
                            ""items"": {
                                ""type"": ""OBJECT"",
                                ""properties"": {
                                    ""match"": { ""type"": ""STRING"", ""nullable"": true },
                                    ""stats"": { ""type"": ""STRING"", ""nullable"": true },
                                    ""outcome"": { ""type"": ""STRING"", ""nullable"": true }
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
                    },
                    ["tools"] = new[]
                    {
                        new Dictionary<string, object>
                        {
                            ["googleSearch"] = new Dictionary<string, object>()
                        }
                    },
                    ["generationConfig"] = new Dictionary<string, object>
                    {
                        ["maxOutputTokens"] = 8192,
                        ["responseMimeType"] = "application/json",
                        ["responseSchema"] = JsonSerializer.Deserialize<object>(schemaJson, options)!,
                        ["thinkingConfig"] = new Dictionary<string, object> { ["thinkingBudget"] = 1024 }
                    }
                };

                var jsonPayload = JsonSerializer.Serialize(payload);
                
                // Hardcoded to the latest available Vertex AI enterprise model
                var resolvedModel = "gemini-3.8-flash";

                var url = $"https://aiplatform.googleapis.com/v1/projects/castle-gemini/locations/global/publishers/google/models/{resolvedModel}:generateContent";
                
                betLabel = betId.HasValue ? $"[Bet #{betId.Value}] " : "";
                _logger.LogInformation($" {betLabel}Check Outcome calling Gemini (Model: {resolvedModel})...");
                
                var response = await SendWithRetryAsync(url, jsonPayload, betLabel, token);
                if (!response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    return $"Error checking outcome: {response.StatusCode} - {responseContent}";
                }

                var json = await response.Content.ReadAsStringAsync();
                
                LogAiUsage(json, betLabel);

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

                            if (!isBetBuilder)
                            {
                                // In a standard combo, a single loss kills the entire parlay immediately, regardless of pending or void legs.
                                if (hasLost) resultObj.OverallStatus = "LOST";
                                else if (hasUnknown) resultObj.OverallStatus = "UNKNOWN";
                                else if (hasPending) resultObj.OverallStatus = "MATCH IN PROGRESS";
                                else if (hasVoid && outcomes.Any(o => o == "WON")) resultObj.OverallStatus = "UNKNOWN"; // Needs manual odds recalculation
                                else if (outcomes.All(o => o == "WON")) resultObj.OverallStatus = "WON";
                                else if (outcomes.All(o => o == "VOID")) resultObj.OverallStatus = "VOID";
                                else resultObj.OverallStatus = "UNKNOWN";
                            }
                            else
                            {
                                // In a Bet Builder, a Void leg often voids the entire slip. We must wait for all legs to finish (no pending/unknowns) before confirming a loss.
                                if (hasUnknown) resultObj.OverallStatus = "UNKNOWN";
                                else if (hasPending) resultObj.OverallStatus = "MATCH IN PROGRESS";
                                else if (hasVoid) resultObj.OverallStatus = "UNKNOWN"; // Bookmaker BB void rules vary, requires manual review
                                else if (hasLost) resultObj.OverallStatus = "LOST";
                                else if (outcomes.All(o => o == "WON")) resultObj.OverallStatus = "WON";
                                else resultObj.OverallStatus = "UNKNOWN";
                            }
                            
                            finalJson = JsonSerializer.Serialize(resultObj, new JsonSerializerOptions { WriteIndented = false });
                        }
                        
                        string localBetLabel = betId.HasValue ? $"[Bet #{betId.Value}]" : "[Test/Manual]";
                        string status = resultObj?.OverallStatus ?? "UNKNOWN";
                        bool hasStartTime = !string.IsNullOrEmpty(resultObj?.MatchStartTimeIso);
                        
                        if (status == "MATCH IN PROGRESS" && hasStartTime)
                        {
                            _logger.LogInformation($" {localBetLabel} AI: Found start time -> {resultObj!.MatchStartTimeIso}");
                        }
                        else
                        {
                            _logger.LogInformation($" {localBetLabel} AI: Checked outcome -> Status: {status}");
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
        }

        public async Task<DateTime?> ExtractMatchStartTimeAsync(string extractedBetDataJson, DateTime betPlacedAt)
        {
            var token = await GetVertexAccessTokenAsync();
            if (string.IsNullOrEmpty(token)) return null;

            try
            {
                var prompt = $"You are a sports scheduler. Here is the JSON data of a bet slip placed on {betPlacedAt:yyyy-MM-dd HH:mm}.\n" +
                             $"{extractedBetDataJson}\n\n" +
                             $"Your task is to identify the EARLIEST (FIRST) START TIME among all the matches listed in this bet slip.\n" +
                             $"Use Google Search to find the scheduled kick-off time for the matches. Make sure to look for matches occurring ON OR AFTER {betPlacedAt:yyyy-MM-dd}.\n" +
                             $"First, list out each match and the start time you found. Then, return a JSON object wrapped in a ```json code block containing a single field 'earliestMatchStartTimeUtc' with the ISO 8601 UTC timestamp of the earliest start time among all matches (e.g., '2026-07-23T18:00:00Z'). If you cannot find the time, return null for the field.";

                var payload = new
                {
                    contents = new[] { new { role = "user", parts = new[] { new { text = prompt } } } },
                    tools = new[] { new { googleSearch = new object() } },
                    generationConfig = new 
                    {
                        thinkingConfig = new 
                        {
                            thinkingBudget = 1024
                        }
                    }
                };

                var jsonPayload = JsonSerializer.Serialize(payload);
                
                // Hardcoded to the latest available Vertex AI enterprise model
                var resolvedModel = "gemini-3.8-flash";

                var url = $"https://aiplatform.googleapis.com/v1/projects/castle-gemini/locations/global/publishers/google/models/{resolvedModel}:generateContent";
                string betLabel = "[Match Start Time] ";
                _logger.LogInformation($" {betLabel}calling Gemini (Model: {resolvedModel})...");
                
                var response = await SendWithRetryAsync(url, jsonPayload, betLabel, token);
                
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync();
                
                LogAiUsage(json, betLabel);

                using var doc = JsonDocument.Parse(json);
                var text = ExtractGeminiText(doc);

                if (!string.IsNullOrEmpty(text))
                {
                    var jsonMatch = System.Text.RegularExpressions.Regex.Match(text, @"```json\s*(\{.*?\})\s*```", System.Text.RegularExpressions.RegexOptions.Singleline);
                    if (jsonMatch.Success)
                    {
                        text = jsonMatch.Groups[1].Value;
                    }

                    var resultDoc = JsonDocument.Parse(text.Trim());
                    if (resultDoc.RootElement.TryGetProperty("earliestMatchStartTimeUtc", out var timeElement) && timeElement.ValueKind != JsonValueKind.Null)
                    {
                        if (DateTime.TryParse(timeElement.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsedTime))
                        {
                            return parsedTime.ToUniversalTime();
                        }
                    }
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        private void LogAiUsage(string jsonResponse, string betLabel)
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
                    string warning = thinking >= 900 ? " ⚠️ WARNING: Approaching thinking limit!" : "";

                    _logger.LogInformation($" {betLabel}📊 AI Usage -> Total: {total} | Input: {prompt} | Output: {output}{thinkMsg}{warning}");
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

        private async Task<HttpResponseMessage> SendWithRetryAsync(string url, string jsonPayload, string logLabel = "", string token = "")
        {
            int maxRetries = 3;
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, url);
                    request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                    if (!string.IsNullOrEmpty(token))
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    }
                    
                    var response = await _httpClient.SendAsync(request);
                    
                    if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable || response.StatusCode == (System.Net.HttpStatusCode)429)
                    {
                        if (i < maxRetries - 1)
                        {
                            int delaySec = 5 * (i + 1);
                            _logger.LogWarning($"{logLabel}Gemini API returned {response.StatusCode}. Retrying in {delaySec}s... (Attempt {i+1}/{maxRetries-1})");
                            await Task.Delay(delaySec * 1000);
                            continue;
                        }
                    }
                    return response;
                }
                catch (TaskCanceledException)
                {
                    if (i < maxRetries - 1)
                    {
                        int delaySec = 5 * (i + 1);
                        _logger.LogWarning($"{logLabel}Gemini API TaskCanceled (Timeout). Retrying in {delaySec}s... (Attempt {i+1}/{maxRetries-1})");
                        await Task.Delay(delaySec * 1000);
                        continue;
                    }
                    throw;
                }
            }
            throw new Exception("Max retries exceeded.");
        }

    }
}
