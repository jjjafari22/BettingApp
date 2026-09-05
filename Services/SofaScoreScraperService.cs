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
            // Trying a non-browser User-Agent to avoid Cloudflare TLS fingerprint mismatch penalties
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "BettingApp/1.0 (Contact: admin@example.com)");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        }

        public async Task<string?> GetMatchStatsJsonAsync(string matchName, DateTime? betPlacedAt = null, int? betId = null)
        {
            if (string.IsNullOrEmpty(matchName)) return null;
            string betLabel = betId.HasValue ? $"[Bet #{betId.Value}]" : "[Extraction]";

            try
            {
                var teams = matchName.Split(new[] { " vs ", "-", " v " }, StringSplitOptions.RemoveEmptyEntries);
                
                string player1 = teams[0].Trim();
                string player2 = teams.Length > 1 ? teams[1].Trim() : "";

                // 1. Search for Player 1 to get their entity ID
                string searchUrl = $"https://www.sofascore.com/api/v1/search/all?q={Uri.EscapeDataString(player1)}";
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
                string eventsUrl = $"https://www.sofascore.com/api/v1/team/{playerId}/events/last/0";
                var eventsResponse = await _httpClient.GetAsync(eventsUrl);
                if (!eventsResponse.IsSuccessStatusCode) return null;

                string eventsJson = await eventsResponse.Content.ReadAsStringAsync();
                using var eventsDoc = JsonDocument.Parse(eventsJson);

                if (eventsDoc.RootElement.TryGetProperty("events", out var eventsArray))
                {
                    // 3. Find the event
                    foreach (var ev in eventsArray.EnumerateArray())
                    {
                        if (string.IsNullOrEmpty(player2))
                        {
                            // If we only have player1 (no "vs"), return their most recent event
                            Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] {betLabel} SofaScore: Found Single-Player Match ID {ev.GetProperty("id").GetInt32()} for {matchName}");
                            return ev.GetRawText();
                        }

                        string homeTeam = ev.GetProperty("homeTeam").GetProperty("name").GetString() ?? "";
                        string awayTeam = ev.GetProperty("awayTeam").GetProperty("name").GetString() ?? "";

                        bool isDoublesQuery = matchName.Contains("/") || matchName.Contains(",");
                        bool isDoublesTarget = homeTeam.Contains("/") || awayTeam.Contains("/");
                        if (isDoublesQuery != isDoublesTarget) continue;

                        if (FuzzyMatch(player2, homeTeam) || FuzzyMatch(player2, awayTeam))
                        {
                            Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] {betLabel} SofaScore: Found Match ID {ev.GetProperty("id").GetInt32()} for {matchName}");
                            return ev.GetRawText();
                        }
                    }
                }
                
                // 4. Try future/next events just in case it hasn't started yet
                string nextEventsUrl = $"https://www.sofascore.com/api/v1/team/{playerId}/events/next/0";
                var nextResponse = await _httpClient.GetAsync(nextEventsUrl);
                if (nextResponse.IsSuccessStatusCode)
                {
                    string nextJson = await nextResponse.Content.ReadAsStringAsync();
                    using var nextDoc = JsonDocument.Parse(nextJson);
                    if (nextDoc.RootElement.TryGetProperty("events", out var nextEventsArray))
                    {
                        foreach (var ev in nextEventsArray.EnumerateArray())
                        {
                            if (string.IsNullOrEmpty(player2))
                            {
                                Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] {betLabel} SofaScore: Found Future Single-Player Match ID {ev.GetProperty("id").GetInt32()} for {matchName}");
                                return ev.GetRawText();
                            }

                            string homeTeam = ev.GetProperty("homeTeam").GetProperty("name").GetString() ?? "";
                            string awayTeam = ev.GetProperty("awayTeam").GetProperty("name").GetString() ?? "";

                            bool isDoublesQuery = matchName.Contains("/") || matchName.Contains(",");
                            bool isDoublesTarget = homeTeam.Contains("/") || awayTeam.Contains("/");
                            if (isDoublesQuery != isDoublesTarget) continue;

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

        public async Task<string?> ResolvePlayerMatchAsync(string selection, DateTime? betPlacedAt, int? betId = null)
        {
            if (string.IsNullOrEmpty(selection)) return null;
            
            string betLabel = betId.HasValue ? $"[Bet #{betId.Value}]" : "[Extraction]";
            var targetDate = betPlacedAt ?? DateTime.UtcNow;

            try
            {
                // Try to extract just the player name (usually the first part before a dash)
                string playerName = selection.Split(new[] { " - ", " : ", " to " }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();

                string searchUrl = $"https://www.sofascore.com/api/v1/search/all?q={Uri.EscapeDataString(playerName)}";
                var searchResponse = await _httpClient.GetAsync(searchUrl);
                if (!searchResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] {betLabel} SofaScore: Search API failed with status: {searchResponse.StatusCode}");
                    return null;
                }

                string searchJson = await searchResponse.Content.ReadAsStringAsync();
                using var searchDoc = JsonDocument.Parse(searchJson);
                
                if (!searchDoc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
                {
                    Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] {betLabel} SofaScore: No results found for player '{playerName}'");
                    return null;
                }

                // Grab the team from the first valid player entity
                foreach (var result in results.EnumerateArray())
                {
                    if (result.TryGetProperty("type", out var type) && type.GetString() == "player")
                    {
                        if (result.TryGetProperty("entity", out var entity))
                        {
                            if (entity.TryGetProperty("team", out var team) && team.TryGetProperty("id", out var teamIdProp))
                            {
                                int teamId = teamIdProp.GetInt32();
                                return await FindClosestMatchForTeamAsync(teamId, targetDate, betId);
                            }
                            else if (entity.TryGetProperty("name", out var playerNameStr))
                            {
                                return playerNameStr.GetString(); // e.g. "Carlos Alcaraz"
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] {betLabel} SofaScore: Exception in ResolvePlayerMatchAsync: {ex.Message}");
            }

            return null;
        }

        private async Task<string?> FindClosestMatchForTeamAsync(int teamId, DateTime targetDate, int? betId = null)
        {
            string? closestMatch = null;
            double minDiff = double.MaxValue;
            string betLabel = betId.HasValue ? $"[Bet #{betId.Value}]" : "[Extraction]";

            // Check both past and future events to find the one closest to the bet placement date
            string[] endpoints = { "last/0", "next/0" };
            
            foreach (var endpoint in endpoints)
            {
                try 
                {
                    string url = $"https://www.sofascore.com/api/v1/team/{teamId}/events/{endpoint}";
                    var response = await _httpClient.GetAsync(url);
                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] {betLabel} SofaScore: endpoint {endpoint} failed with status {response.StatusCode}");
                        continue;
                    }

                    string json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);

                    if (doc.RootElement.TryGetProperty("events", out var eventsArray))
                    {
                        foreach (var ev in eventsArray.EnumerateArray())
                        {
                            if (ev.TryGetProperty("startTimestamp", out var startTsProp))
                            {
                                long ts = startTsProp.GetInt64();
                                DateTime eventDate = DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime;
                                double diff = Math.Abs((eventDate - targetDate).TotalHours);
                                
                                // Limit to 120 hours to prevent grabbing matches from weeks away
                                if (diff < minDiff && diff < 120)
                                {
                                    minDiff = diff;
                                    string homeTeam = ev.GetProperty("homeTeam").GetProperty("name").GetString() ?? "";
                                    string awayTeam = ev.GetProperty("awayTeam").GetProperty("name").GetString() ?? "";
                                    closestMatch = $"{homeTeam} vs {awayTeam}";
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] {betLabel} SofaScore: Exception in endpoint {endpoint}: {ex.Message}");
                }
            }

            if (!string.IsNullOrEmpty(closestMatch))
            {
                Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] {betLabel} SofaScore: Found closest match in history: '{closestMatch}'");
            }
            return closestMatch;
        }

        private bool FuzzyMatch(string query, string target)
        {
            if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(target)) return false;
            
            query = query.ToLowerInvariant();
            target = target.ToLowerInvariant();

            // Strip punctuation for tokenization
            var punctuation = new[] { '.', ',', '-', '/' };
            string cleanQuery = query;
            string cleanTarget = target;
            foreach (var p in punctuation)
            {
                cleanQuery = cleanQuery.Replace(p.ToString(), " ");
                cleanTarget = cleanTarget.Replace(p.ToString(), " ");
            }

            var qTokens = cleanQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => t.Length > 1).ToList();
            var tTokens = cleanTarget.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => t.Length > 1).ToList();

            if (qTokens.Count == 0 || tTokens.Count == 0)
                return target.Contains(query) || query.Contains(target);

            // If ALL meaningful tokens in query are found in target (or vice-versa), it's a match.
            // e.g. query="a gea", target="arthur gea" -> "gea" matches "gea". 
            // Wait, "a" is length 1, so it was filtered out! So qTokens=["gea"], tTokens=["arthur", "gea"].
            // "gea" is in tTokens, so it matches!
            bool qInT = qTokens.All(qt => tTokens.Any(tt => tt == qt || tt.Contains(qt) || qt.Contains(tt)));
            bool tInQ = tTokens.All(tt => qTokens.Any(qt => qt == tt || qt.Contains(tt) || tt.Contains(qt)));

            if (qInT || tInQ) return true;

            // Fallback: If any single token longer than 2 characters exactly matches a token in the target
            foreach (var token in qTokens)
            {
                if (token.Length > 2 && tTokens.Any(tt => tt == token)) return true;
            }
            
            return target.Contains(query) || query.Contains(target);
        }
    }
}
