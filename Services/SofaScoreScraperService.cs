using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace BettingApp.Services
{
    public class SofaScoreScraperService
    {
        private readonly HttpClient _httpClient;

        public SofaScoreScraperService(HttpClient httpClient)
        {
            _httpClient = httpClient;
            // SofaScore requires standard browser headers
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
            _httpClient.DefaultRequestHeaders.Add("Origin", "https://www.sofascore.com");
            _httpClient.DefaultRequestHeaders.Add("Referer", "https://www.sofascore.com/");
        }

        public async Task<string?> GetMatchStatsJsonAsync(string matchName, DateTime? betPlacedAt = null, int? betId = null)
        {
            if (string.IsNullOrEmpty(matchName)) return null;
            string betLabel = betId.HasValue ? $"[Bet #{betId.Value}]" : "[Test/Manual]";

            try
            {
                var teams = matchName.Split(new[] { " vs ", "-", " v " }, StringSplitOptions.RemoveEmptyEntries);
                if (teams.Length < 2)
                {
                    // Not a standard format, silently fail and let LLM use search
                    return null;
                }

                string player1 = teams[0].Trim();
                string player2 = teams[1].Trim();

                // 1. Search for Player 1 to get their entity ID
                string searchUrl = $"https://api.sofascore.com/api/v1/search/all?q={Uri.EscapeDataString(player1)}";
                var searchResponse = await _httpClient.GetAsync(searchUrl);
                if (!searchResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] {betLabel} SofaScore: Search API failed for '{player1}'");
                    return null;
                }

                string searchJson = await searchResponse.Content.ReadAsStringAsync();
                using var searchDoc = JsonDocument.Parse(searchJson);
                
                if (!searchDoc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
                {
                    Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] {betLabel} SofaScore: No search results for '{player1}'");
                    return null;
                }

                // Find the first tennis player match
                string? playerId = null;
                foreach (var result in results.EnumerateArray())
                {
                    if (result.TryGetProperty("entity", out var entity))
                    {
                        if (entity.TryGetProperty("sport", out var sport) && sport.TryGetProperty("name", out var sportName))
                        {
                            if (sportName.GetString()?.Equals("Tennis", StringComparison.OrdinalIgnoreCase) == true)
                            {
                                playerId = entity.GetProperty("id").GetInt32().ToString();
                                break;
                            }
                        }
                    }
                }

                if (string.IsNullOrEmpty(playerId))
                {
                    Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] {betLabel} SofaScore: Could not find Tennis player ID for '{player1}'");
                    return null;
                }

                // 2. Fetch recent events for this player
                string eventsUrl = $"https://api.sofascore.com/api/v1/team/{playerId}/events/last/0";
                var eventsResponse = await _httpClient.GetAsync(eventsUrl);
                if (!eventsResponse.IsSuccessStatusCode) return null;

                string eventsJson = await eventsResponse.Content.ReadAsStringAsync();
                using var eventsDoc = JsonDocument.Parse(eventsJson);

                if (eventsDoc.RootElement.TryGetProperty("events", out var eventsArray))
                {
                    // 3. Find the event against Player 2
                    foreach (var ev in eventsArray.EnumerateArray())
                    {
                        string homeTeam = ev.GetProperty("homeTeam").GetProperty("name").GetString() ?? "";
                        string awayTeam = ev.GetProperty("awayTeam").GetProperty("name").GetString() ?? "";

                        if (FuzzyMatch(player2, homeTeam) || FuzzyMatch(player2, awayTeam))
                        {
                            Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] {betLabel} SofaScore: Found Match ID {ev.GetProperty("id").GetInt32()} for {matchName}");
                            return ev.GetRawText();
                        }
                    }
                }
                
                // 4. Try future/next events just in case it hasn't started yet
                string nextEventsUrl = $"https://api.sofascore.com/api/v1/team/{playerId}/events/next/0";
                var nextResponse = await _httpClient.GetAsync(nextEventsUrl);
                if (nextResponse.IsSuccessStatusCode)
                {
                    string nextJson = await nextResponse.Content.ReadAsStringAsync();
                    using var nextDoc = JsonDocument.Parse(nextJson);
                    if (nextDoc.RootElement.TryGetProperty("events", out var nextEventsArray))
                    {
                        foreach (var ev in nextEventsArray.EnumerateArray())
                        {
                            string homeTeam = ev.GetProperty("homeTeam").GetProperty("name").GetString() ?? "";
                            string awayTeam = ev.GetProperty("awayTeam").GetProperty("name").GetString() ?? "";

                            if (FuzzyMatch(player2, homeTeam) || FuzzyMatch(player2, awayTeam))
                            {
                                Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] {betLabel} SofaScore: Found Future Match ID {ev.GetProperty("id").GetInt32()} for {matchName}");
                                return ev.GetRawText();
                            }
                        }
                    }
                }
                
                Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] {betLabel} SofaScore: Could not find match '{matchName}' in player history");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] {betLabel} Error scraping SofaScore JSON for {matchName}: {ex.Message}");
            }

            return null;
        }

        private bool FuzzyMatch(string query, string target)
        {
            if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(target)) return false;
            
            query = query.ToLowerInvariant();
            target = target.ToLowerInvariant();
            
            // Often just matching the last name is enough (e.g. "Alcaraz" vs "Carlos Alcaraz")
            var queryTokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in queryTokens)
            {
                if (token.Length > 3 && target.Contains(token)) return true;
            }
            
            return target.Contains(query) || query.Contains(target);
        }
    }
}
