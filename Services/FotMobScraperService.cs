using System;
using System.Linq;
using System.Threading.Tasks;

namespace BettingApp.Services
{
    public class FotMobScraperService
    {
        private readonly HttpClient _httpClient;
        private readonly TeamAliasMappingService _teamAliasMapper;

        public FotMobScraperService(HttpClient httpClient, TeamAliasMappingService teamAliasMapper)
        {
            _httpClient = httpClient;
            _teamAliasMapper = teamAliasMapper;
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        }

        public async Task<string?> GetMatchStatsJsonAsync(string matchName, DateTime? betPlacedAt = null, int? betId = null)
        {
            try
            {
                string betLabel = betId.HasValue ? $"[Bet #{betId.Value}]" : "[Test/Manual]";
                // 1. Clean the match name and extract Home Team for robust searching
                string[] split = matchName.Split(new[] { " vs ", " - ", " v " }, StringSplitOptions.None);
                string homeTeam = _teamAliasMapper.NormalizeTeamName(split[0].Trim(), removeStopWords: false);
                string awayTeam = split.Length > 1 ? _teamAliasMapper.NormalizeTeamName(split[1].Trim(), removeStopWords: false) : "";
                
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

                // Fallback 3: Deep dive into Team Fixtures! (Because apigw suggest only returns top 3-4 matches, hiding older or pre-season friendlies)
                if (string.IsNullOrEmpty(eventId))
                {
                    // Collect team IDs from our previous searches!
                    var teamIdsToDeepSearch = new System.Collections.Generic.HashSet<string>();
                    
                    async Task TryCollectTeamIds(string query)
                    {
                        if (string.IsNullOrEmpty(query) || query.Length < 3) return;
                        try {
                            string qUrl = $"https://apigw.fotmob.com/searchapi/suggest?term={Uri.EscapeDataString(query)}";
                            string qJson = await _httpClient.GetStringAsync(qUrl);
                            using var qDoc = System.Text.Json.JsonDocument.Parse(qJson);
                            if (qDoc.RootElement.TryGetProperty("teamSuggest", out var teamSuggest) && teamSuggest.GetArrayLength() > 0)
                            {
                                if (teamSuggest[0].TryGetProperty("options", out var options))
                                {
                                    foreach (var opt in options.EnumerateArray().Take(2))
                                    {
                                        if (opt.TryGetProperty("payload", out var payload) && payload.TryGetProperty("id", out var tid))
                                        {
                                            string idStr = tid.ValueKind == System.Text.Json.JsonValueKind.Number ? tid.GetInt32().ToString() : tid.GetString() ?? "";
                                            if (!string.IsNullOrEmpty(idStr)) teamIdsToDeepSearch.Add(idStr);
                                        }
                                    }
                                }
                            }
                        } catch { }
                    }

                    string longestHome = homeTeam.Split(' ').OrderByDescending(w => w.Length).FirstOrDefault() ?? "";
                    string cleanAway = awayTeam;
                    var dm = System.Text.RegularExpressions.Regex.Match(awayTeam, @"\((?:Starts:\s*)?([^)]+)\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (dm.Success) cleanAway = awayTeam.Substring(0, dm.Index).Trim();
                    string longestAway = cleanAway.Split(' ').OrderByDescending(w => w.Length).FirstOrDefault() ?? "";

                    await Task.WhenAll(
                        TryCollectTeamIds(homeTeam),
                        TryCollectTeamIds(awayTeam),
                        TryCollectTeamIds(longestHome),
                        TryCollectTeamIds(longestAway)
                    );

                    // Now for each team ID, fetch their full fixture list!
                    foreach (var teamId in teamIdsToDeepSearch)
                    {
                        try 
                        {
                            string html = await _httpClient.GetStringAsync($"https://www.fotmob.com/teams/{teamId}/overview/team");
                            var regexMatch = System.Text.RegularExpressions.Regex.Match(html, @"<script id=""__NEXT_DATA__"" type=""application/json"">(.*?)</script>");
                            if (regexMatch.Success)
                            {
                                using var doc = System.Text.Json.JsonDocument.Parse(regexMatch.Groups[1].Value);
                                var fallback = doc.RootElement.GetProperty("props").GetProperty("pageProps").GetProperty("fallback");
                                var teamData = fallback.GetProperty($"team-{teamId}");
                                var fixturesProp = teamData.GetProperty("fixtures");
                                
                                if (fixturesProp.ValueKind != System.Text.Json.JsonValueKind.Object || 
                                    !fixturesProp.TryGetProperty("allFixtures", out var allFixtures) ||
                                    !allFixtures.TryGetProperty("fixtures", out var fixtures))
                                {
                                    continue; // Skip if no fixtures exist for this team
                                }
                                
                                foreach (var f in fixtures.EnumerateArray())
                                {
                                    string id = "";
                                    if (f.TryGetProperty("id", out var idProp))
                                    {
                                        id = idProp.ValueKind == System.Text.Json.JsonValueKind.Number ? idProp.GetInt32().ToString() : idProp.GetString() ?? "";
                                    }
                                    
                                    string homeName = f.TryGetProperty("home", out var h) && h.TryGetProperty("name", out var hn) ? hn.GetString() ?? "" : "";
                                    string awayName = f.TryGetProperty("away", out var a) && a.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "";
                                    
                                    // Check if this fixture matches our target match!
                                    var homeTokens = homeTeam.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 2 && !IsGenericPrefix(w)).Select(NormalizeText).ToArray();
                                    var awayTokens = cleanAway.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 2 && !IsGenericPrefix(w)).Select(NormalizeText).ToArray();
                                    
                                    if (homeTokens.Length == 0) homeTokens = new[] { NormalizeText(homeTeam) };
                                    if (awayTokens.Length == 0 && !string.IsNullOrEmpty(cleanAway)) awayTokens = new[] { NormalizeText(cleanAway) };
                                    
                                    string normFixtureHome = NormalizeText(homeName);
                                    string normFixtureAway = NormalizeText(awayName);

                                    bool match1 = homeTokens.Any(t => FuzzyMatch(t, normFixtureHome)) && (awayTokens.Length == 0 || awayTokens.Any(t => FuzzyMatch(t, normFixtureAway)));
                                    bool match2 = homeTokens.Any(t => FuzzyMatch(t, normFixtureAway)) && (awayTokens.Length == 0 || awayTokens.Any(t => FuzzyMatch(t, normFixtureHome)));

                                    if (match1 || match2)
                                    {
                                        eventId = id;
                                        break; // Found it!
                                    }
                                }
                            }
                        } 
                        catch { }
                        
                        if (!string.IsNullOrEmpty(eventId)) break; // Found it, stop searching teams!
                    }
                }

                if (string.IsNullOrEmpty(eventId))
                {
                    Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] {betLabel} FotMob: Could not find match '{matchName}'");
                    return null;
                }

                Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] {betLabel} FotMob: Found Match ID {eventId} for {matchName}");

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

                            if (pageProps.TryGetProperty("general", out var general))
                            {
                                var trimmedGeneral = new System.Collections.Generic.Dictionary<string, object>();
                                if (general.TryGetProperty("matchTimeUTC", out var mTime)) trimmedGeneral["matchTimeUTC"] = mTime;
                                if (general.TryGetProperty("started", out var st)) trimmedGeneral["started"] = st;
                                if (general.TryGetProperty("finished", out var fin)) trimmedGeneral["finished"] = fin;
                                trimmedData["general"] = trimmedGeneral;
                            }

                            if (pageProps.TryGetProperty("header", out var header))
                            {
                                var trimmedHeader = new System.Collections.Generic.Dictionary<string, object>();
                                if (header.TryGetProperty("status", out var stat)) trimmedHeader["status"] = stat;
                                trimmedData["header"] = trimmedHeader;
                            }

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
                    
                    // Fallback to a truncated payload to prevent crashing the AI with 170k+ characters
                    string rawString = match.Groups[1].Value;
                    return rawString.Length > 5000 ? rawString.Substring(0, 5000) + "...[TRUNCATED]" : rawString;
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] Error scraping JSON for {matchName}: {ex.Message}");
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
                        .Where(w => w.Length >= 2 && !IsGenericPrefix(w))
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
                                             .Where(w => w.Length >= 2 && !IsGenericPrefix(w)).Select(NormalizeText).ToArray();
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
            if (string.IsNullOrEmpty(text)) return "";
            var normalized = text.Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder();
            foreach (var c in normalized)
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(c);
                }
            }
            string result = sb.ToString().ToLowerInvariant();
            
            // Map common nordic and german characters that don't decompose to ascii equivalents
            result = result.Replace("ø", "o").Replace("æ", "ae").Replace("å", "a")
                           .Replace("ö", "o").Replace("ä", "a").Replace("ü", "u");
                           
            // Map common english translated team names to local names
            result = result.Replace("copenhagen", "kobenhavn");
            
            return result;
        }

        private bool FuzzyMatch(string token, string fixtureName)
        {
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(fixtureName)) return false;
            if (fixtureName.Contains(token)) return true;
            if (token.Contains(fixtureName)) return true;
            
            // Check if any word in fixtureName shares the first 4 chars with token
            if (token.Length >= 4)
            {
                string prefix = token.Substring(0, 4);
                var fixtureTokens = fixtureName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var ft in fixtureTokens)
                {
                    if (ft.Length >= 4 && ft.StartsWith(prefix)) return true;
                }
            }
            return false;
        }

        private bool IsGenericPrefix(string word)
        {
            if (string.IsNullOrEmpty(word)) return true;
            string w = word.ToLowerInvariant();
            return w == "fc" || w == "fk" || w == "bk" || w == "if" || w == "il" || w == "ik" || w == "ff" || w == "gf" || w == "cd" || w == "cf";
        }
    }
}
