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
                             "3) totalOdds (the final combined odds of the slip, if visible). " +
                             "4) stake (the amount bet, e.g. '100', '1000'). CRITICAL: Often the user will manually draw or write their stake over the image with a digital pen. You MUST look for manual handwritten digits over the image indicating the stake and prioritize that over printed text! " +
                             "5) legs: an array of objects representing each individual bet, containing: " +
                             "   - match (e.g. 'Arsenal vs Man City'). CRITICAL: You MUST translate the team names into their standard, globally recognized English names (e.g. you MUST output 'FC Copenhagen' instead of 'FC København', and 'Bayern Munich' instead of 'Bayern München'). This is required for our Odds API to find the match. " +
                             "   - market (e.g. 'Asian Handicap (0-1)', 'Total Cards'). CRITICAL: If the market is in another language (e.g. Danish 'Kort i alt'), translate it to English. CRITICAL: If the market includes a specific line, handicap, or point spread (e.g., '(0-1)', '-1.5', '+2.5'), you MUST include that numerical modifier in the market name! Do not leave it out! " +
                             "   - selection (the specific bet chosen, e.g. 'Arsenal' or 'Under 2.5'). CRITICAL: If this is a player prop, you MUST include the exact condition (e.g. 'Marcus Rashford - Will Score'). Do NOT just write the player's name! " +
                             "   - badges (an array of strings). CRITICAL: Look carefully for any special promo labels, text, or icons near the bet (e.g., 'Power Sub', 'Early Payout', 'Super Boost'). If you see any, add them to this array! " +
                             "   - odds (e.g. '1.95'). " +
                             "Return ONLY a raw JSON object with keys: bookmaker, isCombo, totalOdds, stake, legs. Ensure ALL numeric values (like totalOdds, stake, and odds) are formatted as strings, but keep isCombo as a boolean. Do not include markdown blocks like ```json.";

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
                        responseMimeType = "application/json"
                    }
                };

                var jsonPayload = JsonSerializer.Serialize(payload);
                
                // Hardcoded to the latest available Vertex AI enterprise model
                var resolvedModel = "gemini-2.5-flash";

                var apiUrl = $"https://us-central1-aiplatform.googleapis.com/v1/projects/castle-gemini/locations/us-central1/publishers/google/models/{resolvedModel}:generateContent";
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                
                string betLabel = betId.HasValue ? $"[Bet #{betId.Value}] " : "";
                Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] {betLabel}AI Auto-Read calling Gemini (Model: {resolvedModel})...");

                var response = await SendWithRetryAsync(apiUrl, jsonPayload, betLabel);
                string responseString = await response.Content.ReadAsStringAsync();

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

                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var result = JsonSerializer.Deserialize<AiVisionExtractionResult>(textResponse, options);
                    
                    if (result?.Legs != null)
                    {
                        foreach (var leg in result.Legs)
                        {
                            if (leg.Badges != null && leg.Badges.Any(b => b.Contains("Power Sub", StringComparison.OrdinalIgnoreCase) || b.Contains("Substitute", StringComparison.OrdinalIgnoreCase)))
                            {
                                if (!leg.Selection.Contains("(Power Sub)")) leg.Selection += " (Power Sub)";
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
                Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] {betLabel}EXCEPTION in ExtractBetSlipDataAsync: {ex.Message}");
                return (null, $"Exception: {ex.Message}");
            }
        }

        public async Task<string?> ConfirmOutcomeAsync(string extractedBetDataJson, DateTime betPlacedAt, int? betId = null)
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
                             $"CRITICAL FOR STATS AND SCORES: You MUST differentiate between Half-Time (HT) and Full-Time (FT) results! If the market specifies '1st Half' or 'Half Time', you must check the half-time stats. Otherwise, you MUST use the FINAL FULL-TIME (FT) score and stats! Double check that the stats you are pulling are for the FULL match and not just the first half. Pay attention to which team is Home and Away to get the score order correct.\n" +
                             $"CRITICAL FOR EXTRA TIME: Unless the market explicitly says 'To Qualify', 'To Lift Trophy', or 'Including Extra Time', ALL bets (goals, cards, corners, match result, player props) apply ONLY to Regular Time (90 minutes + injury time). Events that occur in Extra Time (e.g., the 111th minute of a 120-minute match) DO NOT COUNT! For example, if a player receives a yellow card in Extra Time, a standard 'Player Booked' bet is LOST.\n" +
                             $"CRITICAL FOR SCREENSHOTS: I will attach the RAW JSON statistics fetched directly from the FotMob API for the matches in this bet slip if available. You MUST carefully parse this JSON to find the exact scores for the specific teams requested in the bet legs! This JSON data is your absolute primary source of truth.\n" +
                             $"CRITICAL FOR FALLBACK / GOOGLE SEARCH: If FotMob lacks data, you MUST use your Google Search tool (checking ESPN, Flashscore, etc.). DO NOT guess stats. When searching, you MUST strictly verify two things:\n" +
                             $"1. THE DATE: Ensure the match occurred on or near the correct date listed in the bet slip. If no explicit StartTime is given, you MUST find the MOST RECENT match played between them. If you find a match from months ago, DO NOT use it!\n" +
                             $"2. THE TEAMS/PLAYERS ORDER: You must strictly verify the exact order of the players/teams (Home vs Away). A match between 'Player A vs Player B' is fundamentally different from 'Player B vs Player A'. If they played multiple times recently, the Home/Away order is your source of truth to pick the right match!\n" +
                             $"CRITICAL FOR SOURCES: For every match, explicitly state 'Verified via provided FotMob JSON' or 'Verified via Google Search' directly in the 'stats' field for each leg.\n" +
                             $"CRITICAL FOR POWER SUB: If the selection contains '(Power Sub)', it means the bet transfers to the substitute! If the named player is substituted off, the stats of the player who comes on for them MUST be added to their total! You must find who was substituted on for that player and combine their stats to determine the outcome.\n" +
                             $"Check if the matches are finished, live, or not started. Determine if the overall bet was Won, Lost, or Void based on the results.\n" +
                             $"CRITICAL FOR ASIAN HANDICAPS: If a market includes a score in parentheses like '(0-1)', it means this was a live bet placed at that score. For live Asian Handicaps in soccer/football, the handicap applies ONLY to the remainder of the match! You must subtract this starting score from the final score before applying the handicap to determine if the bet won or lost.\n" +
                             $"CRITICAL FOR SPLIT ASIAN LINES (Half-Win / Half-Loss): If a bet features a split Asian line (e.g., '-0.5, -1.0', 'Over 2.0, 2.5', '2.25', '2.75') AND the final result causes one half of the bet to Win while the other half Voids (a Half-Win), OR one half to Lose while the other half Voids (a Half-Loss), YOU MUST mark the leg outcome as 'UNKNOWN'. Do NOT mark it as Won, Lost, or Void! You may only mark a split Asian line as 'Won' if BOTH halves win completely, or 'Lost' if BOTH halves lose completely. If the result is split/mixed, you must use 'UNKNOWN'.\n" +
                             $"CRITICAL FOR COMBO BETS: Evaluate each leg COMPLETELY INDEPENDENTLY! Even if multiple legs are for the same match, you MUST write a unique, specific 'stats' reasoning for EACH leg based on its specific Market and Selection. Do NOT copy and paste the same stats reasoning across multiple legs. For example, if Leg 1 is a Goalscorer and Leg 2 is a Match Result, Leg 2's stats MUST discuss the match score, NOT the goalscorer.\n" +
                             $"CRITICAL FOR OVERALL STATUS: The 'overallStatus' field MUST be exactly one of the following strings:\n" +
                             $"- 'MATCH FINISHED - WON' (if all legs have finished and won)\n" +
                             $"- 'MATCH FINISHED - LOST' (if any leg has finished and lost, even if other legs are pending)\n" +
                             $"- 'MATCH FINISHED - VOID' (if the bet was voided)\n" +
                             $"- 'MATCH NOT STARTED' (if the match has not started yet, and no leg has definitively lost. Do NOT use 'BET PENDING' or other variations!)\n" +
                             $"- 'MATCH IN PROGRESS' (if the match is currently in progress/live, and no leg has definitively lost yet)\n" +
                             $"- 'UNKNOWN' (if the match is finished but the specific prop result cannot be found yet)\n" +
                             $"CRITICAL FOR LIVE STATUS: You MUST check the 'general' and 'header' sections of the FotMob JSON to see if the match is live. If `general.started` is true but `general.finished` is false, or if `header.status` indicates the match is ongoing, YOU MUST NOT mark the bet as 'LOST' or 'WON'. You must use 'MATCH IN PROGRESS' and mark the legs as 'Pending'. Do not prematurely grade a bet if the match is still being played!\n" +
                             $"CRITICAL FOR SCHEDULING: If the match has NOT STARTED, you must determine its exact kickoff time in UTC. If the kickoff time is ALREADY clearly stated in the bet slip data (e.g. 'Starts: 28.Jul 11:00'), you MUST parse it directly and DO NOT use Google Search. Only use Google Search if the start time is missing. Return it in ISO 8601 format in the `matchStartTimeIso` field (e.g. \"2026-07-25T19:00:00Z\").\n" +
                             $"Return a strictly formatted JSON object with the following schema:\n" +
                             $"{{ \"overallStatus\": \"MATCH NOT STARTED\", \"matchStartTimeIso\": \"2026-07-25T19:00:00Z\", \"fullAnalysis\": \"Your detailed reasoning formatted with \n line breaks...\", \"legs\": [ {{ \"match\": \"Team A vs Team B\", \"outcome\": \"Won / Lost / Void / Pending\", \"stats\": \"e.g. 12 corners, or Match starts in 2 hours.\" }} ] }}\n" +
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
                    }
                };

                var jsonPayload = JsonSerializer.Serialize(payload);
                
                // Hardcoded to the latest available Vertex AI enterprise model
                var resolvedModel = "gemini-2.5-flash";

                var url = $"https://us-central1-aiplatform.googleapis.com/v1/projects/castle-gemini/locations/us-central1/publishers/google/models/{resolvedModel}:generateContent";
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                
                betLabel = betId.HasValue ? $"[Bet #{betId.Value}] " : "";
                Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] {betLabel}Check Outcome calling Gemini (Model: {resolvedModel})...");
                
                var response = await SendWithRetryAsync(url, jsonPayload, betLabel);
                if (!response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    return $"Error checking outcome: {response.StatusCode} - {responseContent}";
                }

                var json = await response.Content.ReadAsStringAsync();
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
                        using var resultDoc = JsonDocument.Parse(finalJson);
                        string localBetLabel = betId.HasValue ? $"[Bet #{betId.Value}]" : "[Test/Manual]";
                        
                        string status = resultDoc.RootElement.TryGetProperty("overallStatus", out var os) ? os.GetString() ?? "UNKNOWN" : "UNKNOWN";
                        bool hasStartTime = resultDoc.RootElement.TryGetProperty("matchStartTimeIso", out var ms) && !string.IsNullOrEmpty(ms.GetString());
                        
                        if (status == "MATCH NOT STARTED" && hasStartTime)
                        {
                            Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] {localBetLabel} AI: Found start time via Google Search -> {ms.GetString()}");
                        }
                        else
                        {
                            Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] {localBetLabel} AI: Checked outcome -> Status: {status}");
                        }
                    }
                    catch { } // ignore parsing errors for logging
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
                    tools = new[] { new { googleSearch = new object() } }
                };

                var jsonPayload = JsonSerializer.Serialize(payload);
                
                // Hardcoded to the latest available Vertex AI enterprise model
                var resolvedModel = "gemini-2.5-flash";

                var url = $"https://us-central1-aiplatform.googleapis.com/v1/projects/castle-gemini/locations/us-central1/publishers/google/models/{resolvedModel}:generateContent";
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                string betLabel = "[Match Start Time] ";
                Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] {betLabel}calling Gemini (Model: {resolvedModel})...");
                
                var response = await SendWithRetryAsync(url, jsonPayload, betLabel);
                
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync();
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
        private async Task<HttpResponseMessage> SendWithRetryAsync(string url, string jsonPayload, string logLabel = "")
        {
            int maxRetries = 3;
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    var requestContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                    var response = await _httpClient.PostAsync(url, requestContent);
                    
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
