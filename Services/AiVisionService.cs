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
                             "   - market (e.g. 'Asian Handicap', 'Total Cards'). CRITICAL: If the market is in another language (e.g. Danish 'Kort i alt', Swedish, Norwegian), you MUST translate it into standard English (e.g. 'Total Cards'). " +
                             "   - selection (the specific bet chosen, e.g. 'Arsenal' or 'Under 2.5'). Translate this to English as well if necessary, " +
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
    }
}
