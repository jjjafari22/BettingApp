using System;
using System.Linq;
using System.Threading.Tasks;

namespace BettingApp.Services
{
    public class FotMobScraperService
    {
        private readonly HttpClient _httpClient;

        public FotMobScraperService(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        }

        public async Task<string?> GetMatchStatsJsonAsync(string matchName, DateTime? betPlacedAt = null)
        {
            try
            {
                // 1. Clean the match name and extract Home Team for robust searching
                string[] split = matchName.Split(new[] { " vs ", " - ", " v " }, StringSplitOptions.None);
                string homeTeam = split[0].Trim();
                string awayTeam = split.Length > 1 ? split[1].Trim() : "";
                
                string matchQuery = Uri.EscapeDataString(homeTeam); // Search only home team to guarantee results
                
                // 1. Search FotMob API
                string searchUrl = $"https://apigw.fotmob.com/searchapi/suggest?term={matchQuery}";
                string searchJson = await _httpClient.GetStringAsync(searchUrl);
                
                string? eventId = null;

                using (var searchDoc = System.Text.Json.JsonDocument.Parse(searchJson))
                {
                    searchDoc.RootElement.TryGetProperty("matchSuggest", out var matchSuggests);

                    if (matchSuggests.ValueKind != System.Text.Json.JsonValueKind.Undefined && matchSuggests.GetArrayLength() > 0)
                    {
                        eventId = ExtractEventId(searchDoc, homeTeam, awayTeam, betPlacedAt);
                    }

                    // Fallback: If full home team fails or returned only invalid matches (like women's teams), try searching the most significant word (longest word).
                    if (string.IsNullOrEmpty(eventId))
                    {
                        string longestWord = homeTeam.Split(' ').OrderByDescending(w => w.Length).FirstOrDefault() ?? homeTeam.Split(' ')[0];
                        if (longestWord != homeTeam && longestWord.Length >= 3)
                        {
                            string fallbackQuery = Uri.EscapeDataString(longestWord);
                            searchUrl = $"https://apigw.fotmob.com/searchapi/suggest?term={fallbackQuery}";
                            string fallbackJson = await _httpClient.GetStringAsync(searchUrl);
                            
                            using var fallbackDoc = System.Text.Json.JsonDocument.Parse(fallbackJson);
                            eventId = ExtractEventId(fallbackDoc, homeTeam, awayTeam, betPlacedAt);
                        }
                    }

                    // Fallback 2: Sometimes the home team has prefixes like "FK " and FotMob search completely fails, but the away team works perfectly!
                    if (string.IsNullOrEmpty(eventId) && !string.IsNullOrEmpty(awayTeam))
                    {
                        string cleanAwayTeam = awayTeam;
                        var dateMatch = System.Text.RegularExpressions.Regex.Match(awayTeam, @"\((?:Starts:\s*)?([^)]+)\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (dateMatch.Success)
                        {
                            cleanAwayTeam = awayTeam.Substring(0, dateMatch.Index).Trim();
                        }
                        
                        string longestAway = cleanAwayTeam.Split(' ').OrderByDescending(w => w.Length).FirstOrDefault() ?? cleanAwayTeam.Split(' ')[0];
                        if (longestAway.Length >= 3)
                        {
                            string fallbackQuery = Uri.EscapeDataString(longestAway);
                            searchUrl = $"https://apigw.fotmob.com/searchapi/suggest?term={fallbackQuery}";
                            string fallbackJson = await _httpClient.GetStringAsync(searchUrl);
                            
                            using var fallbackDoc = System.Text.Json.JsonDocument.Parse(fallbackJson);
                            eventId = ExtractEventId(fallbackDoc, homeTeam, awayTeam, betPlacedAt);
                        }
                    }
                }

                if (string.IsNullOrEmpty(eventId))
                {
                    Console.WriteLine($"Could not find match against {awayTeam} in FotMob search results for {homeTeam}");
                    return null;
                }

                Console.WriteLine($"Found FotMob Match ID {eventId} for {matchName}");

                // 2. Fetch Match HTML
                string matchHtml = await _httpClient.GetStringAsync($"https://www.fotmob.com/match/{eventId}");
                
                // 3. Extract SSR JSON
                var match = System.Text.RegularExpressions.Regex.Match(matchHtml, @"<script id=""__NEXT_DATA__"" type=""application/json"">(.*?)</script>");
                if (match.Success)
                {
                    try 
                    {
                        using var ssrDoc = System.Text.Json.JsonDocument.Parse(match.Groups[1].Value);
                        var root = ssrDoc.RootElement;
                        if (root.TryGetProperty("props", out var props) &&
                            props.TryGetProperty("pageProps", out var pageProps) &&
                            pageProps.TryGetProperty("content", out var content))
                        {
                            var trimmedData = new System.Collections.Generic.Dictionary<string, object>();

                            if (content.TryGetProperty("matchFacts", out var matchFacts))
                            {
                                var trimmedFacts = new System.Collections.Generic.Dictionary<string, object>();
                                if (matchFacts.TryGetProperty("infoBox", out var infoBox)) trimmedFacts["infoBox"] = infoBox;
                                if (matchFacts.TryGetProperty("events", out var events)) trimmedFacts["events"] = events;
                                trimmedData["matchFacts"] = trimmedFacts;
                            }

                            if (content.TryGetProperty("stats", out var stats)) trimmedData["stats"] = stats;
                            if (content.TryGetProperty("playerStats", out var playerStats)) trimmedData["playerStats"] = playerStats;

                            // Strip down the JSON to only the relevant stats to save massive amounts of tokens and prevent 503s!
                            return System.Text.Json.JsonSerializer.Serialize(trimmedData);
                        }
                    } 
                    catch { }
                    
                    return match.Groups[1].Value; // fallback to full payload if parsing fails
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error scraping JSON for {matchName}: {ex.Message}");
                return null;
            }
        }

        private string? ExtractEventId(System.Text.Json.JsonDocument doc, string homeTeam, string awayTeam, DateTime? betPlacedAt)
        {
            if (!doc.RootElement.TryGetProperty("matchSuggest", out var matchSuggests) || matchSuggests.GetArrayLength() == 0)
                return null;

            if (!matchSuggests[0].TryGetProperty("options", out var options) || options.GetArrayLength() == 0)
                return null;

            var awayTeamTokens = string.IsNullOrEmpty(awayTeam) ? new string[0] : 
                awayTeam.Split(new[] { ' ', '-', '/' }, StringSplitOptions.RemoveEmptyEntries)
                        .Where(w => w.Length >= 3)
                        .ToArray();

            if (awayTeamTokens.Length == 0 && !string.IsNullOrEmpty(awayTeam))
            {
                awayTeamTokens = new[] { awayTeam };
            }

            string? bestId = null;
            int bestScore = -1;
            TimeSpan smallestTimeDiff = TimeSpan.MaxValue;

            foreach (var option in options.EnumerateArray())
            {
                var payload = option.GetProperty("payload");
                string optionHomeName = payload.TryGetProperty("homeName", out var h) ? h.GetString() ?? "" : "";
                string optionAwayName = payload.TryGetProperty("awayName", out var a) ? a.GetString() ?? "" : "";
                string text = option.GetProperty("text").GetString() ?? "";
                string normalizedText = NormalizeText(text);

                bool queryIsWomen = homeTeam.Contains("women", StringComparison.OrdinalIgnoreCase) || homeTeam.Contains("(w)", StringComparison.OrdinalIgnoreCase) || homeTeam.Contains("femenil", StringComparison.OrdinalIgnoreCase);
                bool optionIsWomen = text.Contains("women", StringComparison.OrdinalIgnoreCase) || text.Contains("(w)", StringComparison.OrdinalIgnoreCase) || text.Contains("femenil", StringComparison.OrdinalIgnoreCase);

                var homeTeamTokens = homeTeam.Split(new[] { ' ', '-', '/' }, StringSplitOptions.RemoveEmptyEntries)
                                             .Where(w => w.Length >= 3).Select(NormalizeText).ToArray();
                if (homeTeamTokens.Length == 0 && !string.IsNullOrEmpty(homeTeam)) homeTeamTokens = new[] { NormalizeText(homeTeam) };

                bool homeMatch = false;
                foreach (var token in homeTeamTokens)
                {
                    if (normalizedText.Contains(token, StringComparison.OrdinalIgnoreCase))
                    {
                        homeMatch = true;
                        break;
                    }
                }

                bool awayTeamMatch = string.IsNullOrEmpty(awayTeam);
                if (!awayTeamMatch)
                {
                    var normalizedAwayTokens = awayTeamTokens.Select(NormalizeText).ToArray();
                    foreach (var token in normalizedAwayTokens)
                    {
                        if (normalizedText.Contains(token, StringComparison.OrdinalIgnoreCase))
                        {
                            awayTeamMatch = true;
                            break;
                        }
                    }
                }

                if (!homeMatch || !awayTeamMatch)
                {
                    continue; // Must contain at least one token from BOTH home and away teams!
                }

                int score = 0;
                
                if (queryIsWomen != optionIsWomen) 
                {
                    score -= 50; // Heavy penalty for gender mismatch!
                }
                
                // Score 10 points if the Home/Away order strictly matches our expected order!
                if (optionHomeName.Contains(homeTeam, StringComparison.OrdinalIgnoreCase) || homeTeam.Contains(optionHomeName, StringComparison.OrdinalIgnoreCase))
                {
                    bool orderMatch = false;
                    foreach (var token in awayTeamTokens)
                    {
                        if (optionAwayName.Contains(token, StringComparison.OrdinalIgnoreCase))
                        {
                            orderMatch = true;
                            break;
                        }
                    }
                    if (orderMatch)
                    {
                        score += 10;
                    }
                }

                // Parse match date to prioritize the closest match (e.g. Leg 1 vs Leg 2)
                if (payload.TryGetProperty("matchDate", out var matchDateElement))
                {
                    if (DateTime.TryParse(matchDateElement.GetString(), out DateTime matchDate))
                    {
                        DateTime targetDate = betPlacedAt ?? DateTime.UtcNow;
                        
                        // If the awayTeam string contains "(Starts: 24.Jul 20:00)", extract the date!
                        var dateMatch = System.Text.RegularExpressions.Regex.Match(awayTeam, @"\((?:Starts:\s*)?([^)]+)\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (dateMatch.Success && DateTime.TryParse(dateMatch.Groups[1].Value, out DateTime parsedTarget))
                        {
                            // if it parsed successfully without a year, it might be in the past or future.
                            // We can just use it if it's within a few months of betPlacedAt
                            targetDate = parsedTarget;
                            if (betPlacedAt.HasValue && Math.Abs((targetDate - betPlacedAt.Value).TotalDays) > 180)
                            {
                                // Fix year wrap-around
                                targetDate = targetDate.AddYears(targetDate < betPlacedAt.Value ? 1 : -1);
                            }
                        }

                        // Calculate absolute time difference between target date and match date
                        TimeSpan diff = (matchDate - targetDate).Duration();
                        
                        // Strict threshold: If the match is more than 14 days apart from our target date, reject it!
                        if (diff.TotalDays > 14)
                        {
                            continue;
                        }
                        
                        if (score > bestScore || (score == bestScore && diff < smallestTimeDiff))
                        {
                            bestScore = score;
                            smallestTimeDiff = diff;
                            bestId = payload.GetProperty("id").GetString();
                        }
                        continue;
                    }
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestId = payload.GetProperty("id").GetString();
                }
            }

            return bestId;
        }
        private string NormalizeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return text.ToLowerInvariant()
                       .Replace("ø", "o")
                       .Replace("å", "a")
                       .Replace("æ", "ae")
                       .Replace("ä", "a")
                       .Replace("ö", "o")
                       .Replace("ü", "u")
                       .Replace("é", "e")
                       .Replace("è", "e")
                       .Replace("á", "a")
                       .Replace("í", "i")
                       .Replace("ó", "o")
                       .Replace("ú", "u")
                       .Replace("ñ", "n");
        }
    }
}
