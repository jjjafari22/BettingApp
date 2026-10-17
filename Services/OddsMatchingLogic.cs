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
                    
                    if (hcStr.Contains("-") && hcStr.Length > 2 && !hcStr.StartsWith("-"))
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
                        if (hcStr.StartsWith("+"))
                        {
                            t1Hc = teamSel == "1" ? val : -val;
                            t2Hc = teamSel == "2" ? val : -val;
                        }
                        else
                        {
                            t1Hc = val;
                            t2Hc = -val;
                        }
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
                    else if (displayOName.Equals("1X", StringComparison.OrdinalIgnoreCase) || displayOName.Equals("X1", StringComparison.OrdinalIgnoreCase)) displayOName = $"{t1} or Draw";
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

    public static bool CheckPlayerInitialsMatch(string firstNameFromOdds, string sel, string lastName)
    {
        if (string.IsNullOrWhiteSpace(firstNameFromOdds) || string.IsNullOrWhiteSpace(sel)) return false;

        string normSel = BettingApp.Services.TeamAliasMappingService.RemoveDiacritics(sel).ToLowerInvariant();
        string cleanFirst = BettingApp.Services.TeamAliasMappingService.RemoveDiacritics(firstNameFromOdds).ToLowerInvariant().Replace(".", "").Replace(" ", "").Replace("-", "");
        
        string cleanSel = System.Text.RegularExpressions.Regex.Replace(normSel, @"\b(over|under|yes|no|total|points?|goals?|assists?|shots?|saves?)\b", " ");
        cleanSel = System.Text.RegularExpressions.Regex.Replace(cleanSel, @"\d+(?:\.\d+)?", " ");
        cleanSel = System.Text.RegularExpressions.Regex.Replace(cleanSel, @"[.+\-/:()"",]", " ");
        
        var words = cleanSel.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                            .Where(w => !w.Equals(lastName, StringComparison.OrdinalIgnoreCase))
                            .ToList();
        
        if (!words.Any()) return false;

        if (words.Any(w => w.Equals(cleanFirst, StringComparison.OrdinalIgnoreCase))) return true;

        string initials = string.Concat(words.Select(w => w[0]));
        if (initials.Equals(cleanFirst, StringComparison.OrdinalIgnoreCase)) return true;
        if (cleanFirst.Length == 1 && words.Any(w => w.StartsWith(cleanFirst, StringComparison.OrdinalIgnoreCase))) return true;
        if (initials.StartsWith(cleanFirst, StringComparison.OrdinalIgnoreCase)) return true;
        if (cleanFirst.StartsWith(initials, StringComparison.OrdinalIgnoreCase) && initials.Length >= 2) return true;

        return false;
    }

    public static bool IsFuzzyPlayerMatch(string str1, string str2)
    {
        if (string.IsNullOrWhiteSpace(str1) || string.IsNullOrWhiteSpace(str2)) return false;
        
        string norm1 = BettingApp.Services.TeamAliasMappingService.RemoveDiacritics(str1).ToLowerInvariant();
        string norm2 = BettingApp.Services.TeamAliasMappingService.RemoveDiacritics(str2).ToLowerInvariant();

        Func<string, string> injectImplicitOdds = (string norm) => {
            norm = System.Text.RegularExpressions.Regex.Replace(norm, @"(\d+)\+", m => $"over {double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) - 0.5}");
            norm = System.Text.RegularExpressions.Regex.Replace(norm, @"(?:to score\s+|to record\s+|to get\s+|to have\s+)?(\d+)\s+or\s+more", m => $"over {double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) - 0.5}");
            norm = System.Text.RegularExpressions.Regex.Replace(norm, @"(?:at\s+least\s+)(\d+)", m => $"over {double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) - 0.5}");

            if (!norm.Contains("over ") && !norm.Contains("under ") && 
                (norm.Contains("to provide") || norm.Contains("to score") || norm.Contains("to record") || norm.Contains("to get") || norm.Contains("anytime") || norm.Contains("carded") || norm.Contains("booked")))
            {
                if (!norm.Contains("not ")) return norm + " over 0.5";
                else return norm + " under 0.5";
            }
            return norm;
        };
        norm1 = injectImplicitOdds(norm1);
        norm2 = injectImplicitOdds(norm2);

        // Handle "power sub" or "super sub" promotions as whole phrases using word boundaries
        // This ensures "Max Power" doesn't lose his name, while "Power Sub" gets ignored.
        norm1 = System.Text.RegularExpressions.Regex.Replace(norm1, @"\b(power sub|super sub)\b", "");
        norm2 = System.Text.RegularExpressions.Regex.Replace(norm2, @"\b(power sub|super sub)\b", "");

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

        var allTokens1 = norm1.Split(new[] { ' ', '-', '.', ',', ':', '/', '+', '(', ')' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        var allTokens2 = norm2.Split(new[] { ' ', '-', '.', ',', ':', '/', '+', '(', ')' }, StringSplitOptions.RemoveEmptyEntries).ToList();

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
        var ignoreWords = new HashSet<string> { "over", "under", "yes", "no", "player", "shots", "target", "score", "anytime", "goalscorer", "fouls", "assists", "assist", "cards", "booked", "carded", "booking", "points", "provide", "make", "have", "to", "be", "shown", "a", "card", "on" };
        
        var tokens1 = allTokens1
            .Where(t => t.Length >= 1 && !ignoreWords.Contains(t) && !double.TryParse(t, out _))
            .ToList();
            
        var tokens2 = allTokens2
            .Where(t => t.Length >= 1 && !ignoreWords.Contains(t) && !double.TryParse(t, out _))
            .ToList();
            
        if (!tokens1.Any() || !tokens2.Any()) return false;
        
        var tokens2List = tokens2.ToList();
        int matchCount = 0;
        foreach (var t1 in tokens1)
        {
            int matchIdx = tokens2List.IndexOf(t1);
            if (matchIdx >= 0)
            {
                matchCount++;
                tokens2List.RemoveAt(matchIdx);
            }
            else if (t1.Length == 1) // Initial
            {
                int initIdx = tokens2List.FindIndex(o => o.StartsWith(t1));
                if (initIdx >= 0)
                {
                    matchCount++;
                    tokens2List.RemoveAt(initIdx);
                }
            }
            else if (t1.Length <= 3 && t1.All(char.IsLetter)) // Multi-initial like "jj"
            {
                var matchingTokens = new List<int>();
                int t1CharIdx = 0;
                for (int j = 0; j < tokens2List.Count && t1CharIdx < t1.Length; j++)
                {
                    if (tokens2List[j].StartsWith(t1[t1CharIdx].ToString()))
                    {
                        matchingTokens.Add(j);
                        t1CharIdx++;
                    }
                }
                if (t1CharIdx == t1.Length)
                {
                    matchCount++;
                    foreach (var idx in matchingTokens.OrderByDescending(x => x))
                    {
                        tokens2List.RemoveAt(idx);
                    }
                }
            }
        }
        if (matchCount == tokens1.Count) return true;
        
        var tokens1List = tokens1.ToList();
        int reverseMatch = 0;
        foreach(var t2 in tokens2)
        {
            int matchIdx = tokens1List.IndexOf(t2);
            if (matchIdx >= 0)
            {
                reverseMatch++;
                tokens1List.RemoveAt(matchIdx);
            }
            else if (t2.Length == 1)
            {
                int initIdx = tokens1List.FindIndex(o => o.StartsWith(t2));
                if (initIdx >= 0)
                {
                    reverseMatch++;
                    tokens1List.RemoveAt(initIdx);
                }
            }
            else if (t2.Length <= 3 && t2.All(char.IsLetter)) // Multi-initial like "jj"
            {
                var matchingTokens = new List<int>();
                int t2CharIdx = 0;
                for (int j = 0; j < tokens1List.Count && t2CharIdx < t2.Length; j++)
                {
                    if (tokens1List[j].StartsWith(t2[t2CharIdx].ToString()))
                    {
                        matchingTokens.Add(j);
                        t2CharIdx++;
                    }
                }
                if (t2CharIdx == t2.Length)
                {
                    reverseMatch++;
                    foreach (var idx in matchingTokens.OrderByDescending(x => x))
                    {
                        tokens1List.RemoveAt(idx);
                    }
                }
            }
        }
        if (reverseMatch == tokens2.Count) return true;
        
        return false;
    }

    private static readonly System.Text.RegularExpressions.Regex _hcSignRegex = new(@"(?:^|\s|\()([+-]\d+(?:\.\d+)?)\)?", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex _parenMatchRegex = new(@"\(([^,()]+),\s*([^()]+)\)", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex _simpleParenMatchRegex = new(@"\(([^()]+)\)$", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex _hcRegex = new(@"([+-]\d+(?:\.\d+)?)$", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex _numRegex = new(@"(\d+(?:\.\d+)?)$", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex _basicHcMatchRegex = new(@"^(1|X|x|2|1X|X1|X2|2X|12)(?:\s*\(([^)]+)\))?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex _htftRegex = new(@"^(1|X|2)/(1|X|2)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex _powerSubRegex = new(@"\b(power sub|super sub)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex _dotZeroRegex = new(@"\.0+(?!\d)", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex _drawTieRegex = new(@"\b(?:draw|tie)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    public class OutcomeMatchingContext
    {
        public BettingApp.Services.AiVisionLeg? Leg { get; }
        public BettingApp.Models.OddsPapiSearchResult? SearchResult { get; }
        public string? OpMarketName { get; }

        public double SelHcVal { get; }
        public bool HasSelHc { get; }

        public string NormSelRaw { get; } = "";
        public string NormSel { get; } = "";
        public string RawSel { get; } = "";
        public string FallbackSel { get; } = "";

        public string AiT1 { get; } = "";
        public string AiT2 { get; } = "";
        public string OpT1 { get; } = "";
        public string OpT2 { get; } = "";
        public bool IsSwapped { get; }
        public string NormT1 { get; } = "";
        public string NormT2 { get; } = "";
        public string TargetOp1 { get; } = "";
        public string TargetOp2 { get; } = "";
        public string AiTarget { get; } = "";
        public string HtftTarget { get; } = "";
        public double? AiHc { get; }

        public string MatchSelVal { get; } = "";
        public bool MatchSelSuccess { get; }
        public int MatchSelIndex { get; }

        public string NumSelVal { get; } = "";
        public bool NumSelSuccess { get; }
        public int NumSelIndex { get; }

        public string NormMarket { get; } = "";
        public string NormOpMarket { get; } = "";
        public string PlayerNameFromAiSel { get; } = "";

        public bool HasHigherLineReq { get; }
        public double? TargetLine { get; }
        public bool WantsUnder { get; }
        public string CleanExpectedOver { get; } = "";
        public string CleanExpectedUnder { get; } = "";

        public OutcomeMatchingContext(BettingApp.Services.AiVisionLeg? leg, BettingApp.Models.OddsPapiSearchResult? searchResult, string? opMarketName = null)
        {
            Leg = leg;
            SearchResult = searchResult;
            OpMarketName = opMarketName;

            if (leg == null || string.IsNullOrEmpty(leg.Selection)) return;

            var mSel = _hcSignRegex.Match(leg.Selection);
            if (mSel.Success && double.TryParse(mSel.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double valS))
            {
                SelHcVal = valS;
                HasSelHc = true;
            }

            NormSelRaw = BettingApp.Services.TeamAliasMappingService.RemoveDiacritics(leg.Selection).ToLowerInvariant();

            if (leg.Match != null)
            {
                var hcSplit = leg.Match.Split(new[] { " vs ", " v ", " - " }, StringSplitOptions.None);
                if (hcSplit.Length >= 2)
                {
                    AiT1 = BettingApp.Services.TeamAliasMappingService.RemoveDiacritics(hcSplit[0].Split('(')[0].Trim()).ToLowerInvariant();
                    AiT2 = BettingApp.Services.TeamAliasMappingService.RemoveDiacritics(hcSplit[1].Split('(')[0].Trim()).ToLowerInvariant();

                    if (searchResult != null && !string.IsNullOrEmpty(searchResult.MatchName))
                    {
                        var opMatchSplit = searchResult.MatchName.Split(new[] { " vs ", " v ", " - " }, StringSplitOptions.None);
                        if (opMatchSplit.Length >= 2)
                        {
                            OpT1 = BettingApp.Services.TeamAliasMappingService.RemoveDiacritics(opMatchSplit[0].Trim()).ToLowerInvariant();
                            OpT2 = BettingApp.Services.TeamAliasMappingService.RemoveDiacritics(opMatchSplit[1].Trim()).ToLowerInvariant();
                        }
                    }

                    if ((OpT2.Contains(AiT1) && AiT1.Length > 3) || (AiT1.Contains(OpT2) && OpT2.Length > 3) ||
                        (OpT1.Contains(AiT2) && AiT2.Length > 3) || (AiT2.Contains(OpT1) && OpT1.Length > 3) ||
                        BettingApp.Services.TeamAliasMappingService.IsTeamMatch(AiT1, OpT2) ||
                        BettingApp.Services.TeamAliasMappingService.IsTeamMatch(AiT2, OpT1))
                    {
                        if (!OpT1.Contains(AiT1) && !AiT1.Contains(OpT1) && !BettingApp.Services.TeamAliasMappingService.IsTeamMatch(AiT1, OpT1))
                        {
                            IsSwapped = true;
                        }
                    }

                    NormT1 = IsSwapped ? AiT2 : AiT1;
                    NormT2 = IsSwapped ? AiT1 : AiT2;
                    TargetOp1 = IsSwapped ? OpT2 : OpT1;
                    TargetOp2 = IsSwapped ? OpT1 : OpT2;

                    if (NormSelRaw.Contains(" / ") || NormSelRaw.Contains(" - "))
                    {
                        var htftSplit = NormSelRaw.Split(new[] { " / ", " - " }, StringSplitOptions.RemoveEmptyEntries);
                        if (htftSplit.Length == 2)
                        {
                            string ht = htftSplit[0].Trim();
                            string ft = htftSplit[1].Trim();
                            int htTeam = BettingApp.Services.TeamAliasMappingService.ResolveTeamSelection(ht, NormT1, NormT2, TargetOp1, TargetOp2);
                            int ftTeam = BettingApp.Services.TeamAliasMappingService.ResolveTeamSelection(ft, NormT1, NormT2, TargetOp1, TargetOp2);
                            string htCode = htTeam == 1 ? "1" : (htTeam == 2 ? "2" : (_drawTieRegex.IsMatch(ht) ? "X" : ""));
                            string ftCode = ftTeam == 1 ? "1" : (ftTeam == 2 ? "2" : (_drawTieRegex.IsMatch(ft) ? "X" : ""));
                            if (!string.IsNullOrEmpty(htCode) && !string.IsNullOrEmpty(ftCode))
                            {
                                HtftTarget = $"{htCode}/{ftCode}";
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(HtftTarget))
                    {
                        int teamResolved = BettingApp.Services.TeamAliasMappingService.ResolveTeamSelection(leg.Selection, NormT1, NormT2, TargetOp1, TargetOp2);
                        bool mentionsT1 = teamResolved == 1 || BettingApp.Services.TeamAliasMappingService.IsTeamMatch(leg.Selection, NormT1) || (!string.IsNullOrEmpty(TargetOp1) && BettingApp.Services.TeamAliasMappingService.IsTeamMatch(leg.Selection, TargetOp1));
                        bool mentionsT2 = teamResolved == 2 || BettingApp.Services.TeamAliasMappingService.IsTeamMatch(leg.Selection, NormT2) || (!string.IsNullOrEmpty(TargetOp2) && BettingApp.Services.TeamAliasMappingService.IsTeamMatch(leg.Selection, TargetOp2));
                        bool hasDraw = System.Text.RegularExpressions.Regex.IsMatch(NormSelRaw, @"\b(?:draw|tie|ties|oavgjort|uavgjort|kryss)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                        if (mentionsT1 && mentionsT2) AiTarget = "12";
                        else if (teamResolved == 1 && hasDraw) AiTarget = "1X";
                        else if (teamResolved == 2 && hasDraw) AiTarget = "X2";
                        else if (teamResolved == 1) AiTarget = "1";
                        else if (teamResolved == 2) AiTarget = "2";
                        else if (hasDraw) AiTarget = "X";
                        else if (NormSelRaw.Contains("1x")) AiTarget = "1X";
                        else if (NormSelRaw.Contains("x2") || NormSelRaw.Contains("2x")) AiTarget = "X2";
                        else if (NormSelRaw.Contains("12")) AiTarget = "12";
                    }

                    var aiHcMatch = _hcRegex.Match(NormSelRaw.Replace(" ", "").Replace("(", "").Replace(")", ""));
                    if (aiHcMatch.Success && double.TryParse(aiHcMatch.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedAiHc))
                    {
                        AiHc = parsedAiHc;
                    }
                    else if (leg.Market != null)
                    {
                        var mktRegex = new System.Text.RegularExpressions.Regex(@"(?:starts|\()(\d+[-:]\d+|[+-]\d+(?:\.\d+)?)\)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        var mktMatch = mktRegex.Match(leg.Market.Replace(" ", "").ToLowerInvariant());
                        if (mktMatch.Success)
                        {
                            string extractedHc = mktMatch.Groups[1].Value;
                            if (extractedHc.Contains("-") && extractedHc.Length > 2 && extractedHc[0] != '-')
                            {
                                var parts = extractedHc.Split('-');
                                if (parts.Length == 2 && double.TryParse(parts[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double p1) && double.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double p2))
                            {
                                AiHc = AiTarget == "1" ? (p1 - p2) : (AiTarget == "2" ? (p2 - p1) : (p1 - p2));
                            }
                        }
                        else if (extractedHc.Contains(":"))
                        {
                            var parts = extractedHc.Split(':');
                            if (parts.Length == 2 && double.TryParse(parts[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double p1) && double.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double p2))
                            {
                                AiHc = AiTarget == "1" ? (p1 - p2) : (AiTarget == "2" ? (p2 - p1) : (p1 - p2));
                            }
                        }
                        else if (double.TryParse(extractedHc, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedMktHc))
                        {
                            AiHc = AiTarget == "1" ? parsedMktHc : (AiTarget == "2" ? -parsedMktHc : parsedMktHc);
                        }
                    }
                }
            }
        }

        string rawSel = leg.Selection;
        rawSel = System.Text.RegularExpressions.Regex.Replace(rawSel, @"(\d+)\+", m => $"over {double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) - 0.5}");
        rawSel = System.Text.RegularExpressions.Regex.Replace(rawSel, @"\btie\b", "draw", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        rawSel = _powerSubRegex.Replace(rawSel, "");
        RawSel = rawSel;

        NormSel = _dotZeroRegex.Replace(BettingApp.Services.TeamAliasMappingService.StripToAlphaNumeric(rawSel), "");

        var mSelHc = _hcRegex.Match(NormSel);
        if (mSelHc.Success)
        {
            MatchSelSuccess = true;
            MatchSelVal = mSelHc.Value;
            MatchSelIndex = mSelHc.Index;
        }

        var mSelNum = _numRegex.Match(NormSel);
        if (mSelNum.Success)
        {
            NumSelSuccess = true;
            NumSelVal = mSelNum.Value;
            NumSelIndex = mSelNum.Index;
        }

        if (!string.IsNullOrEmpty(leg.Market))
        {
            NormMarket = BettingApp.Services.TeamAliasMappingService.RemoveDiacritics(leg.Market).ToLowerInvariant().Replace(" ", "").Replace("toscore", "").Replace("anytimegoalscorer", "");
        }

        if (!string.IsNullOrEmpty(opMarketName))
        {
            NormOpMarket = BettingApp.Services.TeamAliasMappingService.RemoveDiacritics(opMarketName).ToLowerInvariant().Replace(" ", "").Replace("toscore", "").Replace("anytimegoalscorer", "");
        }

        PlayerNameFromAiSel = NormSel.Replace("yes", "").Replace("no", "").Replace("over05", "").Replace("under05", "").Replace("toprovideanassist", "").Replace("powersub", "").Replace("-", "");

        string fb = (string.IsNullOrWhiteSpace(leg.Market) ? "" : leg.Market + " ") + leg.Selection;
        fb = System.Text.RegularExpressions.Regex.Replace(fb, @"(\d+)\+", m => $"over {double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) - 0.5}");
        fb = System.Text.RegularExpressions.Regex.Replace(fb, @"(?:to score\s+|to record\s+|to get\s+|to have\s+)?(\d+)\s+or\s+more", m => $"over {double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) - 0.5}");
        fb = System.Text.RegularExpressions.Regex.Replace(fb, @"(?:at\s+least\s+)(\d+)", m => $"over {double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) - 0.5}");
        FallbackSel = fb;

        string fullLegText = $"{(leg.Market ?? "")} {(leg.Selection ?? "")}";
        HasHigherLineReq = System.Text.RegularExpressions.Regex.IsMatch(
            fullLegText,
            @"(?:\bat\s+least\s+([2-9]|\d{2,})\b|\b([2-9]|\d{2,})\s*(?:\+(?!\d)|or\s+more\b|\s*(?:points?|goals?|assists?|shots?|saves?|cards?|tackles?|fouls?)\b)|\bover\s*(?:0\.[6-9]|[1-9]|\d{2,}|[1-9]\.\d+)\b)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Direction (Over vs Under): Selection ALWAYS takes precedence over market category!
        bool selHasUnder = System.Text.RegularExpressions.Regex.IsMatch(NormSelRaw, @"\b(?:under|no|nei|not)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        bool selHasOver = System.Text.RegularExpressions.Regex.IsMatch(NormSelRaw, @"\b(?:over|yes|ja)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (selHasUnder && !selHasOver)
        {
            WantsUnder = true;
        }
        else if (selHasOver)
        {
            WantsUnder = false;
        }
        else
        {
            string mkt = leg.Market ?? "";
            bool isTwoWayMarket = System.Text.RegularExpressions.Regex.IsMatch(mkt, @"\b(?:over\s*[\/-]\s*under|under\s*[\/-]\s*over|o\/u)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
                                  (mkt.Contains("over", StringComparison.OrdinalIgnoreCase) && mkt.Contains("under", StringComparison.OrdinalIgnoreCase));

            if (!isTwoWayMarket && System.Text.RegularExpressions.Regex.IsMatch(mkt, @"\b(?:under|no|nei|not)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                WantsUnder = true;
            }
            else
            {
                WantsUnder = false;
            }
        }

        // Target Line: prefer Selection line over Market line
        var selLineMatch = System.Text.RegularExpressions.Regex.Match(
            leg.Selection ?? "",
            @"(?:\b(?:over|under)\s*(\d+(?:\.\d+)?)\b|\bat\s+least\s+(\d+(?:\.\d+)?)\b|\b(\d+(?:\.\d+)?)\s*(?:\+(?!\d)|or\s+more\b))",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        var mktLineMatch = System.Text.RegularExpressions.Regex.Match(
            leg.Market ?? "",
            @"(?:\b(?:over|under)\s*(\d+(?:\.\d+)?)\b|\bat\s+least\s+(\d+(?:\.\d+)?)\b|\b(\d+(?:\.\d+)?)\s*(?:\+(?!\d)|or\s+more\b))",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        var activeLineMatch = selLineMatch.Success ? selLineMatch : mktLineMatch;
        if (activeLineMatch.Success)
        {
            string valStr = !string.IsNullOrEmpty(activeLineMatch.Groups[1].Value) ? activeLineMatch.Groups[1].Value :
                            (!string.IsNullOrEmpty(activeLineMatch.Groups[2].Value) ? activeLineMatch.Groups[2].Value : activeLineMatch.Groups[3].Value);

            if (double.TryParse(valStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedVal))
            {
                if (!string.IsNullOrEmpty(activeLineMatch.Groups[1].Value))
                {
                    TargetLine = parsedVal;
                }
                else
                {
                    TargetLine = parsedVal >= 1 ? parsedVal - 0.5 : parsedVal;
                }
            }
        }
        else
        {
            var fallbackLineMatch = System.Text.RegularExpressions.Regex.Match(
                $"{(leg.Selection ?? "")} {(leg.Market ?? "")}",
                @"(?:\b(?:total\s+goals?|total|goals?|mål|mal|o\/u|line)\s*(\d+(?:\.\d+)?)\b|\b(\d+(?:\.\d+)?)\s*(?:goals?|mål|mal)\b|\b(\d+\.\d+)\b)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (fallbackLineMatch.Success)
            {
                string valStr = !string.IsNullOrEmpty(fallbackLineMatch.Groups[1].Value) ? fallbackLineMatch.Groups[1].Value :
                                (!string.IsNullOrEmpty(fallbackLineMatch.Groups[2].Value) ? fallbackLineMatch.Groups[2].Value : fallbackLineMatch.Groups[3].Value);

                if (double.TryParse(valStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedVal))
                {
                    TargetLine = parsedVal;
                }
            }
        }

        if (TargetLine.HasValue)
        {
            string cleanTarget = TargetLine.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            CleanExpectedOver = $"over{cleanTarget}";
            CleanExpectedUnder = $"under{cleanTarget}";
        }
    }
}

    public static bool IsOutcomeMatch(string oName, string displayOName, OutcomeMatchingContext ctx)
    {
        if (ctx.Leg == null || string.IsNullOrEmpty(ctx.Leg.Selection)) return false;

        if (ctx.HasSelHc)
        {
            var mOName = _hcSignRegex.Match(displayOName ?? "");
            if (mOName.Success && double.TryParse(mOName.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double valO))
            {
                if (Math.Abs(valO - ctx.SelHcVal) > 0.001)
                {
                    return false;
                }
            }
        }

        bool isMatch = false;
        bool logicallyMatched = false;

        if (!string.IsNullOrEmpty(ctx.HtftTarget) && string.Equals(oName, ctx.HtftTarget, StringComparison.OrdinalIgnoreCase))
        {
            logicallyMatched = true;
        }
        else if (oName.Contains("/") && ctx.NormT1.Length > 0)
        {
            string normONameRaw = BettingApp.Services.TeamAliasMappingService.RemoveDiacritics(oName).ToLowerInvariant();
            if (normONameRaw.Contains(" / ") || normONameRaw.Contains(" - "))
            {
                var opHtftSplit = normONameRaw.Split(new[] { " / ", " - " }, StringSplitOptions.RemoveEmptyEntries);
                if (opHtftSplit.Length == 2)
                {
                    string ht = opHtftSplit[0].Trim();
                    string ft = opHtftSplit[1].Trim();
                    int htTeam = BettingApp.Services.TeamAliasMappingService.ResolveTeamSelection(ht, ctx.NormT1, ctx.NormT2, ctx.TargetOp1, ctx.TargetOp2);
                    int ftTeam = BettingApp.Services.TeamAliasMappingService.ResolveTeamSelection(ft, ctx.NormT1, ctx.NormT2, ctx.TargetOp1, ctx.TargetOp2);
                    string htCode = (ht == "1" || ht == "2" || ht == "x") ? ht.ToUpper() : (htTeam == 1 ? "1" : (htTeam == 2 ? "2" : (_drawTieRegex.IsMatch(ht) ? "X" : "")));
                    string ftCode = (ft == "1" || ft == "2" || ft == "x") ? ft.ToUpper() : (ftTeam == 1 ? "1" : (ftTeam == 2 ? "2" : (_drawTieRegex.IsMatch(ft) ? "X" : "")));
                    if (!string.IsNullOrEmpty(htCode) && !string.IsNullOrEmpty(ftCode) && $"{htCode}/{ftCode}" == ctx.HtftTarget)
                    {
                        logicallyMatched = true;
                    }
                }
            }
            else if (_htftRegex.IsMatch(oName) && string.Equals(oName, ctx.HtftTarget, StringComparison.OrdinalIgnoreCase))
            {
                logicallyMatched = true;
            }
        }

        if (!logicallyMatched && !string.IsNullOrEmpty(ctx.AiTarget))
        {
            var basicHcMatch = _basicHcMatchRegex.Match(oName);
            if (basicHcMatch.Success)
            {
                string opTarget = basicHcMatch.Groups[1].Value.ToUpperInvariant();
                if (opTarget == "2X") opTarget = "X2";
                if (opTarget == "X1") opTarget = "1X";
                string normAiTarget = ctx.AiTarget;
                if (normAiTarget == "2X") normAiTarget = "X2";
                if (normAiTarget == "X1") normAiTarget = "1X";

                if (normAiTarget == opTarget)
                {
                    string hcStr = basicHcMatch.Groups[2].Value.Trim();
                    double? opHc = null;
                    if (!string.IsNullOrEmpty(hcStr))
                    {
                        if (double.TryParse(hcStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
                        {
                            opHc = hcStr.StartsWith("+") ? val : (opTarget == "2" ? -val : val);
                        }
                        else if (hcStr.Contains("-") && !hcStr.StartsWith("-"))
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
                    }

                    if (opHc.HasValue && ctx.AiHc.HasValue && Math.Abs(opHc.Value - ctx.AiHc.Value) < 0.001) logicallyMatched = true;
                    else if (!opHc.HasValue && !ctx.AiHc.HasValue) logicallyMatched = true;
                }
            }
        }

        if (logicallyMatched)
        {
            isMatch = true;
        }
        else
        {
            if (IsPureOverUnderOutcomeMatch(oName, displayOName, ctx))
            {
                return true;
            }

            string displayONameSafe = _powerSubRegex.Replace(displayOName ?? "", "");
            string normOName = _dotZeroRegex.Replace(BettingApp.Services.TeamAliasMappingService.StripToAlphaNumeric(displayONameSafe), "");
            bool isLineSel = ctx.NormSel.StartsWith("over") || ctx.NormSel.StartsWith("under") || normOName.StartsWith("over") || normOName.StartsWith("under");

            if (normOName == ctx.NormSel || (!isLineSel && !string.IsNullOrEmpty(normOName) && !string.IsNullOrEmpty(ctx.NormSel) && BettingApp.Services.TeamAliasMappingService.IsTeamMatch(normOName, ctx.NormSel)))
            {
                isMatch = true;
            }
            else if ((ctx.NormSel == "yes" || ctx.NormSel == "no" || ctx.NormSel == "over05") && !string.IsNullOrEmpty(ctx.NormMarket))
            {
                string playerNameFromOdds = normOName.Replace("yes", "").Replace("no", "").Replace("over05", "").Replace("under05", "");
                if (playerNameFromOdds.Length > 3 && (ctx.NormMarket.Contains(playerNameFromOdds) || playerNameFromOdds.Contains(ctx.NormMarket)))
                {
                    isMatch = true;
                }
            }
            else if ((normOName == "yes" || normOName == "over05") && !string.IsNullOrEmpty(ctx.NormOpMarket))
            {
                if (ctx.PlayerNameFromAiSel.Length > 3 && (ctx.NormOpMarket.Contains(ctx.PlayerNameFromAiSel) || ctx.PlayerNameFromAiSel.Contains(ctx.NormOpMarket) || IsFuzzyPlayerMatch(ctx.OpMarketName ?? "", ctx.Leg?.Selection ?? "")))
                {
                    isMatch = true;
                }
            }
            else
            {
                var matchOName = _hcRegex.Match(normOName);
                if (matchOName.Success && ctx.MatchSelSuccess)
                {
                    if (matchOName.Value == ctx.MatchSelVal)
                    {
                        string teamOName = normOName.Substring(0, matchOName.Index);
                        string teamSel = ctx.NormSel.Substring(0, ctx.MatchSelIndex);
                        if (!string.IsNullOrEmpty(teamOName) && !string.IsNullOrEmpty(teamSel) &&
                            (teamOName.Contains(teamSel) || teamSel.Contains(teamOName) || BettingApp.Services.TeamAliasMappingService.IsTeamMatch(teamOName, teamSel)))
                        {
                            isMatch = true;
                        }
                    }
                }
                else if (!matchOName.Success && !ctx.MatchSelSuccess)
                {
                    var numOName = _numRegex.Match(normOName);
                    if (numOName.Success && ctx.NumSelSuccess)
                    {
                        if (numOName.Value == ctx.NumSelVal)
                        {
                            string baseOName = normOName.Substring(0, numOName.Index);
                            string baseSel = ctx.NormSel.Substring(0, ctx.NumSelIndex);
                            if (!string.IsNullOrEmpty(baseOName) && !string.IsNullOrEmpty(baseSel))
                            {
                                if (baseOName == "over" || baseOName == "under" || baseSel == "over" || baseSel == "under")
                                {
                                    if (baseOName == baseSel)
                                    {
                                        isMatch = true;
                                    }
                                }
                                else if (baseOName.Contains(baseSel) || baseSel.Contains(baseOName) || BettingApp.Services.TeamAliasMappingService.IsTeamMatch(baseOName, baseSel))
                                {
                                    isMatch = true;
                                }
                            }
                        }
                    }
                    else if (!numOName.Success && !ctx.NumSelSuccess)
                    {
                        if (normOName.Contains(ctx.NormSel))
                        {
                            if ((ctx.NormSel == "yes" || ctx.NormSel == "no") && normOName.Length > ctx.NormSel.Length + 3)
                            {
                                string playerNameFromOdds = normOName.Replace("yes", "").Replace("no", "");
                                if (!string.IsNullOrEmpty(ctx.NormMarket) && !string.IsNullOrEmpty(playerNameFromOdds) && (ctx.NormMarket.Contains(playerNameFromOdds) || playerNameFromOdds.Contains(ctx.NormMarket)))
                                {
                                    isMatch = true;
                                }
                            }
                            else
                            {
                                isMatch = true;
                            }
                        }
                        else if (ctx.NormSel.Contains(normOName))
                        {
                            if (normOName.StartsWith("over") || normOName.StartsWith("under"))
                            {
                                // Pure line outcome must not match a player prop via substring
                            }
                            else if (normOName.Length >= 4)
                            {
                                isMatch = true;
                            }
                            else
                            {
                                if (ctx.NormSel.EndsWith("-" + normOName) || ctx.NormSel.StartsWith(normOName + "-") || ctx.NormSel.Contains("-" + normOName + "-"))
                                {
                                    isMatch = true;
                                }
                            }
                        }
                    }
                    else if (IsFuzzyPlayerMatch(displayOName ?? "", string.IsNullOrWhiteSpace(ctx.Leg?.Market) ? (ctx.Leg?.Selection ?? "") : $"{ctx.Leg.Market} {ctx.Leg.Selection}"))
                    {
                        isMatch = true;
                    }
                }
            }

            if (!isMatch)
            {
                var parenMatch = _parenMatchRegex.Match(displayOName ?? "");
                if (parenMatch.Success)
                {
                    string lastName = BettingApp.Services.TeamAliasMappingService.RemoveDiacritics(parenMatch.Groups[1].Value).ToLowerInvariant().Replace(" ", "");
                    string firstName = BettingApp.Services.TeamAliasMappingService.RemoveDiacritics(parenMatch.Groups[2].Value).ToLowerInvariant().Replace(" ", "");

                    bool matchLast = ctx.NormSel.Contains(lastName);
                    bool matchFirst = ctx.NormSel.Contains(firstName);

                    if (!matchFirst && matchLast)
                    {
                        matchFirst = CheckPlayerInitialsMatch(firstName, ctx.Leg?.Selection ?? "", lastName);
                    }
                    else if (!matchLast && matchFirst)
                    {
                        matchLast = CheckPlayerInitialsMatch(lastName, ctx.Leg?.Selection ?? "", firstName);
                    }

                    string outcomePart = (displayOName ?? "").Substring(0, parenMatch.Index).Trim();
                    string normOutcomePart = _dotZeroRegex.Replace(BettingApp.Services.TeamAliasMappingService.RemoveDiacritics(outcomePart).ToLowerInvariant().Replace(" ", "").Replace("(", "").Replace(")", "").Replace(":", "-"), "");

                    bool isValidNameMatch = matchLast && matchFirst;

                    if (!isValidNameMatch && (matchLast || matchFirst))
                    {
                        string matchedName = matchLast ? lastName : firstName;
                        string playerPartOnly = System.Text.RegularExpressions.Regex.Replace(ctx.NormSel.Replace(normOutcomePart, "").Replace("over", "").Replace("under", "").Replace("yes", "").Replace("no", "").Replace("-", ""), @"\d+(\.\d+)?", "");
                        string leftover = playerPartOnly.Replace(matchedName, "");
                        if (leftover.Length <= 5)
                        {
                            isValidNameMatch = true;
                        }
                    }

                    if (isValidNameMatch)
                    {
                        if (IsPlayerPropOutcomeMatchFast(normOutcomePart, ctx))
                        {
                            isMatch = true;
                        }
                    }
                }
                else
                {
                    var simpleParenMatch = _simpleParenMatchRegex.Match(displayOName ?? "");
                    if (simpleParenMatch.Success)
                    {
                        string pName = BettingApp.Services.TeamAliasMappingService.RemoveDiacritics(simpleParenMatch.Groups[1].Value).ToLowerInvariant().Replace(" ", "");

                        if (!double.TryParse(pName, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _) && ctx.NormSel.Contains(pName))
                        {
                            string outcomePart = (displayOName ?? "").Substring(0, simpleParenMatch.Index).Trim();
                            string normOutcomePart = _dotZeroRegex.Replace(BettingApp.Services.TeamAliasMappingService.RemoveDiacritics(outcomePart).ToLowerInvariant().Replace(" ", "").Replace("(", "").Replace(")", "").Replace(":", "-"), "");

                            if (IsPlayerPropOutcomeMatchFast(normOutcomePart, ctx))
                            {
                                isMatch = true;
                            }
                        }
                    }
                }
            }
        }

        if (!isMatch && !string.IsNullOrEmpty(displayOName) && ctx.Leg != null && !string.IsNullOrEmpty(ctx.Leg.Selection))
        {
            isMatch = IsFuzzyPlayerMatch(ctx.FallbackSel, displayOName);
        }

        return isMatch;
    }

    private static bool IsPureOverUnderOutcomeMatch(string oName, string? displayOName, OutcomeMatchingContext ctx)
    {
        if (!ctx.TargetLine.HasValue) return false;

        // If outcome has player parentheses (LastName, FirstName), it's a player prop, not a pure total
        if (_parenMatchRegex.IsMatch(displayOName ?? "")) return false;

        var simpleParen = _simpleParenMatchRegex.Match(displayOName ?? "");
        if (simpleParen.Success && !System.Text.RegularExpressions.Regex.IsMatch(simpleParen.Groups[1].Value, @"^\d+:\d+$|^\d+-\d+$"))
        {
            if (!double.TryParse(simpleParen.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                return false;
            }
        }

        // Check if selection has a player name
        // Strip common total words, numbers, and punctuation
        string selRemaining = ctx.NormSelRaw ?? "";
        selRemaining = System.Text.RegularExpressions.Regex.Replace(selRemaining, @"\b(?:over|under|o\/u|goals?|mål|mal|scoringer|points?|poeng|pts|cards?|kort|corners?|hjørnespark|hjornespark|shots?|skudd|saves?|redninger|tackles?|taklinger|fouls?|frispark|runs?|games?|sets?|innings?|frames?|power\s*sub|super\s*sub|total|totalt|antall|full\s*time|ft|regular\s*time|incl|overtime|penalties|ot|at\s+least|or\s+more)\b", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        selRemaining = System.Text.RegularExpressions.Regex.Replace(selRemaining, @"\d+(?:\.\d+)?", " ");
        selRemaining = System.Text.RegularExpressions.Regex.Replace(selRemaining, @"[^\w\s]", " ");
        if (!string.IsNullOrEmpty(ctx.NormT1)) selRemaining = System.Text.RegularExpressions.Regex.Replace(selRemaining, $@"\b{System.Text.RegularExpressions.Regex.Escape(ctx.NormT1)}\b", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!string.IsNullOrEmpty(ctx.NormT2)) selRemaining = System.Text.RegularExpressions.Regex.Replace(selRemaining, $@"\b{System.Text.RegularExpressions.Regex.Escape(ctx.NormT2)}\b", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        selRemaining = System.Text.RegularExpressions.Regex.Replace(selRemaining, @"\s+", "").Trim();

        // If there's still text left in the selection (e.g. player name like "kiernandewsburyhall"), don't match as pure total
        if (selRemaining.Length > 2) return false;

        // Check if outcome has combo words like " and " or " & "
        string dispLower = (displayOName ?? "").ToLowerInvariant();
        if (dispLower.Contains(" and ") || dispLower.Contains(" & ") || dispLower.Contains(" og ")) return false;

        // Team alignment check for Team 1 / Team 2 markets
        string combinedLeg = $"{(ctx.Leg?.Market ?? "")} {(ctx.Leg?.Selection ?? "")}";
        int legTeam = BettingApp.Services.TeamAliasMappingService.ResolveTeamSelection(combinedLeg, ctx.NormT1, ctx.NormT2, ctx.TargetOp1, ctx.TargetOp2);
        string normOp = ctx.NormOpMarket ?? "";
        if (legTeam == 1 && normOp.Contains("team2")) return false;
        if (legTeam == 2 && normOp.Contains("team1")) return false;

        // Extract line and direction from outcome
        var opMatch = System.Text.RegularExpressions.Regex.Match(displayOName ?? "", @"\b(over|under)\s*(\d+(?:\.\d+)?)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!opMatch.Success && !string.IsNullOrEmpty(oName))
        {
            opMatch = System.Text.RegularExpressions.Regex.Match(oName, @"\b(over|under)\s*(\d+(?:\.\d+)?)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        if (!opMatch.Success) return false;

        bool opIsUnder = opMatch.Groups[1].Value.Equals("under", StringComparison.OrdinalIgnoreCase);
        if (opIsUnder != ctx.WantsUnder) return false;

        if (double.TryParse(opMatch.Groups[2].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double opLine))
        {
            return Math.Abs(opLine - ctx.TargetLine.Value) < 0.001;
        }

        return false;
    }

    private static bool IsPlayerPropOutcomeMatchFast(string normOutcomePart, OutcomeMatchingContext ctx)
    {
        bool isOutcomeMatch = false;

        if (ctx.TargetLine.HasValue)
        {
            string cleanOutcome = normOutcomePart.Replace(" ", "");
            bool isUnder = cleanOutcome.Contains("under") || cleanOutcome.Contains("no");
            if (isUnder == ctx.WantsUnder)
            {
                if (ctx.WantsUnder)
                {
                    isOutcomeMatch = cleanOutcome == ctx.CleanExpectedUnder || cleanOutcome.Replace(".", "") == ctx.CleanExpectedUnder.Replace(".", "");
                }
                else
                {
                    isOutcomeMatch = cleanOutcome == ctx.CleanExpectedOver || cleanOutcome.Replace(".", "") == ctx.CleanExpectedOver.Replace(".", "");
                }

                if (!isOutcomeMatch)
                {
                    var numMatch = System.Text.RegularExpressions.Regex.Match(cleanOutcome, @"\d+(?:\.\d+)?");
                    if (numMatch.Success && double.TryParse(numMatch.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedOutcomeLine))
                    {
                        if (Math.Abs(parsedOutcomeLine - ctx.TargetLine.Value) < 0.001)
                        {
                            isOutcomeMatch = true;
                        }
                    }
                }
            }
        }
        else
        {
            isOutcomeMatch = string.IsNullOrEmpty(normOutcomePart) || 
                              ctx.NormSel.Contains(normOutcomePart) || 
                              (normOutcomePart == "yes" && (!ctx.NormSel.Contains("no") && !ctx.NormSel.Contains("under"))) ||
                              (normOutcomePart == "over0.5" && ctx.NormSel.Contains("yes") && !ctx.NormSel.Contains("no") && !ctx.NormSel.Contains("under") && !ctx.HasHigherLineReq);
        }

        if (!isOutcomeMatch && normOutcomePart.EndsWith("+"))
        {
            if (int.TryParse(normOutcomePart.TrimEnd('+'), out int plusVal))
            {
                double eqLine = plusVal >= 1 ? plusVal - 0.5 : plusVal;
                if (ctx.TargetLine.HasValue && Math.Abs(ctx.TargetLine.Value - eqLine) < 0.001)
                {
                    isOutcomeMatch = true;
                }
                else
                {
                    string eqOver = $"over{plusVal - 1}.5";
                    if (ctx.NormSel.Contains(eqOver) || (plusVal == 1 && (ctx.NormSel.Contains("assist") || ctx.NormSel.Contains("score") || ctx.NormSel.Contains("goal") || ctx.NormSel.Contains("point") || ctx.NormSel.Contains("1or"))))
                    {
                        isOutcomeMatch = true;
                    }
                }
            }
        }

        return isOutcomeMatch;
    }

    public static bool IsOutcomeMatch(string oName, string displayOName, BettingApp.Services.AiVisionLeg? ActiveLookupLeg, BettingApp.Models.OddsPapiSearchResult? PapiSearchResult, string? opMarketName = null)
    {
        var ctx = new OutcomeMatchingContext(ActiveLookupLeg, PapiSearchResult, opMarketName);
        return IsOutcomeMatch(oName, displayOName, ctx);
    }

#if false
    private static bool LegacyIsOutcomeMatch(string oName, string displayOName, BettingApp.Services.AiVisionLeg? ActiveLookupLeg, BettingApp.Models.OddsPapiSearchResult? PapiSearchResult, string? opMarketName = null)
    {
        bool isMatch = false;
        if (ActiveLookupLeg != null && !string.IsNullOrEmpty(ActiveLookupLeg.Selection))
        {
            var hcSignRegex = new System.Text.RegularExpressions.Regex(@"(?:^|\s|\()([+-]\d+(?:\.\d+)?)\)?");
            var mOName = hcSignRegex.Match(displayOName ?? "");
            var mSel = hcSignRegex.Match(ActiveLookupLeg.Selection);
            if (mOName.Success && mSel.Success &&
                double.TryParse(mOName.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double valO) &&
                double.TryParse(mSel.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double valS))
            {
                if (Math.Abs(valO - valS) > 0.001)
                {
                    return false;
                }
            }

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
                        (opT1.Contains(aiT2) && aiT2.Length > 3) || (aiT2.Contains(opT1) && opT1.Length > 3) ||
                        BettingApp.Services.TeamAliasMappingService.IsTeamMatch(aiT1, opT2) ||
                        BettingApp.Services.TeamAliasMappingService.IsTeamMatch(aiT2, opT1))
                    {
                        // Ensure it's not just a generic match like "fc"
                        if (!opT1.Contains(aiT1) && !aiT1.Contains(opT1) && !BettingApp.Services.TeamAliasMappingService.IsTeamMatch(aiT1, opT1))
                        {
                            isSwapped = true;
                        }
                    }
                    
                    // normT1 represents the team that corresponds to Odds Papi's outcome "1".
                    string normT1 = isSwapped ? aiT2 : aiT1;
                    string normT2 = isSwapped ? aiT1 : aiT2;
                    string targetOp1 = isSwapped ? opT2 : opT1;
                    string targetOp2 = isSwapped ? opT1 : opT2;
                    
                    string aiTarget = "";
                    string htftTarget = "";
                    string opHtftTarget = "";
                    
                    if (normSelRaw.Contains(" / ") || normSelRaw.Contains(" - "))
                    {
                        var htftSplit = normSelRaw.Split(new[] { " / ", " - " }, StringSplitOptions.RemoveEmptyEntries);
                        if (htftSplit.Length == 2)
                        {
                            string ht = htftSplit[0].Trim();
                            string ft = htftSplit[1].Trim();
                            int htTeam = BettingApp.Services.TeamAliasMappingService.ResolveTeamSelection(ht, normT1, normT2, targetOp1, targetOp2);
                            int ftTeam = BettingApp.Services.TeamAliasMappingService.ResolveTeamSelection(ft, normT1, normT2, targetOp1, targetOp2);
                            string htCode = htTeam == 1 ? "1" : (htTeam == 2 ? "2" : (System.Text.RegularExpressions.Regex.IsMatch(ht, @"\b(?:draw|tie)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ? "X" : ""));
                            string ftCode = ftTeam == 1 ? "1" : (ftTeam == 2 ? "2" : (System.Text.RegularExpressions.Regex.IsMatch(ft, @"\b(?:draw|tie)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ? "X" : ""));
                            if (!string.IsNullOrEmpty(htCode) && !string.IsNullOrEmpty(ftCode))
                            {
                                htftTarget = $"{htCode}/{ftCode}";
                            }
                        }
                    }

                    string normONameRaw = BettingApp.Services.TeamAliasMappingService.RemoveDiacritics(oName).ToLowerInvariant();
                    if (normONameRaw.Contains(" / ") || normONameRaw.Contains(" - "))
                    {
                        var opHtftSplit = normONameRaw.Split(new[] { " / ", " - " }, StringSplitOptions.RemoveEmptyEntries);
                        if (opHtftSplit.Length == 2)
                        {
                            string ht = opHtftSplit[0].Trim();
                            string ft = opHtftSplit[1].Trim();
                            int htTeam = BettingApp.Services.TeamAliasMappingService.ResolveTeamSelection(ht, normT1, normT2, targetOp1, targetOp2);
                            int ftTeam = BettingApp.Services.TeamAliasMappingService.ResolveTeamSelection(ft, normT1, normT2, targetOp1, targetOp2);
                            
                            string htCode = (ht == "1" || ht == "2" || ht == "x") ? ht.ToUpper() : (htTeam == 1 ? "1" : (htTeam == 2 ? "2" : (System.Text.RegularExpressions.Regex.IsMatch(ht, @"\b(?:draw|tie)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ? "X" : "")));
                            string ftCode = (ft == "1" || ft == "2" || ft == "x") ? ft.ToUpper() : (ftTeam == 1 ? "1" : (ftTeam == 2 ? "2" : (System.Text.RegularExpressions.Regex.IsMatch(ft, @"\b(?:draw|tie)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ? "X" : "")));
                            
                            if (!string.IsNullOrEmpty(htCode) && !string.IsNullOrEmpty(ftCode))
                            {
                                opHtftTarget = $"{htCode}/{ftCode}";
                            }
                        }
                    }
                    else if (System.Text.RegularExpressions.Regex.IsMatch(oName, @"^(1|X|2)/(1|X|2)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    {
                        opHtftTarget = oName.ToUpperInvariant();
                    }
                    
                    if (string.IsNullOrEmpty(htftTarget))
                    {
                        int teamResolved = BettingApp.Services.TeamAliasMappingService.ResolveTeamSelection(ActiveLookupLeg.Selection, normT1, normT2, targetOp1, targetOp2);
                        bool mentionsT1 = teamResolved == 1 || BettingApp.Services.TeamAliasMappingService.IsTeamMatch(ActiveLookupLeg.Selection, normT1) || (!string.IsNullOrEmpty(targetOp1) && BettingApp.Services.TeamAliasMappingService.IsTeamMatch(ActiveLookupLeg.Selection, targetOp1));
                        bool mentionsT2 = teamResolved == 2 || BettingApp.Services.TeamAliasMappingService.IsTeamMatch(ActiveLookupLeg.Selection, normT2) || (!string.IsNullOrEmpty(targetOp2) && BettingApp.Services.TeamAliasMappingService.IsTeamMatch(ActiveLookupLeg.Selection, targetOp2));
                        bool hasDraw = System.Text.RegularExpressions.Regex.IsMatch(normSelRaw, @"\b(?:draw|tie|ties|oavgjort|uavgjort|kryss)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                        if (mentionsT1 && mentionsT2) aiTarget = "12";
                        else if (teamResolved == 1 && hasDraw) aiTarget = "1X";
                        else if (teamResolved == 2 && hasDraw) aiTarget = "X2";
                        else if (teamResolved == 1) aiTarget = "1";
                        else if (teamResolved == 2) aiTarget = "2";
                        else if (hasDraw) aiTarget = "X";
                        else if (normSelRaw.Contains("1x")) aiTarget = "1X";
                        else if (normSelRaw.Contains("x2") || normSelRaw.Contains("2x")) aiTarget = "X2";
                        else if (normSelRaw.Contains("12")) aiTarget = "12";
                    }
                    
                    if (!string.IsNullOrEmpty(htftTarget) && (string.Equals(oName, htftTarget, StringComparison.OrdinalIgnoreCase) || string.Equals(opHtftTarget, htftTarget, StringComparison.OrdinalIgnoreCase)))
                    {
                        logicallyMatched = true;
                    }
                    else if (!string.IsNullOrEmpty(aiTarget))
                    {
                        var basicHcMatch = System.Text.RegularExpressions.Regex.Match(oName, @"^(1|X|x|2|1X|X1|X2|2X|12)(?:\s*\(([^)]+)\))?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (basicHcMatch.Success)
                        {
                            string opTarget = basicHcMatch.Groups[1].Value.ToUpperInvariant();
                            if (opTarget == "2X") opTarget = "X2";
                            if (opTarget == "X1") opTarget = "1X";
                            string normAiTarget = aiTarget;
                            if (normAiTarget == "2X") normAiTarget = "X2";
                            if (normAiTarget == "X1") normAiTarget = "1X";

                            if (normAiTarget == opTarget)
                            {
                                string hcStr = basicHcMatch.Groups[2].Value.Trim();
                                double? opHc = null;
                                if (!string.IsNullOrEmpty(hcStr))
                                {
                                    if (double.TryParse(hcStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
                                    {
                                        if (hcStr.StartsWith("+"))
                                        {
                                            opHc = val;
                                        }
                                        else
                                        {
                                            opHc = opTarget == "2" ? -val : val;
                                        }
                                    }
                                    else if (hcStr.Contains("-") && !hcStr.StartsWith("-"))
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
                rawSel = System.Text.RegularExpressions.Regex.Replace(rawSel, @"\b(power sub|super sub)\b", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                string displayONameSafe = System.Text.RegularExpressions.Regex.Replace(displayOName ?? "", @"\b(power sub|super sub)\b", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                string normOName = System.Text.RegularExpressions.Regex.Replace(BettingApp.Services.TeamAliasMappingService.StripToAlphaNumeric(displayONameSafe), @"\.0+(?!\d)", "");
                string normSel = System.Text.RegularExpressions.Regex.Replace(BettingApp.Services.TeamAliasMappingService.StripToAlphaNumeric(rawSel), @"\.0+(?!\d)", "");
                
                bool isLineSel = normSel.StartsWith("over") || normSel.StartsWith("under") || normOName.StartsWith("over") || normOName.StartsWith("under");
                if (normOName == normSel || (!isLineSel && !string.IsNullOrEmpty(normOName) && !string.IsNullOrEmpty(normSel) && BettingApp.Services.TeamAliasMappingService.IsTeamMatch(normOName, normSel)))
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
                else if ((normOName == "yes" || normOName == "over05") && !string.IsNullOrEmpty(opMarketName))
                {
                    string normOpMarket = BettingApp.Services.TeamAliasMappingService.RemoveDiacritics(opMarketName).ToLowerInvariant().Replace(" ", "").Replace("toscore", "").Replace("anytimegoalscorer", "");
                    string playerNameFromAiSel = normSel.Replace("yes", "").Replace("no", "").Replace("over05", "").Replace("under05", "").Replace("toprovideanassist", "").Replace("powersub", "").Replace("-", "");
                    
                    if (!string.IsNullOrEmpty(normOpMarket) && !string.IsNullOrEmpty(playerNameFromAiSel) && normOpMarket.Length > 3 && playerNameFromAiSel.Length > 3 && (normOpMarket.Contains(playerNameFromAiSel) || playerNameFromAiSel.Contains(normOpMarket) || IsFuzzyPlayerMatch(opMarketName, ActiveLookupLeg?.Selection ?? "")))
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
                            (teamOName.Contains(teamSel) || teamSel.Contains(teamOName) || BettingApp.Services.TeamAliasMappingService.IsTeamMatch(teamOName, teamSel)))
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
                                (baseOName.Contains(baseSel) || baseSel.Contains(baseOName) || BettingApp.Services.TeamAliasMappingService.IsTeamMatch(baseOName, baseSel)))
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
                    else if (IsFuzzyPlayerMatch(displayOName ?? "", string.IsNullOrWhiteSpace(ActiveLookupLeg?.Market) ? (ActiveLookupLeg?.Selection ?? "") : $"{ActiveLookupLeg.Market} {ActiveLookupLeg.Selection}"))
                    {
                        isMatch = true;
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
                            if (IsPlayerPropOutcomeMatch(normOutcomePart, normSel, ActiveLookupLeg))
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
                            
                            if (!double.TryParse(pName, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _) && normSel.Contains(pName))
                            {
                                string outcomePart = (displayOName ?? "").Substring(0, simpleParenMatch.Index).Trim();
                                string normOutcomePart = System.Text.RegularExpressions.Regex.Replace(BettingApp.Services.TeamAliasMappingService.RemoveDiacritics(outcomePart).ToLowerInvariant().Replace(" ", "").Replace("(", "").Replace(")", "").Replace(":", "-"), @"\.0+(?!\d)", "");
                                
                                if (IsPlayerPropOutcomeMatch(normOutcomePart, normSel, ActiveLookupLeg))
                                {
                                    isMatch = true;
                                }
                            }
                        }
                    }
                }
            }
        }
        
        string fallbackSel = (string.IsNullOrWhiteSpace(ActiveLookupLeg?.Market) ? "" : ActiveLookupLeg.Market + " ") + (ActiveLookupLeg?.Selection ?? "");
        fallbackSel = System.Text.RegularExpressions.Regex.Replace(fallbackSel, @"(\d+)\+", m => $"over {double.Parse(m.Groups[1].Value) - 0.5}");
        fallbackSel = System.Text.RegularExpressions.Regex.Replace(fallbackSel, @"(?:to score\s+|to record\s+|to get\s+|to have\s+)?(\d+)\s+or\s+more", m => $"over {double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) - 0.5}");
        fallbackSel = System.Text.RegularExpressions.Regex.Replace(fallbackSel, @"(?:at\s+least\s+)(\d+)", m => $"over {double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) - 0.5}");
        
        if (!isMatch && !string.IsNullOrEmpty(displayOName) && ActiveLookupLeg != null && !string.IsNullOrEmpty(ActiveLookupLeg.Selection))
        {
            isMatch = IsFuzzyPlayerMatch(fallbackSel, displayOName);
        }
        
        return isMatch;
    }

    private static bool IsPlayerPropOutcomeMatch(string normOutcomePart, string normSel, BettingApp.Services.AiVisionLeg? activeLeg)
    {
        string fullLegText = $"{(activeLeg?.Market ?? "")} {(activeLeg?.Selection ?? "")}";
        
        bool hasHigherLineReq = System.Text.RegularExpressions.Regex.IsMatch(
            fullLegText,
            @"\b(?:at\s+least\s+([2-9]|\d{2,})|([2-9]|\d{2,})\s*(?:\+|or\s+more|points?|goals?|assists?|shots?|saves?|cards?|tackles?|fouls?)|over\s*(?:0\.[6-9]|[1-9]|\d{2,}|[1-9]\.\d+))\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        double? targetLine = null;
        var lineMatch = System.Text.RegularExpressions.Regex.Match(
            fullLegText,
            @"\b(?:(over|under)\s*(\d+(?:\.\d+)?)|at\s+least\s+(\d+(?:\.\d+)?)|(\d+(?:\.\d+)?)\s*(?:\+|or\s+more))\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        bool wantsUnder = normSel.Contains("under") || normSel.Contains("no");

        if (lineMatch.Success)
        {
            string dirStr = lineMatch.Groups[1].Value.ToLowerInvariant();
            if (dirStr == "under") wantsUnder = true;

            string valStr = !string.IsNullOrEmpty(lineMatch.Groups[2].Value) ? lineMatch.Groups[2].Value :
                            (!string.IsNullOrEmpty(lineMatch.Groups[3].Value) ? lineMatch.Groups[3].Value : lineMatch.Groups[4].Value);

            if (double.TryParse(valStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedVal))
            {
                if (!string.IsNullOrEmpty(lineMatch.Groups[1].Value))
                {
                    targetLine = parsedVal;
                }
                else
                {
                    targetLine = parsedVal >= 1 ? parsedVal - 0.5 : parsedVal;
                }
            }
        }

        bool isOutcomeMatch = false;

        if (targetLine.HasValue)
        {
            string cleanTarget = targetLine.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            string expectedOver = $"over{cleanTarget}";
            string expectedUnder = $"under{cleanTarget}";
            string cleanOutcome = normOutcomePart.Replace(" ", "");

            if (wantsUnder)
            {
                isOutcomeMatch = cleanOutcome == expectedUnder || cleanOutcome.Replace(".", "") == expectedUnder.Replace(".", "");
            }
            else
            {
                isOutcomeMatch = cleanOutcome == expectedOver || cleanOutcome.Replace(".", "") == expectedOver.Replace(".", "");
            }
        }
        else
        {
            isOutcomeMatch = string.IsNullOrEmpty(normOutcomePart) || 
                              normSel.Contains(normOutcomePart) || 
                              (normOutcomePart == "yes" && (!normSel.Contains("no") && !normSel.Contains("under"))) ||
                              (normOutcomePart == "over0.5" && normSel.Contains("yes") && !normSel.Contains("no") && !normSel.Contains("under") && !hasHigherLineReq);
        }

        if (!isOutcomeMatch && normOutcomePart.EndsWith("+"))
        {
            if (int.TryParse(normOutcomePart.TrimEnd('+'), out int plusVal))
            {
                string eqOver = $"over{plusVal - 1}.5";
                if (normSel.Contains(eqOver) || (plusVal == 1 && (normSel.Contains("assist") || normSel.Contains("score") || normSel.Contains("goal") || normSel.Contains("point") || normSel.Contains("1or"))))
                {
                    isOutcomeMatch = true;
                }
            }
        }

        return isOutcomeMatch;
    }
#endif

    public static BettingApp.Models.OddsPapiMarket? FindBestMarket(List<BettingApp.Models.OddsPapiMarket> markets, string marketCategory, string? matchName, string? selectionName, BettingApp.Services.MarketMappingService marketMapper)
    {
        if (markets == null || !markets.Any() || string.IsNullOrEmpty(marketCategory)) return null;

        // If the betslip is for a Bet Builder or combination market (e.g. "Both Teams To Score & Over 2.5 Goals", "Match Result and Both Teams to Score", etc.),
        // OddsPapi does not provide combined odds. Do not match any single market component.
        if (BettingApp.Services.MarketMappingService.IsCombinationMarket(marketCategory, matchName) || 
            (!string.IsNullOrEmpty(selectionName) && BettingApp.Services.MarketMappingService.IsCombinationMarket(selectionName, matchName)))
        {
            return null;
        }
        
        var normalizedTargets = marketMapper.NormalizeMarketName(marketCategory, matchName, selectionName);
        
        var simplify = (string s) => 
        {
            var clean = BettingApp.Services.TeamAliasMappingService.RemoveDiacritics(s).ToLowerInvariant()
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
            var statKeywords = new[] { "corner", "booking", "card", "foul", "save", "goalkeeper", "shot", "tackle", "offside" };

            foreach (var target in simplifiedTargets)
            {
                var targetStats = statKeywords.Where(kw => target.Contains(kw)).ToList();

                var potentialMatches = markets.Where(m => 
                {
                    string simM = simplify(m.MarketName);
                    if (targetStats.Any())
                    {
                        bool hasMatchingStat = targetStats.Any(kw => 
                            simM.Contains(kw) || 
                            (kw == "booking" && simM.Contains("card")) || 
                            (kw == "card" && simM.Contains("booking")) ||
                            (kw == "goalkeeper" && simM.Contains("save")) ||
                            (kw == "save" && simM.Contains("goalkeeper")));
                        if (!hasMatchingStat) return false;
                    }
                    else
                    {
                        if (statKeywords.Any(kw => simM.Contains(kw))) return false;
                    }

                    return simM.Contains(target) || (target.Contains(simM) && Math.Abs(target.Length - simM.Length) <= 4);
                }).ToList();

                if (potentialMatches.Count == 1)
                {
                    bestMatch = potentialMatches.First();
                    break;
                }
                else if (potentialMatches.Count > 1 && !string.IsNullOrEmpty(selectionName))
                {
                    string normSel = simplify(selectionName).Replace("yes", "").Replace("no", "").Replace("over05", "").Replace("under05", "");
                    if (normSel.Length > 3) 
                    {
                        bestMatch = potentialMatches.FirstOrDefault(m => IsFuzzyPlayerMatch(selectionName, m.MarketName) || simplify(m.MarketName).Contains(normSel) || normSel.Contains(simplify(m.MarketName)));
                    }
                    if (bestMatch == null) bestMatch = potentialMatches.First();
                    break;
                }
                else if (potentialMatches.Count > 1)
                {
                    bestMatch = potentialMatches.First();
                    break;
                }
            }
        }
        
        if (bestMatch == null && simplifiedTargets.Any(t => t.Contains("teamtotalgoal")))
        {
            if (!string.IsNullOrEmpty(matchName) && !string.IsNullOrEmpty(selectionName))
            {
                var hcSplit = matchName.Split(new[] { " vs ", " v ", " - " }, StringSplitOptions.None);
                if (hcSplit.Length >= 2)
                {
                    int matchedTeam = BettingApp.Services.TeamAliasMappingService.ResolveTeamSelection(selectionName, hcSplit[0], hcSplit[1]);
                    string expectedTeamMarket = "";
                    if (matchedTeam == 1) expectedTeamMarket = simplify("Over Under Team 1");
                    else if (matchedTeam == 2) expectedTeamMarket = simplify("Over Under Team 2");
                    
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
