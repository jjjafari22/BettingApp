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
                
                string homeSearchStr = _teamAliasMapper.NormalizeTeamName(split[0].Trim(), removeStopWords: true);
                if (string.IsNullOrEmpty(homeSearchStr)) homeSearchStr = homeTeam; // Fallback if it was ONLY stop words
                
                string matchQuery = Uri.EscapeDataString(homeTeam); // Search the full home team first to guarantee specific results like "Sheffield United" instead of just "Sheffield"
                
                // 1. Search FotMob API
                string searchUrl = $"https://apigw.fotmob.com/searchapi/suggest?term={matchQuery}";
                string searchJson = await _httpClient.GetStringAsync(searchUrl);
                
                string? eventId = null;

                int currentBestScore = 0;
                using (var searchDoc = System.Text.Json.JsonDocument.Parse(searchJson))
                {
                    searchDoc.RootElement.TryGetProperty("matchSuggest", out var matchSuggests);

                    if (matchSuggests.ValueKind != System.Text.Json.JsonValueKind.Undefined && matchSuggests.GetArrayLength() > 0)
                    {
                        var res = ExtractEventIdWithScore(searchDoc, homeTeam, awayTeam, betPlacedAt);
                        eventId = res.id;
                        currentBestScore = res.score;
                    }

                    // Fallback: If we didn't find a perfect straight match, try searching the most significant word (longest word).
                    if (currentBestScore < 200)
                    {
                        string longestWord = homeSearchStr.Split(' ').OrderByDescending(w => w.Length).FirstOrDefault() ?? homeSearchStr.Split(' ')[0];
                        if (longestWord != homeSearchStr && longestWord.Length >= 3)
                        {
                            string fallbackQuery = Uri.EscapeDataString(longestWord);
                            searchUrl = $"https://apigw.fotmob.com/searchapi/suggest?term={fallbackQuery}";
                            string fallbackJson = await _httpClient.GetStringAsync(searchUrl);
                            
                            using var fallbackDoc = System.Text.Json.JsonDocument.Parse(fallbackJson);
                            var fallbackRes = ExtractEventIdWithScore(fallbackDoc, homeTeam, awayTeam, betPlacedAt);
                            if (fallbackRes.score > currentBestScore || (fallbackRes.score == currentBestScore && fallbackRes.score > 0 && string.IsNullOrEmpty(eventId)))
                            {
                                eventId = fallbackRes.id;
                                currentBestScore = fallbackRes.score;
                            }
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
                        
                        string awaySearchStr = _teamAliasMapper.NormalizeTeamName(cleanAwayTeam, removeStopWords: true);
                        if (string.IsNullOrEmpty(awaySearchStr)) awaySearchStr = cleanAwayTeam;

                        string longestAway = awaySearchStr.Split(' ').OrderByDescending(w => w.Length).FirstOrDefault() ?? awaySearchStr.Split(' ')[0];
                        if (longestAway.Length >= 3)
                        {
                            string fallbackQuery = Uri.EscapeDataString(longestAway);
                            searchUrl = $"https://apigw.fotmob.com/searchapi/suggest?term={fallbackQuery}";
                            string fallbackJson = await _httpClient.GetStringAsync(searchUrl);
                            
                            using var fallbackDoc = System.Text.Json.JsonDocument.Parse(fallbackJson);
                            var fallbackRes = ExtractEventIdWithScore(fallbackDoc, homeTeam, awayTeam, betPlacedAt);
                            if (fallbackRes.score > currentBestScore || (fallbackRes.score == currentBestScore && fallbackRes.score > 0 && string.IsNullOrEmpty(eventId)))
                            {
                                eventId = fallbackRes.id;
                                currentBestScore = fallbackRes.score;
                            }
                        }
                    }
                }

                // Fallback 3: Deep dive into Team Fixtures! (Because apigw suggest only returns top 3-4 matches, hiding older or pre-season friendlies)
                // If currentBestScore < 200, it means we only found a swappedMatch or nothing. Do a Deep Dive to ensure we don't miss a straightMatch!
                if (currentBestScore < 200)
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

                    string longestHome = homeSearchStr.Split(' ').OrderByDescending(w => w.Length).FirstOrDefault() ?? "";
                    string cleanAway = awayTeam;
                    var dm = System.Text.RegularExpressions.Regex.Match(awayTeam, @"\((?:Starts:\s*)?([^)]+)\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (dm.Success) cleanAway = awayTeam.Substring(0, dm.Index).Trim();
                    
                    string awaySearchStr = _teamAliasMapper.NormalizeTeamName(cleanAway, removeStopWords: true);
                    if (string.IsNullOrEmpty(awaySearchStr)) awaySearchStr = cleanAway;

                    string longestAway = awaySearchStr.Split(' ').OrderByDescending(w => w.Length).FirstOrDefault() ?? "";

                    await Task.WhenAll(
                        TryCollectTeamIds(homeSearchStr),
                        TryCollectTeamIds(awaySearchStr),
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
                                
                                DateTime targetDate = betPlacedAt ?? DateTime.UtcNow;
                                // We will carry over smallestTimeDiff to prefer closer matches
                                TimeSpan smallestTimeDiff = TimeSpan.MaxValue;
                                
                                foreach (var f in fixtures.EnumerateArray())
                                {
                                    string id = "";
                                    if (f.TryGetProperty("id", out var idProp))
                                    {
                                        id = idProp.ValueKind == System.Text.Json.JsonValueKind.Number ? idProp.GetInt32().ToString() : idProp.GetString() ?? "";
                                    }
                                    
                                    string homeName = f.TryGetProperty("home", out var h) && h.TryGetProperty("name", out var hn) ? hn.GetString() ?? "" : "";
                                    string awayName = f.TryGetProperty("away", out var a) && a.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "";
                                    
                                    int matchScore = GetTeamMatchScore(homeTeam, awayTeam ?? "", homeName, awayName);
                                    if (matchScore == 0) continue;

                                    if (f.TryGetProperty("status", out var statusProp) && statusProp.TryGetProperty("utcTime", out var utcProp))
                                    {
                                        if (DateTime.TryParse(utcProp.GetString(), out DateTime fixtureDate))
                                        {
                                            TimeSpan diff = (fixtureDate - targetDate).Duration();
                                            // Reject matches that are completely wildly off
                                            if (diff.TotalDays > 120) continue;

                                            if (matchScore > currentBestScore || (matchScore == currentBestScore && diff < smallestTimeDiff))
                                            {
                                                currentBestScore = matchScore;
                                                smallestTimeDiff = diff;
                                                eventId = id;
                                            }
                                        }
                                    }
                                    else if (string.IsNullOrEmpty(eventId) || matchScore > currentBestScore)
                                    {
                                        currentBestScore = matchScore;
                                        eventId = id;
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

                // 2. Fetch Match HTML (Bust CDN cache with a unique timestamp)
                string matchHtml = await _httpClient.GetStringAsync($"https://www.fotmob.com/match/{eventId}?_ts={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
                
                // 3. Extract SSR JSON
                var match = System.Text.RegularExpressions.Regex.Match(matchHtml, @"<script id=""__NEXT_DATA__"" type=""application/json"">(.*?)</script>");
                if (match.Success)
                {
                    try 
                    {
                        using var ssrDoc = System.Text.Json.JsonDocument.Parse(match.Groups[1].Value);
                        var root = ssrDoc.RootElement;
                        if (root.TryGetProperty("props", out var props) &&
                            props.TryGetProperty("pageProps", out var pageProps))
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
                                if (header.TryGetProperty("teams", out var teams)) trimmedHeader["teams"] = teams;
                                trimmedData["header"] = trimmedHeader;
                            }

                            if (pageProps.TryGetProperty("content", out var content))
                            {
                                if (content.TryGetProperty("matchFacts", out var matchFacts))
                                {
                                    var trimmedFacts = new System.Collections.Generic.Dictionary<string, object>();
                                    if (matchFacts.TryGetProperty("infoBox", out var infoBox)) trimmedFacts["infoBox"] = infoBox;
                                    if (matchFacts.TryGetProperty("events", out var events)) 
                                    {
                                        trimmedFacts["events"] = FlattenEvents(events);
                                    }
                                    trimmedData["matchFacts"] = trimmedFacts;
                                }

                                if (content.TryGetProperty("stats", out var stats)) 
                                {
                                    trimmedData["stats"] = FlattenMatchStats(stats);
                                }
                                
                                if (content.TryGetProperty("playerStats", out var playerStats)) 
                                {
                                    trimmedData["playerStats"] = FlattenPlayerStats(playerStats);
                                }

                                if (content.TryGetProperty("lineup", out var lineup))
                                {
                                    trimmedData["lineup"] = FlattenLineup(lineup);
                                }
                            }

                            if (trimmedData.TryGetValue("general", out var genObj) && genObj is System.Collections.Generic.Dictionary<string, object> genDict)
                            {
                                if (genDict.TryGetValue("started", out var startedObj) && startedObj is System.Text.Json.JsonElement stElem && stElem.ValueKind == System.Text.Json.JsonValueKind.False)
                                {
                                    trimmedData["WARNING"] = "THIS MATCH HAS NOT STARTED YET! THERE ARE NO STATS. DO NOT SEARCH GOOGLE FOR STATS! MARK AS PENDING.";
                                }
                            }

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

        private object FlattenEvents(System.Text.Json.JsonElement eventsObj)
        {
            var flatEvents = new System.Collections.Generic.List<object>();
            try 
            {
                if (eventsObj.ValueKind == System.Text.Json.JsonValueKind.Object && eventsObj.TryGetProperty("events", out var eventsArr))
                {
                    foreach (var ev in eventsArr.EnumerateArray())
                    {
                        var dict = new System.Collections.Generic.Dictionary<string, object>();
                        if (ev.TryGetProperty("timeStr", out var time)) dict["time"] = time.ToString() ?? "";
                        if (ev.TryGetProperty("type", out var type)) dict["type"] = type.GetString() ?? "";
                        if (ev.TryGetProperty("nameStr", out var name)) dict["name"] = name.GetString() ?? "";
                        
                        if (ev.TryGetProperty("card", out var card)) dict["card"] = card.GetString() ?? "";
                        if (ev.TryGetProperty("goalDescription", out var gDesc)) dict["desc"] = gDesc.GetString() ?? "";
                        if (ev.TryGetProperty("isHome", out var isHome)) 
                        {
                            if (isHome.ValueKind == System.Text.Json.JsonValueKind.True) dict["isHome"] = true;
                            else if (isHome.ValueKind == System.Text.Json.JsonValueKind.False) dict["isHome"] = false;
                        }

                        if (ev.TryGetProperty("swap", out var swap) && swap.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            var swapArr = new System.Collections.Generic.List<string>();
                            foreach (var s in swap.EnumerateArray())
                            {
                                if (s.TryGetProperty("name", out var sName)) swapArr.Add(sName.GetString() ?? "");
                            }
                            if (swapArr.Count > 0) dict["swap"] = swapArr;
                        }
                        
                        flatEvents.Add(dict);
                    }
                }
            }
            catch { }
            return flatEvents;
        }

        private object FlattenMatchStats(System.Text.Json.JsonElement statsObj)
        {
            var flatStats = new System.Collections.Generic.Dictionary<string, object>();
            try 
            {
                if (statsObj.TryGetProperty("Periods", out var periods) && periods.TryGetProperty("All", out var all) && all.TryGetProperty("stats", out var statsArr))
                {
                    foreach (var category in statsArr.EnumerateArray())
                    {
                        if (category.TryGetProperty("stats", out var innerStats))
                        {
                            foreach (var stat in innerStats.EnumerateArray())
                            {
                                string title = stat.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                                if (string.IsNullOrEmpty(title)) continue;
                                
                                if (stat.TryGetProperty("stats", out var s) && s.ValueKind == System.Text.Json.JsonValueKind.Array)
                                {
                                    var vals = new System.Collections.Generic.List<string>();
                                    foreach (var v in s.EnumerateArray()) vals.Add(v.ToString());
                                    flatStats[title] = vals;
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return flatStats;
        }

        private object FlattenLineup(System.Text.Json.JsonElement lineupObj)
        {
            var flatLineup = new System.Collections.Generic.Dictionary<string, object>();
            try 
            {
                if (lineupObj.TryGetProperty("homeTeam", out var home))
                {
                    flatLineup["homeTeam"] = FlattenTeamLineup(home);
                }
                if (lineupObj.TryGetProperty("awayTeam", out var away))
                {
                    flatLineup["awayTeam"] = FlattenTeamLineup(away);
                }
            }
            catch { }
            return flatLineup;
        }

        private object FlattenTeamLineup(System.Text.Json.JsonElement teamObj)
        {
            var teamLineup = new System.Collections.Generic.Dictionary<string, object>();
            if (teamObj.TryGetProperty("starters", out var starters))
            {
                teamLineup["starters"] = ExtractPlayerNames(starters);
            }
            if (teamObj.TryGetProperty("subs", out var subs))
            {
                teamLineup["subs"] = ExtractPlayerNames(subs);
            }
            return teamLineup;
        }

        private System.Collections.Generic.List<string> ExtractPlayerNames(System.Text.Json.JsonElement playersArray)
        {
            var names = new System.Collections.Generic.List<string>();
            if (playersArray.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var player in playersArray.EnumerateArray())
                {
                    if (player.TryGetProperty("name", out var name))
                    {
                        names.Add(name.GetString() ?? "");
                    }
                }
            }
            return names;
        }

        private object FlattenPlayerStats(System.Text.Json.JsonElement playerStatsElement)
        {
            var flatStats = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, object>>();
            
            try 
            {
                if (playerStatsElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    foreach (var playerProp in playerStatsElement.EnumerateObject())
                    {
                        var playerObj = playerProp.Value;
                        if (playerObj.ValueKind == System.Text.Json.JsonValueKind.Object)
                        {
                            string playerName = playerObj.TryGetProperty("name", out var n) ? n.GetString() ?? playerProp.Name : playerProp.Name;
                            var pStats = new System.Collections.Generic.Dictionary<string, object>();
                            
                            if (playerObj.TryGetProperty("stats", out var statsArr) && statsArr.ValueKind == System.Text.Json.JsonValueKind.Array)
                            {
                                foreach (var category in statsArr.EnumerateArray())
                                {
                                    if (category.TryGetProperty("stats", out var innerStats) && innerStats.ValueKind == System.Text.Json.JsonValueKind.Object)
                                    {
                                        foreach (var statProp in innerStats.EnumerateObject())
                                        {
                                            string statName = statProp.Name;
                                            if (statProp.Value.TryGetProperty("stat", out var statVal) && statVal.TryGetProperty("value", out var val))
                                            {
                                                if (val.ValueKind == System.Text.Json.JsonValueKind.Number)
                                                    pStats[statName] = val.GetDouble();
                                                else if (val.ValueKind == System.Text.Json.JsonValueKind.String)
                                                    pStats[statName] = val.GetString() ?? "";
                                            }
                                        }
                                    }
                                }
                            }
                            if (pStats.Count > 0)
                                flatStats[playerName] = pStats;
                        }
                    }
                }
            }
            catch { }
            
            return flatStats;
        }

        public static bool CheckSubset(string[] tokensA, string[] tokensB)
        {
            if (tokensA.Length == 0) return true;
            if (tokensB.Length == 0) return false;
            return tokensA.All(a => tokensB.Any(b => FuzzyMatch(a, b)));
        }

        public static bool HasSpecialModifier(string input)
        {
            var text = input.ToLowerInvariant();
            return text.Contains("women") || text.Contains("(w)") || text.Contains("femenil") || 
                   text.Contains("u21") || text.Contains("u23") || text.Contains("u19") || 
                   text.Contains("u18") || text.Contains("u20") || text.Contains("reserves") || text.Contains("youth");
        }

        public bool CheckTeamMatch(string query, string option)
        {
            query = _teamAliasMapper.NormalizeTeamName(query, removeStopWords: false);
            option = _teamAliasMapper.NormalizeTeamName(option, removeStopWords: false);

            var qTokens = query.Split(new[] { ' ', '-', '/' }, StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 2 && !IsGenericPrefix(w)).ToArray();
            if (qTokens.Length == 0 && !string.IsNullOrEmpty(query)) qTokens = new[] { _teamAliasMapper.NormalizeTeamName(query, removeStopWords: false) };
            
            var oTokens = option.Split(new[] { ' ', '-', '/' }, StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 2 && !IsGenericPrefix(w)).ToArray();
            if (oTokens.Length == 0 && !string.IsNullOrEmpty(option)) oTokens = new[] { _teamAliasMapper.NormalizeTeamName(option, removeStopWords: false) };

            if (oTokens.Length == 0) return false;

            bool qInO = CheckSubset(qTokens, oTokens);
            bool oInQ = CheckSubset(oTokens, qTokens);

            if (HasSpecialModifier(option) != HasSpecialModifier(query))
            {
                return false;
            }

            return qInO || oInQ;
        }

        public int GetTeamMatchScore(string queryHome, string queryAway, string optHome, string optAway)
        {
            if (string.IsNullOrEmpty(queryHome)) return 0;
            
            bool straightMatch = CheckTeamMatch(queryHome, optHome) && (string.IsNullOrEmpty(queryAway) || CheckTeamMatch(queryAway, optAway));
            if (straightMatch) return 200;

            bool swappedMatch = CheckTeamMatch(queryHome, optAway) && (string.IsNullOrEmpty(queryAway) || CheckTeamMatch(queryAway, optHome));
            if (swappedMatch) return 100;

            return 0;
        }

        public bool AreTeamsMatching(string queryHome, string queryAway, string optHome, string optAway)
        {
            return GetTeamMatchScore(queryHome, queryAway, optHome, optAway) > 0;
        }

        private (string? id, int score) ExtractEventIdWithScore(System.Text.Json.JsonDocument doc, string homeTeam, string awayTeam, DateTime? betPlacedAt)
        {
            if (!doc.RootElement.TryGetProperty("matchSuggest", out var matchSuggests) || matchSuggests.GetArrayLength() == 0)
                return (null, 0);

            if (!matchSuggests[0].TryGetProperty("options", out var options) || options.GetArrayLength() == 0)
                return (null, 0);

            DateTime? parsedTargetDate = null;
            if (awayTeam != null)
            {
                var dateMatch = System.Text.RegularExpressions.Regex.Match(awayTeam, @"\((?:Starts:\s*)?([^)]+)\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (dateMatch.Success)
                {
                    string dateStr = dateMatch.Groups[1].Value;
                    awayTeam = awayTeam.Substring(0, dateMatch.Index).Trim();
                    
                    if (DateTime.TryParse(dateStr, out DateTime dt1)) parsedTargetDate = dt1;
                    else if (DateTime.TryParseExact(dateStr, "dd.MMM HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime dt2)) parsedTargetDate = dt2;
                }
            }

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

                int score = GetTeamMatchScore(homeTeam, awayTeam ?? "", optionHomeName, optionAwayName);
                if (score == 0)
                {
                    continue;
                }

                // Parse match date to prioritize the closest match (e.g. Leg 1 vs Leg 2)
                if (payload.TryGetProperty("matchDate", out var matchDateElement))
                {
                    if (DateTime.TryParse(matchDateElement.GetString(), out DateTime matchDate))
                    {
                        DateTime targetDate = betPlacedAt ?? DateTime.UtcNow;
                        
                        if (parsedTargetDate.HasValue)
                        {
                            targetDate = parsedTargetDate.Value;
                            if (betPlacedAt.HasValue && Math.Abs((targetDate - betPlacedAt.Value).TotalDays) > 180)
                            {
                                // Fix year wrap-around
                                targetDate = targetDate.AddYears(targetDate < betPlacedAt.Value ? 1 : -1);
                            }
                        }

                        // Calculate absolute time difference between target date and match date
                        TimeSpan diff = (matchDate - targetDate).Duration();
                        
                        // Strict threshold: If the match is more than 90 days apart from our target date, reject it!
                        if (diff.TotalDays > 90)
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

            return (bestId, bestScore);
        }

        public static bool FuzzyMatch(string token, string fixtureName)
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

        public static bool IsGenericPrefix(string word)
        {
            if (string.IsNullOrEmpty(word)) return true;
            string w = word.ToLowerInvariant();
            return w == "fc" || w == "fk" || w == "bk" || w == "if" || w == "il" || w == "ik" || w == "ff" || w == "gf" || w == "cd" || w == "cf" || w == "al" || w == "el" || w == "la" || w == "de";
        }
    }
}
