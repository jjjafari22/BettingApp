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
        
        [JsonPropertyName("isCombo")]
        public bool IsCombo { get; set; }
        
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

        [JsonPropertyName("matchDate")]
        public string MatchDate { get; set; } = "";

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

        public AiVisionService(HttpClient httpClient, IConfiguration config, FotMobScraperService fotMob)
        {
            _httpClient = httpClient;
            _config = config;
            _apiKey = config["GeminiApiKey"];
            _fotMob = fotMob;
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
                var prompt = "You are a sports betting OCR bot. Look at this betting slip screenshot and extract the bet details. " +
                             "The slip may contain a single bet or a combo (parlay/accumulator) with multiple bets (legs). " +
                             "Extract: " +
                             "1) bookmaker (e.g. 'Unibet', 'Bet365', 'EpicBet', etc. derived from logos or UI style). " +
                             "2) isCombo (boolean, true if there are multiple bets combined). " +
                             "3) isLive (boolean, true if the bet was placed LIVE/In-Play. Look for 'LIVE' labels, or current match scores like '0-0', '1-0' printed near the market name or teams). " +
                             "4) totalOdds (the final combined odds of the slip, if visible). " +
                             "5) stake (the amount bet, e.g. '100', '1000'). CRITICAL: Often the user will manually draw or write their stake over the image with a digital pen. You MUST look for manual handwritten digits over the image indicating the stake and prioritize that over printed text! " +
                             "6) legs: an array of objects representing each individual bet, containing: " +
                             "   - match (e.g. 'Arsenal vs Man City'). CRITICAL: You MUST translate the team names into their standard, globally recognized English names (e.g. you MUST output 'FC Copenhagen' instead of 'FC København', and 'Bayern Munich' instead of 'Bayern München'). This is required for our Odds API to find the match. " +
                             "   - market (e.g. 'Asian Handicap (0-1)', 'Total Cards'). CRITICAL: If the market is in another language (e.g. Danish 'Kort i alt'), translate it to English. CRITICAL: If the market includes a specific line, handicap, or point spread (e.g., '(0-1)', '-1.5', '+2.5'), you MUST include that numerical modifier in the market name! Do not leave it out! " +
                             "   - selection (the specific bet chosen, e.g. 'Arsenal' or 'Under 2.5'). CRITICAL: If this is a player prop, you MUST include the exact condition (e.g. 'Marcus Rashford - Will Score'). Do NOT just write the player's name! " +
                             "   - badges (an array of strings). CRITICAL: Look carefully for any special promo labels, text, or visual icons near the bet (e.g., 'Power Sub', 'Sub on Play on', 'Super Sub', 'Early Payout', 'Super Boost'). IMPORTANT FOR POWER SUB: Some bookmakers do not write the text, but instead use a visual icon next to the player (such as two arrows pointing in opposite directions, a 'swap' symbol, or a substitution icon). If you see a visual icon that clearly represents a player substitution, you MUST add 'Power Sub' to this array. Be careful not to confuse generic UI arrows (like dropdown arrows) with a substitution icon! " +
                             "   - matchDate (e.g. '22.Aug 04:00', 'Tomorrow 18:00', or '2023-10-25'). CRITICAL: Look very carefully for the kickoff date and time for this match printed on the slip, or the date the bet was placed. Extract exactly what you see. If missing, return null. " +
                             "   - odds (e.g. '1.95'). " +
                             "Return ONLY a raw JSON object with keys: bookmaker, isCombo, isLive, totalOdds, stake, legs. Ensure ALL numeric values (like totalOdds, stake, and odds) are formatted as strings, but keep isCombo and isLive as booleans. Do not include markdown blocks like ```json.";

                var payload = new
                {
                    contents = new[]
                    {
                        new
                        {
                            role = "user",
                            parts = new object[]
                            {
                                new { text = prompt },
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
                        responseMimeType = "application/json",
                        thinkingConfig = new { thinkingBudget = 1024 }
                    }
                };

                var jsonPayload = JsonSerializer.Serialize(payload);
                
                // Hardcoded to the latest available Vertex AI enterprise model
                var resolvedModel = "gemini-3.7-flash";

                var apiUrl = $"https://aiplatform.googleapis.com/v1/projects/castle-gemini/locations/global/publishers/google/models/{resolvedModel}:generateContent";
                
                string betLabel = betId.HasValue ? $"[Bet #{betId.Value}] " : "";
                Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] {betLabel}AI Auto-Read calling Gemini (Model: {resolvedModel})...");

                var response = await SendWithRetryAsync(apiUrl, jsonPayload, betLabel, token);
                string responseString = await response.Content.ReadAsStringAsync();
                
                if (response.IsSuccessStatusCode)
                {
                    LogAiUsage(responseString, betLabel);
                }

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] {betLabel}AI ERROR: {response.StatusCode} - {responseString}");
                    return (null, $"Gemini API Error: {response?.StatusCode}\nResolved Model: {resolvedModel}\nDetails: {responseString}");
                }

                // 4. Parse the response
                using var doc = JsonDocument.Parse(responseString);
                var candidates = doc.RootElement.GetProperty("candidates");
                if (candidates.GetArrayLength() > 0)
                {
                    var textResponse = candidates[0]
                        .GetProperty("content")
                        .GetProperty("parts")[0]
                        .GetProperty("text")
                        .GetString()?.Trim() ?? "";

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
                string betLabel = betId.HasValue ? $"[Bet #{betId.Value}] " : "";
                Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] {betLabel}EXCEPTION in ExtractBetSlipDataAsync: {ex.ToString()}");
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

                var prompt = $"You are a sports betting expert. Here is the JSON data of a bet slip that was placed at {betPlacedAt:yyyy-MM-dd HH:mm}.\n" +
                             $"{extractedBetDataJson}\n\n" +
                             $"Please determine the final result for each match (leg) listed in the bet based strictly on the provided FotMob data.\n" +
                             $"CRITICAL DATE CHECK: The bet was uploaded to our system on {betPlacedAt:yyyy-MM-dd}. The FotMob JSON attached (if any) represents the correct match found within a few days of this upload date. You MUST use the fixture provided in the FotMob JSON, even if its start time is slightly before the upload date (e.g. if the user uploaded the slip a day late or is testing old bets). Do not reject the provided FotMob fixture!\n" +
                             $"CRITICAL FOR FOTMOB VERIFICATION: In the `fullAnalysis` field, the VERY FIRST line MUST explicitly state whether FotMob data was successfully provided and what it contained. E.g., 'FotMob Status: Match found, but no scores available yet' or 'FotMob Status: Scores found (1-0)'. If FotMob returned an error like 'No scores found' or 'NOT_FOUND', you MUST explicitly state that. If FotMob lacks data (e.g., corners are missing or no scores found), do NOT guess or hallucinate stats. You must strictly use the data provided.\n" +
                             $"CRITICAL FOR STATS AND SCORES: You MUST differentiate between Half-Time (HT) and Full-Time (FT) results! If the market specifies '1st Half' or 'Half Time', you must check the half-time stats. Otherwise, you MUST use the FINAL FULL-TIME (FT) score and stats! Double check that the stats you are pulling are for the FULL match and not just the first half. A match is definitively finished ONLY if `header.status.reason.short` is 'FT', 'AET', or 'PEN', or if the match clearly reached full time and is not ongoing. IMPORTANT: FotMob's `general.finished` and `header.status.finished` flags are sometimes incorrectly true before post-match verification while the match is still live! You MUST ignore `finished: true` if `header.status.ongoing` is true, if `liveTime` is present, or if `reason.short` indicates a live minute (e.g. '83\\''). Do not grade the bet as finished prematurely!\n" +
                             $"CRITICAL FOR EXTRA TIME: Unless the market explicitly says 'To Qualify', 'To Lift Trophy', or 'Including Extra Time', ALL bets (goals, cards, corners, match result, player props) apply ONLY to Regular Time (90 minutes + injury time). Events that occur in Extra Time (e.g., the 111th minute of a 120-minute match) DO NOT COUNT! For example, if a player receives a yellow card in Extra Time, a standard 'Player Booked' bet is LOST.\n" +
                             $"CRITICAL FOR SCREENSHOTS: I will attach the RAW JSON statistics fetched directly from the FotMob API for the matches in this bet slip if available. You MUST carefully parse this JSON to find the exact scores for the specific teams requested in the bet legs! This JSON data is your absolute primary source of truth.\n" +
                             $"CRITICAL FOR FALLBACK / GOOGLE SEARCH: If FotMob lacks data AND the match has started, you MUST use your Google Search tool (checking ESPN, Flashscore, etc.). If the match has NOT STARTED, do NOT use Google Search for stats! DO NOT guess stats. When searching, you MUST strictly follow this HARD GUARDRAIL rule:\n" +
                             $"HARD GUARDRAIL: You MUST explicitly verify the exact DATE of the match you find on Google. The match date you pull results from MUST be within 5 days of {betPlacedAt:yyyy-MM-dd} (or the explicitly stated start time if one exists). If the search result does not clearly state the exact date of the match, or if it is an old historical match outside of this 5-day window, you MUST completely reject the result and mark the leg outcome as 'UNKNOWN'. Do NOT mark a bet as Lost or Won using an unverified date or an old historical match score!\n" +
                             $"When searching, you must ALSO strictly verify these two things:\n" +
                             (matchStartTime.HasValue 
                                 ? $"1. THE EXACT TIMEFRAME: We have ALREADY determined the exact start time for this match from our API: {matchStartTime.Value:yyyy-MM-dd HH:mm} UTC. You MUST find the match that corresponds to this EXACT UTC date and time!\n"
                                 : $"1. THE EXACT TIMEFRAME: The exact match time is unknown, but this bet slip was uploaded at {betPlacedAt:yyyy-MM-dd HH:mm} UTC. You MUST find the VERY FIRST match played chronologically ON OR AFTER this upload time.\n") +
                             $"2. THE TEAMS/PLAYERS ORDER: You must strictly verify the exact order of the players/teams (Home vs Away). A match between 'Player A vs Player B' is fundamentally different from 'Player B vs Player A'. If they played multiple times recently, the Home/Away order is your source of truth to pick the right match!\n" +
                             $"CRITICAL FOR SOURCES: For every match, explicitly state 'Verified via provided FotMob JSON' or 'Verified via Google Search' directly in the 'stats' field for each leg. If you use Google Search, you MUST include exactly ONE URL to the specific source page you used to find the result. DO NOT just link to the homepage (e.g. https://www.sofascore.com)! You MUST link to the EXACT match or boxscore page (e.g. https://www.sofascore.com/tennis/match/zverev-paul/xyz) where you read the specific statistics.\n" +
                             $"CRITICAL FOR PLAYER PROPS (STARTER RULE): Unless explicitly stated otherwise (e.g., 'To Score As Substitute') AND unless the overall bet slip is marked as `isLive: true`, ALL player proposition bets (e.g., Goalscorer, Player to be Carded, Shots, Assists, Tackles, Passes) apply ONLY if the specified player is in the STARTING XI for their team. If the bet is NOT a live bet and the player does not start, the outcome MUST be marked as 'Void'. HOWEVER, if `isLive` is true, this starter rule DOES NOT APPLY, and you must grade the player's performance normally even if they were substituted on later. You MUST check the `isLive` property of the provided bet slip JSON before voiding a player!\n" +
                             $"CRITICAL FOR POWER SUB: If the selection contains '(Power Sub)' (which also covers 'Super Sub' and 'Sub on Play On'), it means the bet transfers to the substitute! If the named player is substituted off, the stats of the player who comes on for them MUST be added to their total! To do this, look at the FotMob 'events' array for a 'Substitution' event where the 'swap' array contains the named player. The other player in that 'swap' array is the substitute who came on. You MUST find both players in the 'playerStats' dictionary and mathematically add their stats together to determine the final outcome.\n" +
                             $"CRITICAL FOR ASIAN HANDICAPS: If a market includes a score in parentheses like '(0-1)', it means this was a live bet placed at that score. For live Asian Handicaps in soccer/football, the handicap applies ONLY to the remainder of the match! You must subtract this starting score from the final score before applying the handicap to determine if the bet won or lost.\n" +
                             $"CRITICAL FOR SPLIT ASIAN LINES (Half-Win / Half-Loss): If a bet features a split Asian line (e.g., '-0.5, -1.0', 'Over 2.0, 2.5', '2.25', '2.75') AND the final result causes one half of the bet to Win while the other half Voids (a Half-Win), OR one half to Lose while the other half Voids (a Half-Loss), YOU MUST mark the leg outcome as 'UNKNOWN'. Do NOT mark it as Won, Lost, or Void! You may only mark a split Asian line as 'Won' if BOTH halves win completely, or 'Lost' if BOTH halves lose completely. If the result is split/mixed, you must use 'UNKNOWN'.\n" +
                             $"CRITICAL FOR COMBO BETS: Evaluate each leg COMPLETELY INDEPENDENTLY! Even if multiple legs are for the same match, you MUST write a unique, specific 'stats' reasoning for EACH leg based on its specific Market and Selection. Do NOT copy and paste the same stats reasoning across multiple legs. For example, if Leg 1 is a Goalscorer and Leg 2 is a Match Result, Leg 2's stats MUST discuss the match score, NOT the goalscorer.\n" +
                             $"CRITICAL FOR SCHEDULING: If the match has NOT STARTED (e.g. `general.started` is false AND `header.status.started` is false), you MUST NOT grade ANY legs as Won or Lost; all legs must be 'Pending'. You must also determine its exact kickoff time in UTC. If the kickoff time is ALREADY clearly stated in the bet slip data (e.g. 'Starts: 28.Jul 11:00'), you MUST parse it directly and DO NOT use Google Search. Only use Google Search if the start time is missing. Return it in ISO 8601 format in the `matchStartTimeIso` field (e.g. \"2026-07-25T19:00:00Z\").\n" +
                             $"CRITICAL FOR OUTCOMES: For the 'outcome' field in each leg, you MUST strictly use exactly one of these words: 'Won', 'Lost', 'Void', 'Pending', or 'Unknown'. DO NOT use any emojis! DO NOT add extra text!\n" +
                             $"Return a strictly formatted JSON object with the following schema:\n" +
                             $"{{ \"matchStartTimeIso\": \"2026-07-25T19:00:00Z\", \"fullAnalysis\": \"Your detailed reasoning formatted with \n line breaks...\", \"legs\": [ {{ \"match\": \"Team A vs Team B\", \"outcome\": \"Won\", \"stats\": \"e.g. 12 corners, or Match starts in 2 hours.\" }} ] }}\n" +
                             $"Return ONLY valid JSON. Do not include markdown code blocks.";

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var extractionResult = JsonSerializer.Deserialize<AiVisionExtractionResult>(extractedBetDataJson, options);
                
                var partsList = new List<object>
                {
                    new { text = prompt }
                };

                if (extractionResult?.Legs != null)
                {
                    var groupedLegs = extractionResult.Legs.GroupBy(l => l.Match);
                    foreach (var group in groupedLegs)
                    {
                        string matchName = group.Key;
                        
                        var rawJson = await _fotMob.GetMatchStatsJsonAsync(matchName, betPlacedAt, betId);
                        if (!string.IsNullOrEmpty(rawJson))
                        {
                            partsList.Add(new
                            {
                                text = $"=== FOTMOB RAW JSON FOR MATCH {matchName} ===\n{rawJson}\n========================="
                            });
                        }
                        
                        // Add a small delay to avoid rate limits on combo bets
                        await Task.Delay(1000);
                    }
                }

                var payload = new
                {
                    contents = new[]
                    {
                        new { role = "user", parts = partsList.ToArray() }
                    },
                    tools = new[]
                    {
                        new { googleSearch = new { } }
                    },
                    generationConfig = new 
                    {
                        temperature = 0.1,
                        thinkingConfig = new 
                        {
                            thinkingBudget = 1024
                        }
                    }
                };

                var jsonPayload = JsonSerializer.Serialize(payload);
                
                // Hardcoded to the latest available Vertex AI enterprise model
                var resolvedModel = "gemini-3.7-flash";

                var url = $"https://aiplatform.googleapis.com/v1/projects/castle-gemini/locations/global/publishers/google/models/{resolvedModel}:generateContent";
                
                betLabel = betId.HasValue ? $"[Bet #{betId.Value}] " : "";
                Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] {betLabel}Check Outcome calling Gemini (Model: {resolvedModel})...");
                
                var response = await SendWithRetryAsync(url, jsonPayload, betLabel, token);
                if (!response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    return $"Error checking outcome: {response.StatusCode} - {responseContent}";
                }

                var json = await response.Content.ReadAsStringAsync();
                
                LogAiUsage(json, betLabel);

                using var doc = JsonDocument.Parse(json);
                var text = doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text").GetString();

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
                            
                            if (outcomes.Any(o => o == "LOST")) resultObj.OverallStatus = "LOST";
                            else if (outcomes.Any(o => o == "UNKNOWN")) resultObj.OverallStatus = "UNKNOWN";
                            else if (outcomes.Any(o => o == "PENDING")) resultObj.OverallStatus = "MATCH IN PROGRESS";
                            else if (outcomes.All(o => o == "WON")) resultObj.OverallStatus = "WON";
                            else if (outcomes.All(o => o == "VOID")) resultObj.OverallStatus = "VOID";
                            else resultObj.OverallStatus = "UNKNOWN"; // Mix of Won and Void
                            
                            finalJson = JsonSerializer.Serialize(resultObj, new JsonSerializerOptions { WriteIndented = false });
                        }
                        
                        string localBetLabel = betId.HasValue ? $"[Bet #{betId.Value}]" : "[Test/Manual]";
                        string status = resultObj?.OverallStatus ?? "UNKNOWN";
                        bool hasStartTime = !string.IsNullOrEmpty(resultObj?.MatchStartTimeIso);
                        
                        if (status == "MATCH IN PROGRESS" && hasStartTime)
                        {
                            Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] {localBetLabel} AI: Found start time -> {resultObj!.MatchStartTimeIso}");
                        }
                        else
                        {
                            Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] {localBetLabel} AI: Checked outcome -> Status: {status}");
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
                var resolvedModel = "gemini-3.7-flash";

                var url = $"https://aiplatform.googleapis.com/v1/projects/castle-gemini/locations/global/publishers/google/models/{resolvedModel}:generateContent";
                string betLabel = "[Match Start Time] ";
                Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] {betLabel}calling Gemini (Model: {resolvedModel})...");
                
                var response = await SendWithRetryAsync(url, jsonPayload, betLabel, token);
                
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync();
                
                LogAiUsage(json, betLabel);

                using var doc = JsonDocument.Parse(json);
                var text = doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();

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

                    Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] {betLabel}📊 AI Usage -> Total: {total} | Input: {prompt} | Output: {output}{thinkMsg}{warning}");
                }
            }
            catch { }
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
                            Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] {logLabel}Gemini API returned {response.StatusCode}. Retrying in {2 * (i + 1)}s... (Attempt {i+1}/{maxRetries-1})");
                            await Task.Delay(2000 * (i + 1));
                            continue;
                        }
                    }
                    return response;
                }
                catch (TaskCanceledException)
                {
                    if (i < maxRetries - 1)
                    {
                        Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] {logLabel}Gemini API TaskCanceled (Timeout). Retrying in {2 * (i + 1)}s... (Attempt {i+1}/{maxRetries-1})");
                        await Task.Delay(2000 * (i + 1));
                        continue;
                    }
                    throw;
                }
            }
            throw new Exception("Max retries exceeded.");
        }

    }
}
