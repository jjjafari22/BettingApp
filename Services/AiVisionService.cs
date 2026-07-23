using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
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

        [JsonPropertyName("odds")]
        public string Odds { get; set; } = "";
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
        
        [JsonPropertyName("fullAnalysis")]
        public string FullAnalysis { get; set; } = "";
        
        [JsonPropertyName("legs")]
        public List<AiOutcomeLegResult> Legs { get; set; } = new();
    }

    public class AiVisionService
    {
        private readonly HttpClient _httpClient;
        private readonly string? _apiKey;

        public AiVisionService(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;
            _apiKey = config["GeminiApiKey"];
        }

        public async Task<(AiVisionExtractionResult? Result, string? Error)> ExtractBetSlipDataAsync(string imageUrl)
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                return (null, "GeminiApiKey is not configured in user-secrets.");
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
                             "   - match (e.g. 'Arsenal vs Man City'). CRITICAL: You MUST use the official, native spelling of the team names, INCLUDING proper diacritics/special characters, even if the screenshot omits them! (e.g. you MUST output 'Beşiktaş' instead of 'Besiktas', and 'Bodø/Glimt' instead of 'Bodo/Glimt'). This is required for our database to find the match. " +
                             "   - market (e.g. 'Asian Handicap (0-1)', 'Total Cards'). CRITICAL: If the market is in another language (e.g. Danish 'Kort i alt'), translate it to English. CRITICAL: If the market includes a specific line, handicap, or point spread (e.g., '(0-1)', '-1.5', '+2.5'), you MUST include that numerical modifier in the market name! Do not leave it out! " +
                             "   - selection (the specific bet chosen, e.g. 'Arsenal' or 'Under 2.5'). CRITICAL: If this is a player prop (like scoring, cards, assists), you MUST include the exact condition in the selection (e.g. 'Marcus Rashford - Will Score', 'Erling Haaland - Over 1.5 Shots', 'Bukayo Saka - Yes'). Do NOT just write the player's name! Translate to English if necessary. " +
                             "   - odds (e.g. '1.95'). " +
                             "Return ONLY a raw JSON object with keys: bookmaker, isCombo, totalOdds, stake, legs. Do not include markdown blocks like ```json.";

                var payload = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new object[]
                            {
                                new { text = prompt },
                                new
                                {
                                    inline_data = new
                                    {
                                        mime_type = mimeType,
                                        data = base64Image
                                    }
                                }
                            }
                        }
                    }
                };

                var jsonPayload = JsonSerializer.Serialize(payload);
                var requestContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                
                // 3. Auto-resolve the best available Flash model
                var modelsUrl = $"https://generativelanguage.googleapis.com/v1beta/models?key={_apiKey}";
                var modelsResponse = await _httpClient.GetAsync(modelsUrl);
                if (!modelsResponse.IsSuccessStatusCode) return (null, "Failed to fetch model list from Gemini.");
                
                var modelsJson = await modelsResponse.Content.ReadAsStringAsync();
                using var modelsDoc = JsonDocument.Parse(modelsJson);
                var resolvedModel = "gemini-1.5-flash"; // fallback
                
                double maxVersion = 0;
                
                foreach (var m in modelsDoc.RootElement.GetProperty("models").EnumerateArray())
                {
                    var name = m.GetProperty("name").GetString();
                    // We want the standard flash model, not a TTS, text-only, or experimental preview variant.
                    if (name != null && name.Contains("flash") && 
                        !name.Contains("tts") && !name.Contains("text") && !name.Contains("preview") && !name.Contains("vision"))
                    {
                        bool supportsGenerate = false;
                        if (m.TryGetProperty("supportedGenerationMethods", out var methods))
                        {
                            foreach (var method in methods.EnumerateArray())
                            {
                                if (method.GetString() == "generateContent") supportsGenerate = true;
                            }
                        }
                        if (supportsGenerate)
                        {
                            // We prioritize the standard Flash model over Flash-Lite because it has vastly superior
                            // multimodal reasoning, which is necessary for reading complex betting slips, combos, and handwriting.
                            var match = System.Text.RegularExpressions.Regex.Match(name, @"gemini-(\d+\.\d+)-flash$");
                            
                            if (match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double version))
                            {
                                if (version > maxVersion)
                                {
                                    maxVersion = version;
                                    resolvedModel = name.Replace("models/", "");
                                }
                            }
                        }
                    }
                }

                // 4. Call Gemini API with resolved model
                var apiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{resolvedModel}:generateContent?key={_apiKey}";
                var response = await _httpClient.PostAsync(apiUrl, requestContent);
                var responseString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return (null, $"Gemini API Error: {response.StatusCode}\nResolved Model: {resolvedModel}\nDetails: {responseString}");
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
                    return (result, null);
                }

                return (null, "No candidates returned from Gemini.");
            }

            catch (Exception ex)
            {
                return (null, $"Exception: {ex.Message}");
            }
        }

        public async Task<string?> ConfirmOutcomeAsync(string extractedBetDataJson, DateTime betPlacedAt)
        {
            if (string.IsNullOrEmpty(_apiKey)) return "Error: Gemini API key missing.";

            try
            {
                var prompt = $"You are a sports betting expert. Here is the JSON data of a bet slip that was placed at {betPlacedAt:yyyy-MM-dd HH:mm}.\n" +
                             $"{extractedBetDataJson}\n\n" +
                             $"Please search the web to find the final result for each match (leg) listed in the bet.\n" +
                             $"CRITICAL DATE CHECK: The bet was placed on {betPlacedAt:yyyy-MM-dd}. You MUST ensure you are looking at the match that occurred ON OR IMMEDIATELY AFTER this date! Do NOT look at older historic games between these two teams. Verify the date of the match results you find.\n" +
                             $"CRITICAL FOR STATS AND SCORES: You MUST differentiate between Half-Time (HT) and Full-Time (FT) results! If the market specifies '1st Half' or 'Half Time', you must check the half-time stats. Otherwise, you MUST use the FINAL FULL-TIME (FT) score and stats! Double check that the stats you are pulling are for the FULL match and not just the first half. Pay attention to which team is Home and Away to get the score order correct.\n" +
                             $"CRITICAL FOR CORNERS, CARDS, AND PLAYER PROPS: These specific statistics are notoriously difficult to find accurately in standard Google snippets. You MUST explicitly verify these stats on highly reliable sources like UEFA, FIFA, Flashscore, Sofascore, ESPN, or BBC. Do NOT guess or hallucinate stats from unrelated summaries! If you cannot find the EXACT definitive final count for corners or cards, mark the bet as 'UNKNOWN / AWAITING RESULTS' rather than guessing a wrong number.\n" +
                             $"Check if the matches are finished, live, or not started. Determine if the overall bet was Won, Lost, or Void based on the results.\n" +
                             $"CRITICAL FOR ASIAN HANDICAPS: If a market includes a score in parentheses like '(0-1)', it means this was a live bet placed at that score. For live Asian Handicaps in soccer/football, the handicap applies ONLY to the remainder of the match! You must subtract this starting score from the final score before applying the handicap to determine if the bet won or lost.\n" +
                             $"CRITICAL FOR COMBO BETS: Evaluate each leg COMPLETELY INDEPENDENTLY! Even if multiple legs are for the same match, you MUST write a unique, specific 'stats' reasoning for EACH leg based on its specific Market and Selection. Do NOT copy and paste the same stats reasoning across multiple legs. For example, if Leg 1 is a Goalscorer and Leg 2 is a Match Result, Leg 2's stats MUST discuss the match score, NOT the goalscorer.\n" +
                             $"CRITICAL FOR OVERALL STATUS: The 'overallStatus' field MUST be exactly one of the following strings:\n" +
                             $"- 'MATCH FINISHED - WON' (if all legs have finished and won)\n" +
                             $"- 'MATCH FINISHED - LOST' (if any leg has finished and lost, even if other legs are pending)\n" +
                             $"- 'MATCH FINISHED - VOID' (if the bet was voided)\n" +
                             $"- 'MATCH NOT COMPLETED' (if any leg has not started yet, or is currently in progress/live, and no leg has definitively lost yet!)\n" +
                             $"- 'UNKNOWN / AWAITING RESULTS' (if the match is finished but the specific prop result cannot be found yet)\n" +
                             $"Return a strictly formatted JSON object with the following schema:\n" +
                             $"{{ \"overallStatus\": \"MATCH NOT COMPLETED\", \"fullAnalysis\": \"Your detailed reasoning formatted with \n line breaks...\", \"legs\": [ {{ \"match\": \"Team A vs Team B\", \"outcome\": \"Won / Lost / Void / Pending\", \"stats\": \"e.g. 12 corners, or Match starts in 2 hours.\" }} ] }}\n" +
                             $"Return ONLY valid JSON. Do not include markdown code blocks.";

                var payload = new
                {
                    contents = new[]
                    {
                        new { parts = new[] { new { text = prompt } } }
                    },
                    tools = new[]
                    {
                        new { google_search = new object() }
                    }
                };

                var jsonPayload = JsonSerializer.Serialize(payload);
                var requestContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                
                // Auto-resolve the best available Flash model
                var modelsUrl = $"https://generativelanguage.googleapis.com/v1beta/models?key={_apiKey}";
                var modelsResponse = await _httpClient.GetAsync(modelsUrl);
                if (!modelsResponse.IsSuccessStatusCode) return "Failed to fetch model list from Gemini.";
                
                var modelsJson = await modelsResponse.Content.ReadAsStringAsync();
                using var modelsDoc = JsonDocument.Parse(modelsJson);
                var resolvedModel = "gemini-1.5-flash"; // fallback
                
                double maxVersion = 0;
                foreach (var m in modelsDoc.RootElement.GetProperty("models").EnumerateArray())
                {
                    var name = m.GetProperty("name").GetString();
                    if (name != null && name.Contains("flash") && 
                        !name.Contains("tts") && !name.Contains("text") && !name.Contains("preview") && !name.Contains("vision"))
                    {
                        bool supportsGenerate = false;
                        if (m.TryGetProperty("supportedGenerationMethods", out var methods))
                        {
                            foreach (var method in methods.EnumerateArray())
                            {
                                if (method.GetString() == "generateContent") supportsGenerate = true;
                            }
                        }
                        if (supportsGenerate)
                        {
                            var match = System.Text.RegularExpressions.Regex.Match(name, @"gemini-(\d+\.\d+)-flash$");
                            if (match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double version))
                            {
                                if (version > maxVersion)
                                {
                                    maxVersion = version;
                                    resolvedModel = name.Replace("models/", "");
                                }
                            }
                        }
                    }
                }

                var url = $"https://generativelanguage.googleapis.com/v1beta/models/{resolvedModel}:generateContent?key={_apiKey}";
                
                var response = await _httpClient.PostAsync(url, requestContent);
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
                    if (text.StartsWith("```json")) text = text.Substring(7);
                    if (text.StartsWith("```")) text = text.Substring(3);
                    if (text.EndsWith("```")) text = text.Substring(0, text.Length - 3);
                }

                return text?.Trim();
            }
            catch (Exception ex)
            {
                return $"Exception checking outcome: {ex.Message}";
            }
        }
    }
}
