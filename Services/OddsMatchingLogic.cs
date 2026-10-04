using System;
using System.Collections.Generic;
using System.Linq;
using BettingApp.Models;

namespace BettingApp.Services
{
    public static class OddsMatchingLogic
    {
    public static string GetTranslatedOutcomeName(string oName, string matchName)
    {
        string displayOName = oName;
        if (!string.IsNullOrEmpty(matchName))
        {
            var matchSplit = matchName.Split(new[] { " vs ", " v ", " - " }, StringSplitOptions.None);
            if (matchSplit.Length >= 2)
            {
                string t1 = matchSplit[0].Trim();
                string t2 = matchSplit[1].Trim();
                
                var hcMatch = System.Text.RegularExpressions.Regex.Match(displayOName, @"^(1|X|x|2)\s*\(([^)]+)\)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (hcMatch.Success)
                {
                    string teamSel = hcMatch.Groups[1].Value.ToUpperInvariant();
                    string hcStr = hcMatch.Groups[2].Value.Trim();
                    
                    double t1Hc = 0;
                    double t2Hc = 0;
                    bool validHc = false;
                    
                    if (hcStr.Contains("-") && hcStr.Length > 2)
                    {
                        var parts = hcStr.Split('-');
                        if (parts.Length == 2 && double.TryParse(parts[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double p1) && double.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double p2))
                        {
                            t1Hc = p1 - p2;
                            t2Hc = p2 - p1;
                            validHc = true;
                        }
                    }
                    else if (hcStr.Contains(":"))
                    {
                        var parts = hcStr.Split(':');
                        if (parts.Length == 2 && double.TryParse(parts[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double p1) && double.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double p2))
                        {
                            t1Hc = p1 - p2;
                            t2Hc = p2 - p1;
                            validHc = true;
                        }
                    }
                    if (!validHc && double.TryParse(hcStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
                    {
                        t1Hc = val;
                        t2Hc = -val;
                        validHc = true;
                    }
                    
                    if (validHc)
                    {
                        if (teamSel == "1") displayOName = $"{t1} {(t1Hc > 0 ? "+" : "")}{t1Hc}";
                        else if (teamSel == "2") displayOName = $"{t2} {(t2Hc > 0 ? "+" : "")}{t2Hc}";
                        else if (teamSel == "X") displayOName = $"Draw ({(t1Hc > 0 ? "+" : "")}{t1Hc})";
                    }
                }
                else
                {
                    if (displayOName == "1") displayOName = t1;
                    else if (displayOName == "2") displayOName = t2;
                    else if (displayOName == "X" || displayOName.Equals("Draw", StringComparison.OrdinalIgnoreCase)) displayOName = "Draw";
                    else if (displayOName.Equals("1X", StringComparison.OrdinalIgnoreCase)) displayOName = $"{t1} or Draw";
                    else if (displayOName.Equals("X2", StringComparison.OrdinalIgnoreCase) || displayOName.Equals("2X", StringComparison.OrdinalIgnoreCase)) displayOName = $"{t2} or Draw";
                    else if (displayOName == "12") displayOName = $"{t1} or {t2}";
                    else {
                        if (displayOName.StartsWith("1 ")) displayOName = t1 + displayOName.Substring(1);
                        else if (displayOName.StartsWith("2 ")) displayOName = t2 + displayOName.Substring(1);
                        
                        displayOName = displayOName.Replace("1/", $"{t1}/").Replace("/1", $"/{t1}");
                        displayOName = displayOName.Replace("2/", $"{t2}/").Replace("/2", $"/{t2}");
                        displayOName = displayOName.Replace("X/", "Draw/").Replace("x/", "Draw/");
                        displayOName = displayOName.Replace("/X", "/Draw").Replace("/x", "/Draw");
                    }
                }
            }
        }
        return displayOName;
    }

    public static bool IsFuzzyPlayerMatch(string str1, string str2)
    {
        if (string.IsNullOrWhiteSpace(str1) || string.IsNullOrWhiteSpace(str2)) return false;
        
        string norm1 = BettingApp.Services.TeamAliasMappingService.RemoveDiacritics(str1).ToLowerInvariant();
        string norm2 = BettingApp.Services.TeamAliasMappingService.RemoveDiacritics(str2).ToLowerInvariant();

        Func<string, string> injectImplicitOdds = (string norm) => {
            if (norm.Contains("to provide") || norm.Contains("to score") || norm.Contains("to get") || norm.Contains("anytime") || norm.Contains("carded") || norm.Contains("booked"))
            {
                if (!norm.Contains("not ")) return norm + " over 0.5";
                else return norm + " under 0.5";
            }
            return norm;
        };
        norm1 = injectImplicitOdds(norm1);
        norm2 = injectImplicitOdds(norm2);

        // 1. Extract and compare numbers
        var numRegex = new System.Text.RegularExpressions.Regex(@"([+-]?\d+(?:\.\d+)?)");
        var nums1 = numRegex.Matches(norm1).Cast<System.Text.RegularExpressions.Match>().Select(m => m.Value).ToList();
        var nums2 = numRegex.Matches(norm2).Cast<System.Text.RegularExpressions.Match>().Select(m => m.Value).ToList();
        
        if (nums1.Any() && nums2.Any())
        {
            if (double.TryParse(nums1.Last(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double n1) && 
                double.TryParse(nums2.Last(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double n2))
            {
                if (Math.Abs(n1 - n2) > 0.01) return false;
            }
            else
            {
                if (nums1.Last() != nums2.Last()) return false;
            }
        }
        else if (nums1.Any() != nums2.Any())
        {
            var signedRegex = new System.Text.RegularExpressions.Regex(@"^[+-]\d+(?:\.\d+)?$");
            if (nums1.Any(n => signedRegex.IsMatch(n)) || nums2.Any(n => signedRegex.IsMatch(n)))
            {
                return false;
            }
        }


        var allTokens1 = norm1.Split(new[] { ' ', '-', '.', ',', ':', '/', '+' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        var allTokens2 = norm2.Split(new[] { ' ', '-', '.', ',', ':', '/', '+' }, StringSplitOptions.RemoveEmptyEntries).ToList();

        // 2. Extract and compare directions
        bool hasOver1 = allTokens1.Contains("over");
        bool hasUnder1 = allTokens1.Contains("under");
        bool hasOver2 = allTokens2.Contains("over");
        bool hasUnder2 = allTokens2.Contains("under");
        
        if ((hasOver1 || hasUnder1) && (hasOver2 || hasUnder2))
        {
            if (hasOver1 != hasOver2 || hasUnder1 != hasUnder2) return false;
        }

        bool hasYes1 = allTokens1.Contains("yes");
        bool hasNo1 = allTokens1.Contains("no");
        bool hasYes2 = allTokens2.Contains("yes");
        bool hasNo2 = allTokens2.Contains("no");
        
        if ((hasYes1 || hasNo1) && (hasYes2 || hasNo2))
        {
            if (hasYes1 != hasYes2 || hasNo1 != hasNo2) return false;
        }

        // 3. Compare name tokens
        var ignoreWords = new HashSet<string> { "over", "under", "yes", "no", "player", "shots", "target", "score", "anytime", "goalscorer", "fouls", "assists", "cards", "booked", "carded", "booking", "points" };
        
        var tokens1 = allTokens1
            .Where(t => t.Length >= 3 && !ignoreWords.Contains(t) && !double.TryParse(t, out _))
            .ToList();
            
        var tokens2 = allTokens2
            .Where(t => t.Length >= 3 && !ignoreWords.Contains(t) && !double.TryParse(t, out _))
            .ToList();
            
        if (!tokens1.Any() || !tokens2.Any()) return false;
        
        var intersectCount = tokens1.Intersect(tokens2).Count();
        return intersectCount == tokens1.Count || intersectCount == tokens2.Count;
    }

    public static bool IsOutcomeMatch(string oName, string displayOName, BettingApp.Services.AiVisionLeg? ActiveLookupLeg, BettingApp.Models.OddsPapiSearchResult? PapiSearchResult)
    {
        bool isMatch = false;
        if (ActiveLookupLeg != null && !string.IsNullOrEmpty(ActiveLookupLeg.Selection))
        {
            bool logicallyMatched = false;
            if (ActiveLookupLeg.Match != null)
            {
                var hcSplit = ActiveLookupLeg.Match.Split(new[] { " vs ", " v ", " - " }, StringSplitOptions.None);
                if (hcSplit.Length >= 2)
                {
                    string normSelRaw = BettingApp.Services.TeamAliasMappingService.RemoveDiacritics(ActiveLookupLeg.Selection).ToLowerInvariant();
                    string aiT1 = BettingApp.Services.TeamAliasMappingService.RemoveDiacritics(hcSplit[0].Split('(')[0].Trim()).ToLowerInvariant();
                    string aiT2 = BettingApp.Services.TeamAliasMappingService.RemoveDiacritics(hcSplit[1].Split('(')[0].Trim()).ToLowerInvariant();
                    
                    string opT1 = "";
                    string opT2 = "";
                    if (PapiSearchResult != null && !string.IsNullOrEmpty(PapiSearchResult.MatchName))
                    {
                        var opMatchSplit = PapiSearchResult.MatchName.Split(new[] { " vs ", " v ", " - " }, StringSplitOptions.None);
                        if (opMatchSplit.Length >= 2)
                        {
                            opT1 = BettingApp.Services.TeamAliasMappingService.RemoveDiacritics(opMatchSplit[0].Trim()).ToLowerInvariant();
                            opT2 = BettingApp.Services.TeamAliasMappingService.RemoveDiacritics(opMatchSplit[1].Trim()).ToLowerInvariant();
                        }
                    }
                    
                    bool isSwapped = false;
                    // Determine if the bookmaker's Team 1 is actually Odds Papi's Team 2
                    if ((opT2.Contains(aiT1) && aiT1.Length > 3) || (aiT1.Contains(opT2) && opT2.Length > 3) || 
                        (opT1.Contains(aiT2) && aiT2.Length > 3) || (aiT2.Contains(opT1) && opT1.Length > 3))
                    {
                        // Ensure it's not just a generic match like "fc"
                        if (!opT1.Contains(aiT1) && !aiT1.Contains(opT1))
                        {
                            isSwapped = true;
                        }
                    }
                    
                    // normT1 represents the team that corresponds to Odds Papi's outcome "1".
                    string normT1 = isSwapped ? aiT2 : aiT1;
                    string normT2 = isSwapped ? aiT1 : aiT2;
                    
                    string aiTarget = "";
                    string htftTarget = "";
                    
                    if (normSelRaw.Contains(" / ") || normSelRaw.Contains(" - "))
                    {
                        var htftSplit = normSelRaw.Split(new[] { " / ", " - " }, StringSplitOptions.RemoveEmptyEntries);
                        if (htftSplit.Length == 2)
                        {
                            string ht = htftSplit[0].Trim();
                            string ft = htftSplit[1].Trim();
                            string htCode = ht.Contains(normT1) ? "1" : (ht.Contains(normT2) ? "2" : (ht.Contains("draw") || ht.Contains("tie") ? "X" : ""));
                            string ftCode = ft.Contains(normT1) ? "1" : (ft.Contains(normT2) ? "2" : (ft.Contains("draw") || ft.Contains("tie") ? "X" : ""));
                            if (!string.IsNullOrEmpty(htCode) && !string.IsNullOrEmpty(ftCode))
                            {
                                htftTarget = $"{htCode}/{ftCode}";
                            }
                        }
                    }
                    
                    if (string.IsNullOrEmpty(htftTarget))
                    {
                        if (normSelRaw.Contains(normT1) && normSelRaw.Contains(normT2)) aiTarget = "12";
                        else if (normSelRaw.Contains(normT1) && (normSelRaw.Contains("draw") || normSelRaw.Contains("tie"))) aiTarget = "1X";
                        else if (normSelRaw.Contains(normT2) && (normSelRaw.Contains("draw") || normSelRaw.Contains("tie"))) aiTarget = "X2";
                        else if (normSelRaw.Contains(normT1)) aiTarget = "1";
                        else if (normSelRaw.Contains(normT2)) aiTarget = "2";
                        else if (normSelRaw.Contains("draw") || normSelRaw.Contains("tie")) aiTarget = "X";
                    }
                    
                    if (!string.IsNullOrEmpty(htftTarget) && string.Equals(oName, htftTarget, StringComparison.OrdinalIgnoreCase))
                    {
                        logicallyMatched = true;
                    }
                    else if (!string.IsNullOrEmpty(aiTarget))
                    {
                        var basicHcMatch = System.Text.RegularExpressions.Regex.Match(oName, @"^(1|X|x|2|1X|X2|12)(?:\s*\(([^)]+)\))?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (basicHcMatch.Success)
                        {
                            string opTarget = basicHcMatch.Groups[1].Value.ToUpperInvariant();
                            if (aiTarget == opTarget)
                            {
                                string hcStr = basicHcMatch.Groups[2].Value.Trim();
                                double? opHc = null;
                                if (!string.IsNullOrEmpty(hcStr))
                                {
                                    if (hcStr.Contains("-") && hcStr.Length > 2)
                                    {
                                        var parts = hcStr.Split('-');
                                        if (parts.Length == 2 && double.TryParse(parts[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double p1) && double.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double p2))
                                        {
                                            opHc = opTarget == "1" ? (p1 - p2) : (opTarget == "2" ? (p2 - p1) : (p1 - p2));
                                        }
                                    }
                                    else if (hcStr.Contains(":"))
                                    {
                                        var parts = hcStr.Split(':');
                                        if (parts.Length == 2 && double.TryParse(parts[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double p1) && double.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double p2))
                                        {
                                            opHc = opTarget == "1" ? (p1 - p2) : (opTarget == "2" ? (p2 - p1) : (p1 - p2));
                                        }
                                    }
                                    else if (double.TryParse(hcStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
                                    {
                                        opHc = val;
                                    }
                                }
                                
                                double? aiHc = null;
                                var aiHcRegex = new System.Text.RegularExpressions.Regex(@"([+-]\d+(?:\.\d+)?)$");
                                var aiHcMatch = aiHcRegex.Match(normSelRaw.Replace(" ", "").Replace("(", "").Replace(")", ""));
                                if (aiHcMatch.Success && double.TryParse(aiHcMatch.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedAiHc))
                                {
                                    aiHc = parsedAiHc;
                                }
                                else if (ActiveLookupLeg?.Market != null)
                                {
                                    var mktRegex = new System.Text.RegularExpressions.Regex(@"(?:starts|\()(\d+[-:]\d+|[+-]\d+(?:\.\d+)?)\)?$");
                                    var mktMatch = mktRegex.Match(ActiveLookupLeg.Market.Replace(" ", "").ToLowerInvariant());
                                    if (mktMatch.Success)
                                    {
                                        string extractedHc = mktMatch.Groups[1].Value;
                                        if (extractedHc.Contains("-") && extractedHc.Length > 2 && extractedHc[0] != '-') // e.g. "0-2"
                                        {
                                            var parts = extractedHc.Split('-');
                                            if (parts.Length == 2 && double.TryParse(parts[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double p1) && double.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double p2))
                                            {
                                                aiHc = aiTarget == "1" ? (p1 - p2) : (aiTarget == "2" ? (p2 - p1) : (p1 - p2));
                                            }
                                        }
                                        else if (extractedHc.Contains(":")) // e.g. "0:2"
                                        {
                                            var parts = extractedHc.Split(':');
                                            if (parts.Length == 2 && double.TryParse(parts[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double p1) && double.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double p2))
                                            {
                                                aiHc = aiTarget == "1" ? (p1 - p2) : (aiTarget == "2" ? (p2 - p1) : (p1 - p2));
                                            }
                                        }
                                        else if (double.TryParse(extractedHc, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedMktHc))
                                        {
                                            aiHc = aiTarget == "1" ? parsedMktHc : (aiTarget == "2" ? -parsedMktHc : parsedMktHc);
                                        }
                                    }
                                }
                                
                                if (opHc.HasValue && aiHc.HasValue && Math.Abs(opHc.Value - aiHc.Value) < 0.001) logicallyMatched = true;
                                else if (!opHc.HasValue && !aiHc.HasValue) logicallyMatched = true;
                            }
                        }
                    }
                }
            }
            
            if (logicallyMatched)
            {
                isMatch = true;
            }
            else
            {
                string rawSel = ActiveLookupLeg?.Selection ?? "";
                rawSel = System.Text.RegularExpressions.Regex.Replace(rawSel, @"(\d+)\+", m => $"over {double.Parse(m.Groups[1].Value) - 0.5}");
                rawSel = System.Text.RegularExpressions.Regex.Replace(rawSel, @"\btie\b", "draw", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                string normOName = System.Text.RegularExpressions.Regex.Replace(BettingApp.Services.TeamAliasMappingService.ApplyTeamAliases(BettingApp.Services.TeamAliasMappingService.RemoveDiacritics(displayOName ?? "").ToLowerInvariant()).Replace(" ", "").Replace("(", "").Replace(")", "").Replace(":", "-"), @"\.0+(?!\d)", "");
                string normSel = System.Text.RegularExpressions.Regex.Replace(BettingApp.Services.TeamAliasMappingService.ApplyTeamAliases(BettingApp.Services.TeamAliasMappingService.RemoveDiacritics(rawSel).ToLowerInvariant()).Replace(" ", "").Replace("(", "").Replace(")", "").Replace(":", "-"), @"\.0+(?!\d)", "");
                
                if (normOName == normSel)
                {
                    isMatch = true;
                }
                else if ((normSel == "yes" || normSel == "no" || normSel == "over05") && !string.IsNullOrEmpty(ActiveLookupLeg?.Market))
                {
                    string normMarket = BettingApp.Services.TeamAliasMappingService.RemoveDiacritics(ActiveLookupLeg.Market).ToLowerInvariant().Replace(" ", "").Replace("toscore", "").Replace("anytimegoalscorer", "");
                    string playerNameFromOdds = normOName.Replace("yes", "").Replace("no", "").Replace("over05", "").Replace("under05", "");
                    if (!string.IsNullOrEmpty(normMarket) && !string.IsNullOrEmpty(playerNameFromOdds) && normMarket.Length > 3 && playerNameFromOdds.Length > 3 && (normMarket.Contains(playerNameFromOdds) || playerNameFromOdds.Contains(normMarket)))
                    {
                        isMatch = true;
                    }
                }
                else
                {
                    var hcRegex = new System.Text.RegularExpressions.Regex(@"([+-]\d+(?:\.\d+)?)$");
                var matchOName = hcRegex.Match(normOName);
                var matchSel = hcRegex.Match(normSel);
                
                if (matchOName.Success && matchSel.Success)
                {
                    if (matchOName.Value == matchSel.Value)
                    {
                        string teamOName = normOName.Substring(0, matchOName.Index);
                        string teamSel = normSel.Substring(0, matchSel.Index);
                        
                        if (!string.IsNullOrEmpty(teamOName) && !string.IsNullOrEmpty(teamSel) && 
                            (teamOName.Contains(teamSel) || teamSel.Contains(teamOName)))
                        {
                            isMatch = true;
                        }
                    }
                }
                else if (!matchOName.Success && !matchSel.Success)
                {
                    var numRegex = new System.Text.RegularExpressions.Regex(@"(\d+(?:\.\d+)?)$");
                    var numOName = numRegex.Match(normOName);
                    var numSel = numRegex.Match(normSel);
                    
                    if (numOName.Success && numSel.Success)
                    {
                        if (numOName.Value == numSel.Value)
                        {
                            string baseOName = normOName.Substring(0, numOName.Index);
                            string baseSel = normSel.Substring(0, numSel.Index);
                            if (!string.IsNullOrEmpty(baseOName) && !string.IsNullOrEmpty(baseSel) && 
                                (baseOName.Contains(baseSel) || baseSel.Contains(baseOName)))
                            {
                                isMatch = true;
                            }
                        }
                    }
                    else if (!numOName.Success && !numSel.Success)
                    {
                        if (normOName.Contains(normSel))
                        {
                            if ((normSel == "yes" || normSel == "no") && normOName.Length > normSel.Length + 3)
                            {
                                string normMarket = BettingApp.Services.TeamAliasMappingService.RemoveDiacritics(ActiveLookupLeg?.Market ?? "").ToLowerInvariant().Replace(" ", "").Replace("toscore", "").Replace("anytimegoalscorer", "");
                                string playerNameFromOdds = normOName.Replace("yes", "").Replace("no", "");
                                if (!string.IsNullOrEmpty(normMarket) && !string.IsNullOrEmpty(playerNameFromOdds) && (normMarket.Contains(playerNameFromOdds) || playerNameFromOdds.Contains(normMarket)))
                                {
                                    isMatch = true;
                                }
                            }
                            else
                            {
                                isMatch = true;
                            }
                        }
                        else if (normSel.Contains(normOName))
                        {
                            if (normOName.Length >= 4)
                            {
                                isMatch = true;
                            }
                            else
                            {
                                if (normSel.EndsWith("-" + normOName) || normSel.StartsWith(normOName + "-") || normSel.Contains("-" + normOName + "-"))
                                {
                                    isMatch = true;
                                }
                            }
                        }
                    }
                    }
                }
                
                if (!isMatch)
                {
                    var parenMatch = System.Text.RegularExpressions.Regex.Match(displayOName ?? "", @"\(([^,()]+),\s*([^()]+)\)");
                    if (parenMatch.Success)
                    {
                        string lastName = BettingApp.Services.TeamAliasMappingService.RemoveDiacritics(parenMatch.Groups[1].Value).ToLowerInvariant().Replace(" ", "");
                        string firstName = BettingApp.Services.TeamAliasMappingService.RemoveDiacritics(parenMatch.Groups[2].Value).ToLowerInvariant().Replace(" ", "");
                        
                        bool matchLast = normSel.Contains(lastName);
                        bool matchFirst = normSel.Contains(firstName);
                        
                        string outcomePart = (displayOName ?? "").Substring(0, parenMatch.Index).Trim();
                        string normOutcomePart = System.Text.RegularExpressions.Regex.Replace(BettingApp.Services.TeamAliasMappingService.RemoveDiacritics(outcomePart).ToLowerInvariant().Replace(" ", "").Replace("(", "").Replace(")", "").Replace(":", "-"), @"\.0+(?!\d)", "");
                        
                        bool isValidNameMatch = matchLast && matchFirst;
                        
                        if (!isValidNameMatch && (ActiveLookupLeg?.Market?.Contains("Goalkeeper Saves", StringComparison.OrdinalIgnoreCase) == true || ActiveLookupLeg?.Market?.Contains("Goal Keeper Saves", StringComparison.OrdinalIgnoreCase) == true))
                        {
                            isValidNameMatch = true;
                        }
                        
                        if (!isValidNameMatch && (matchLast || matchFirst))
                        {
                            string matchedName = matchLast ? lastName : firstName;
                            // Remove everything except the player's name
                            string playerPartOnly = System.Text.RegularExpressions.Regex.Replace(normSel.Replace(normOutcomePart, "").Replace("over", "").Replace("under", "").Replace("yes", "").Replace("no", "").Replace("-", ""), @"\d+(\.\d+)?", "");
                            string leftover = playerPartOnly.Replace(matchedName, "");
                            
                            // If the leftover is long, it means they explicitly typed a DIFFERENT name that contradicts the matched name (e.g. "Martin Satriano" vs "Mario Martin").
                            // Allow a small leftover (<= 5) for abbreviations like "Jr".
                            if (leftover.Length <= 5)
                            {
                                isValidNameMatch = true;
                            }
                        }
                        
                        if (isValidNameMatch)
                        {
                            bool isOutcomeMatch = string.IsNullOrEmpty(normOutcomePart) || 
                                                  normSel.Contains(normOutcomePart) || 
                                                  (normOutcomePart == "yes" && (!normSel.Contains("no") && !normSel.Contains("under")));
                            
                            if (!isOutcomeMatch && normOutcomePart.EndsWith("+"))
                            {
                                if (int.TryParse(normOutcomePart.TrimEnd('+'), out int plusVal))
                                {
                                    string eqOver = $"over{plusVal - 1}.5";
                                    if (normSel.Contains(eqOver) || (plusVal == 1 && (normSel.Contains("assist") || normSel.Contains("score") || normSel.Contains("goal") || normSel.Contains("1or"))))
                                    {
                                        isOutcomeMatch = true;
                                    }
                                }
                            }
                            
                            if (isOutcomeMatch)
                            {
                                isMatch = true;
                            }
                        }
                    }
                    else
                    {
                        var simpleParenMatch = System.Text.RegularExpressions.Regex.Match(displayOName ?? "", @"\(([^()]+)\)$");
                        if (simpleParenMatch.Success)
                        {
                            string pName = BettingApp.Services.TeamAliasMappingService.RemoveDiacritics(simpleParenMatch.Groups[1].Value).ToLowerInvariant().Replace(" ", "");
                            
                            bool isNameCheckBypassed = ActiveLookupLeg?.Market?.Contains("Goalkeeper Saves", StringComparison.OrdinalIgnoreCase) == true || ActiveLookupLeg?.Market?.Contains("Goal Keeper Saves", StringComparison.OrdinalIgnoreCase) == true;
                            
                            if (isNameCheckBypassed || (!double.TryParse(pName, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _) && normSel.Contains(pName)))
                            {
                                string outcomePart = (displayOName ?? "").Substring(0, simpleParenMatch.Index).Trim();
                                string normOutcomePart = System.Text.RegularExpressions.Regex.Replace(BettingApp.Services.TeamAliasMappingService.RemoveDiacritics(outcomePart).ToLowerInvariant().Replace(" ", "").Replace("(", "").Replace(")", "").Replace(":", "-"), @"\.0+(?!\d)", "");
                                
                                bool isOutcomeMatch = string.IsNullOrEmpty(normOutcomePart) || 
                                                      normSel.Contains(normOutcomePart) || 
                                                      (normOutcomePart == "yes" && (!normSel.Contains("no") && !normSel.Contains("under")));
                                
                                if (!isOutcomeMatch && normOutcomePart.EndsWith("+"))
                                {
                                    if (int.TryParse(normOutcomePart.TrimEnd('+'), out int plusVal))
                                    {
                                        string eqOver = $"over{plusVal - 1}.5";
                                        if (normSel.Contains(eqOver) || (plusVal == 1 && (normSel.Contains("assist") || normSel.Contains("score") || normSel.Contains("goal") || normSel.Contains("1or"))))
                                        {
                                            isOutcomeMatch = true;
                                        }
                                    }
                                }
                                
                                if (isOutcomeMatch)
                                {
                                    isMatch = true;
                                }
                            }
                        }
                    }
                }
            }
        }
        
        if (!isMatch && !string.IsNullOrEmpty(displayOName) && ActiveLookupLeg != null && !string.IsNullOrEmpty(ActiveLookupLeg.Selection))
        {
            isMatch = IsFuzzyPlayerMatch(ActiveLookupLeg.Selection, displayOName);
        }
        
        return isMatch;
    }

    public static BettingApp.Models.OddsPapiMarket? FindBestMarket(List<BettingApp.Models.OddsPapiMarket> markets, string marketCategory, string? matchName, string? selectionName, BettingApp.Services.MarketMappingService marketMapper)
    {
        if (markets == null || !markets.Any() || string.IsNullOrEmpty(marketCategory)) return null;
        
        var normalizedTargets = marketMapper.NormalizeMarketName(marketCategory, matchName);
        
        var simplify = (string s) => 
        {
            var clean = s.ToLowerInvariant()
                         .Replace("players", "player")
                         .Replace("player's", "player")
                         .Replace("shots", "shot")
                         .Replace("cards", "card")
                         .Replace("goals", "goal")
                         .Replace("corners", "corner")
                         .Replace("halves", "half");
            return new string(clean.Where(char.IsLetterOrDigit).ToArray());
        };
        
        var simplifiedTargets = normalizedTargets.Select(t => simplify(t)).ToList();
        
        BettingApp.Models.OddsPapiMarket? bestMatch = null;
        foreach (var target in simplifiedTargets)
        {
            bestMatch = markets.FirstOrDefault(m => simplify(m.MarketName) == target);
            if (bestMatch != null) break;
        }
        
        if (bestMatch == null)
        {
            foreach (var target in simplifiedTargets)
            {
                bestMatch = markets.FirstOrDefault(m => simplify(m.MarketName).Contains(target) || target.Contains(simplify(m.MarketName)));
                if (bestMatch != null) break;
            }
        }
        
        if (bestMatch == null && simplifiedTargets.Any(t => t.Contains("teamtotalgoal")))
        {
            if (!string.IsNullOrEmpty(matchName) && !string.IsNullOrEmpty(selectionName))
            {
                var hcSplit = matchName.Split(new[] { " vs ", " v ", " - " }, StringSplitOptions.None);
                if (hcSplit.Length >= 2)
                {
                    string normSelRaw = BettingApp.Services.TeamAliasMappingService.RemoveDiacritics(selectionName).ToLowerInvariant();
                    string normT1 = BettingApp.Services.TeamAliasMappingService.RemoveDiacritics(hcSplit[0].Split('(')[0].Trim()).ToLowerInvariant();
                    string normT2 = BettingApp.Services.TeamAliasMappingService.RemoveDiacritics(hcSplit[1].Split('(')[0].Trim()).ToLowerInvariant();
                    
                    string expectedTeamMarket = "";
                    if (normSelRaw.Contains(normT1)) expectedTeamMarket = simplify("Over Under Team 1");
                    else if (normSelRaw.Contains(normT2)) expectedTeamMarket = simplify("Over Under Team 2");
                    
                    if (!string.IsNullOrEmpty(expectedTeamMarket))
                    {
                        bestMatch = markets.FirstOrDefault(m => simplify(m.MarketName) == expectedTeamMarket) ?? 
                                    markets.FirstOrDefault(m => simplify(m.MarketName).Contains(expectedTeamMarket));
                    }
                }
            }
        }
        
        return bestMatch;
    }

    public static string GetSortKey(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        
        string normalized = input.Replace(" ", "").Replace("(", "").Replace(")", "").Replace("+", "");

        return System.Text.RegularExpressions.Regex.Replace(normalized, @"([-]?\d+(?:\.\d+)?)", m => 
        {
            if (double.TryParse(m.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
            {
                return (val + 100000).ToString("000000.0000", System.Globalization.CultureInfo.InvariantCulture);
            }
            return m.Value;
        });
    }

    }
}
