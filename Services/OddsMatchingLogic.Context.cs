using System;
using System.Globalization;
using System.Text.RegularExpressions;
using BettingApp.Models;

namespace BettingApp.Services
{
    // Pre-computed normalization and resolution context for a betslip leg against an OddsPapi fixture.
    public static partial class OddsMatchingLogic
    {
        private static string ResolveHtftCode(string part, int teamNum)
        {
            string p = (part ?? "").Trim().ToLowerInvariant();
            if (p == "1") return "1";
            if (p == "2") return "2";
            if (p == "x") return "X";
            if (teamNum == 1) return "1";
            if (teamNum == 2) return "2";
            if (_drawTieRegex.IsMatch(p)) return "X";
            return "";
        }

        internal static bool IsHalfTimeFullTimeMarket(string? market)
        {
            if (string.IsNullOrWhiteSpace(market)) return false;
            if (Regex.IsMatch(market, @"\bht\s*[/\-]\s*ft\b", RegexOptions.IgnoreCase)) return true;
            bool hasHalf = Regex.IsMatch(market, @"\b(?:half|halftime|halvtid|pause|ht)\b", RegexOptions.IgnoreCase);
            bool hasFull = Regex.IsMatch(market, @"\b(?:full|fulltime|fulltid|sluttid|ft)\b", RegexOptions.IgnoreCase);
            return hasHalf && hasFull;
        }

        private static string ResolveDoubleChancePartCode(string part, string normT1, string normT2, string targetOp1, string targetOp2)
        {
            string p = (part ?? "").Trim();
            if (string.IsNullOrEmpty(p)) return "";
            if (_drawTieRegex.IsMatch(p)) return "X";
            if (Regex.IsMatch(p, @"^[+-]?\d+(?:\.\d+)?$")) return "";
            int teamNum = TeamAliasMappingService.ResolveTeamSelection(p, normT1, normT2, targetOp1, targetOp2);
            if (teamNum == 1) return "1";
            if (teamNum == 2) return "2";
            return "";
        }

        internal static string StripTwoWayMarketPhrases(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return Regex.Replace(
                text,
                @"\b(?:over\s*(?:[\/-]\s*|\s+)under|under\s*(?:[\/-]\s*|\s+)over|o\s*\/\s*u|will\s*(?:[\/-]\s*|\s+or\s+)will\s+not|yes\s*(?:[\/-]\s*|\s+)no|no\s*(?:[\/-]\s*|\s+)yes|ja\s*(?:[\/-]\s*|\s+)nei|nei\s*(?:[\/-]\s*|\s+)ja|draw\s+no\s+bet)\b",
                " ",
                RegexOptions.IgnoreCase);
        }

        internal static string NormalizeLinePhrases(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string s = text;
            s = Regex.Replace(s, @"\b(?:a\s+)?hat(?:\s*-\s*|\s+)?trick\b", "3 or more", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\b(?:a\s+)?brace\b", "2 or more", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\b(at\s+least\s+)one(?!\s+half)\b", "${1}1", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\b(at\s+least\s+)two\b", "${1}2", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\b(at\s+least\s+)three\b", "${1}3", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\b(at\s+least\s+)four\b", "${1}4", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\b(at\s+least\s+)five\b", "${1}5", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bone(\s+(?:or|and)\s+more)\b", "1$1", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\btwo(\s+(?:or|and)\s+more)\b", "2$1", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bthree(\s+(?:or|and)\s+more)\b", "3$1", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bfour(\s+(?:or|and)\s+more)\b", "4$1", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bfive(\s+(?:or|and)\s+more)\b", "5$1", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\b(\d+)\s+and\s+more\b", "$1 or more", RegexOptions.IgnoreCase);
            return s;
        }

        public class OutcomeMatchingContext
        {
            public AiVisionLeg? Leg { get; }
            public OddsPapiSearchResult? SearchResult { get; }
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

            public OutcomeMatchingContext(AiVisionLeg? leg, OddsPapiSearchResult? searchResult, string? opMarketName = null)
            {
                Leg = leg;
                SearchResult = searchResult;
                OpMarketName = opMarketName;

                if (leg == null || string.IsNullOrEmpty(leg.Selection)) return;

                var mSel = _hcSignRegex.Match(leg.Selection);
                if (mSel.Success && double.TryParse(mSel.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double valS))
                {
                    SelHcVal = valS;
                    HasSelHc = true;
                }

                NormSelRaw = TeamAliasMappingService.RemoveDiacritics(leg.Selection).ToLowerInvariant();

                if (leg.Match != null)
                {
                    var hcSplit = leg.Match.Split(new[] { " vs ", " v ", " - " }, StringSplitOptions.None);
                    if (hcSplit.Length >= 2)
                    {
                        AiT1 = TeamAliasMappingService.RemoveDiacritics(hcSplit[0].Split('(')[0].Trim()).ToLowerInvariant();
                        AiT2 = TeamAliasMappingService.RemoveDiacritics(hcSplit[1].Split('(')[0].Trim()).ToLowerInvariant();

                        if (searchResult != null && !string.IsNullOrEmpty(searchResult.MatchName))
                        {
                            var opMatchSplit = searchResult.MatchName.Split(new[] { " vs ", " v ", " - " }, StringSplitOptions.None);
                            if (opMatchSplit.Length >= 2)
                            {
                                OpT1 = TeamAliasMappingService.RemoveDiacritics(opMatchSplit[0].Trim()).ToLowerInvariant();
                                OpT2 = TeamAliasMappingService.RemoveDiacritics(opMatchSplit[1].Trim()).ToLowerInvariant();
                            }
                        }

                        bool ai1MatchesOp1 = !string.IsNullOrEmpty(OpT1) && ((OpT1.Contains(AiT1) && AiT1.Length > 2) || (AiT1.Contains(OpT1) && OpT1.Length > 2) || TeamAliasMappingService.IsTeamMatch(AiT1, OpT1));
                        bool ai2MatchesOp2 = !string.IsNullOrEmpty(OpT2) && ((OpT2.Contains(AiT2) && AiT2.Length > 2) || (AiT2.Contains(OpT2) && OpT2.Length > 2) || TeamAliasMappingService.IsTeamMatch(AiT2, OpT2));
                        bool ai1MatchesOp2 = !string.IsNullOrEmpty(OpT2) && ((OpT2.Contains(AiT1) && AiT1.Length > 2) || (AiT1.Contains(OpT2) && OpT2.Length > 2) || TeamAliasMappingService.IsTeamMatch(AiT1, OpT2));
                        bool ai2MatchesOp1 = !string.IsNullOrEmpty(OpT1) && ((OpT1.Contains(AiT2) && AiT2.Length > 2) || (AiT2.Contains(OpT1) && OpT1.Length > 2) || TeamAliasMappingService.IsTeamMatch(AiT2, OpT1));

                        if ((ai1MatchesOp2 || ai2MatchesOp1) && !ai1MatchesOp1 && !ai2MatchesOp2)
                        {
                            IsSwapped = true;
                        }

                        NormT1 = IsSwapped ? AiT2 : AiT1;
                        NormT2 = IsSwapped ? AiT1 : AiT2;
                        TargetOp1 = OpT1;
                        TargetOp2 = OpT2;

                        // Half-Time / Full-Time target resolution (ONLY when the market is actually Half-Time / Full-Time)
                        bool isHtftMarket = IsHalfTimeFullTimeMarket(leg.Market) || IsHalfTimeFullTimeMarket(opMarketName);
                        if (isHtftMarket)
                        {
                            string cleanNormSel = NormSelRaw.Replace(" ", "");
                            if (_htftRegex.IsMatch(cleanNormSel))
                            {
                                HtftTarget = cleanNormSel.ToUpperInvariant();
                            }
                            else
                            {
                                string[]? htftSplit = null;
                                if (NormSelRaw.Contains(" / "))
                                {
                                    htftSplit = NormSelRaw.Split(new[] { " / " }, StringSplitOptions.RemoveEmptyEntries);
                                }
                                else if (NormSelRaw.Contains("/"))
                                {
                                    htftSplit = NormSelRaw.Split('/', StringSplitOptions.RemoveEmptyEntries);
                                }
                                else if (NormSelRaw.Contains(" - "))
                                {
                                    htftSplit = NormSelRaw.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);
                                }
                                else if (NormSelRaw.Contains("-"))
                                {
                                    htftSplit = NormSelRaw.Split('-', StringSplitOptions.RemoveEmptyEntries);
                                }

                                if (htftSplit != null && htftSplit.Length == 2)
                                {
                                    string ht = htftSplit[0].Trim();
                                    string ft = htftSplit[1].Trim();
                                    int htTeam = TeamAliasMappingService.ResolveTeamSelection(ht, NormT1, NormT2, TargetOp1, TargetOp2);
                                    int ftTeam = TeamAliasMappingService.ResolveTeamSelection(ft, NormT1, NormT2, TargetOp1, TargetOp2);
                                    string htCode = ResolveHtftCode(ht, htTeam);
                                    string ftCode = ResolveHtftCode(ft, ftTeam);
                                    if (!string.IsNullOrEmpty(htCode) && !string.IsNullOrEmpty(ftCode))
                                    {
                                        HtftTarget = $"{htCode}/{ftCode}";
                                    }
                                }
                            }
                        }

                        // 1X2 / Double Chance / Handicap team target resolution
                        if (string.IsNullOrEmpty(HtftTarget))
                        {
                            string trimmedNormSel = NormSelRaw.Trim();

                            // 1. Direct Double Chance code patterns ("1X", "X1", "1/X", "X/1", "X2", "2X", "X/2", "2/X", "12", "1/2")
                            if (Regex.IsMatch(trimmedNormSel, @"^(?:1\s*[/\-]?\s*x|x\s*[/\-]?\s*1)$", RegexOptions.IgnoreCase))
                            {
                                AiTarget = "1X";
                            }
                            else if (Regex.IsMatch(trimmedNormSel, @"^(?:2\s*[/\-]?\s*x|x\s*[/\-]?\s*2)$", RegexOptions.IgnoreCase))
                            {
                                AiTarget = "X2";
                            }
                            else if (Regex.IsMatch(trimmedNormSel, @"^(?:12|21|1\s*/\s*2|2\s*/\s*1)$", RegexOptions.IgnoreCase))
                            {
                                AiTarget = "12";
                            }

                            // 2. Two-part composite Double Chance selections ("Como/Draw", "Draw/Como", "Como or Draw", "Como - Draw", "Como/Manchester United", "Como / X", etc.)
                            if (string.IsNullOrEmpty(AiTarget))
                            {
                                var dcParts = Regex.Split(leg.Selection, @"(?:\s*/\s*|\s+-\s+|\s+(?:or|eller|and)\s+|\s*&\s*|\s*,\s*)", RegexOptions.IgnoreCase)
                                                   .Where(p => !string.IsNullOrWhiteSpace(p))
                                                   .ToArray();
                                if (dcParts.Length == 2)
                                {
                                    string c1 = ResolveDoubleChancePartCode(dcParts[0], NormT1, NormT2, TargetOp1, TargetOp2);
                                    string c2 = ResolveDoubleChancePartCode(dcParts[1], NormT1, NormT2, TargetOp1, TargetOp2);
                                    if ((c1 == "1" && c2 == "X") || (c1 == "X" && c2 == "1")) AiTarget = "1X";
                                    else if ((c1 == "2" && c2 == "X") || (c1 == "X" && c2 == "2")) AiTarget = "X2";
                                    else if ((c1 == "1" && c2 == "2") || (c1 == "2" && c2 == "1")) AiTarget = "12";
                                }
                            }

                            // 3. Single-team / Draw / fallback resolution
                            if (string.IsNullOrEmpty(AiTarget))
                            {
                                int teamResolved = TeamAliasMappingService.ResolveTeamSelection(leg.Selection, NormT1, NormT2, TargetOp1, TargetOp2);
                                bool hasDraw = Regex.IsMatch(NormSelRaw, @"\b(?:draw|tie|ties|oavgjort|uavgjort|kryss|likt)\b|^x$", RegexOptions.IgnoreCase);

                                if (teamResolved == 1 && hasDraw) AiTarget = "1X";
                                else if (teamResolved == 2 && hasDraw) AiTarget = "X2";
                                else if (teamResolved == 1) AiTarget = "1";
                                else if (teamResolved == 2) AiTarget = "2";
                                else if (hasDraw) AiTarget = "X";
                                else if (NormSelRaw.Contains("1x") || NormSelRaw.Contains("x1")) AiTarget = "1X";
                                else if (NormSelRaw.Contains("x2") || NormSelRaw.Contains("2x")) AiTarget = "X2";
                                else if (NormSelRaw.Contains("12")) AiTarget = "12";
                            }
                        }

                        // Handicap value resolution (from Selection or Market)
                        var selScoreHcMatch = Regex.Match(NormSelRaw.Replace(" ", ""), @"\((\d+(?:\.\d+)?[-:]\d+(?:\.\d+)?)\)$");
                        if (selScoreHcMatch.Success && TryParseOddsPapiHandicap(AiTarget, selScoreHcMatch.Groups[1].Value, out double parsedSelScoreHc))
                        {
                            AiHc = parsedSelScoreHc;
                        }
                        else
                        {
                            var aiHcMatch = _hcSuffixRegex.Match(NormSelRaw.Replace(" ", "").Replace("(", "").Replace(")", ""));
                            if (aiHcMatch.Success && double.TryParse(aiHcMatch.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedAiHc))
                            {
                                AiHc = parsedAiHc;
                            }
                            else if (leg.Market != null)
                            {
                                var mktRegex = new Regex(@"(?:starts|\()(\d+[-:]\d+|[+-]\d+(?:\.\d+)?)\)?$", RegexOptions.IgnoreCase);
                                var mktMatch = mktRegex.Match(leg.Market.Replace(" ", "").ToLowerInvariant());
                                if (mktMatch.Success)
                                {
                                    string extractedHc = mktMatch.Groups[1].Value;
                                    if (TryParseOddsPapiHandicap(AiTarget, extractedHc, out double parsedMktHc))
                                    {
                                        // Note: if extractedHc started with "+", TryParseOddsPapiHandicap returns +val directly;
                                        // for market handicaps like "(+1)", Team 2 gets -1 while Team 1 gets +1.
                                        if (extractedHc.StartsWith("+") && AiTarget == "2" &&
                                            double.TryParse(extractedHc, NumberStyles.Any, CultureInfo.InvariantCulture, out double rawPlus))
                                        {
                                            AiHc = -rawPlus;
                                        }
                                        else
                                        {
                                            AiHc = parsedMktHc;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                // Direction (Over vs Under): Selection ALWAYS takes precedence over market category!
                bool selHasUnder = Regex.IsMatch(NormSelRaw, @"\b(?:under|no|nei|not)\b", RegexOptions.IgnoreCase);
                bool selHasOver = Regex.IsMatch(NormSelRaw, @"\b(?:over|yes|ja)\b", RegexOptions.IgnoreCase) ||
                                  (!selHasUnder && Regex.IsMatch(NormSelRaw, @"\b(?:will\s+(?:score|get|be|assist|give|provide|record|have|make|receive)|to\s+(?:score|give|provide|get|be|receive|record|make|have)|anytime)\b", RegexOptions.IgnoreCase));

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
                    string mktRaw = leg.Market ?? "";
                    string mktCleaned = StripTwoWayMarketPhrases(mktRaw);
                    bool isTwoWayMarket = mktRaw.Contains("over", StringComparison.OrdinalIgnoreCase) && mktRaw.Contains("under", StringComparison.OrdinalIgnoreCase);

                    WantsUnder = !isTwoWayMarket && Regex.IsMatch(mktCleaned, @"\b(?:under|no|nei|not)\b", RegexOptions.IgnoreCase);
                }

                string dirKeyword = WantsUnder ? "under" : "over";

                string rawSel = NormalizeLinePhrases(leg.Selection);
                rawSel = Regex.Replace(rawSel, @"(\d+)\+", m => $"{dirKeyword} {double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) - 0.5}", RegexOptions.IgnoreCase);
                rawSel = Regex.Replace(rawSel, @"(?:to score\s+|to record\s+|to get\s+|to have\s+)?(\d+)\s+or\s+more", m => $"{dirKeyword} {double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) - 0.5}", RegexOptions.IgnoreCase);
                rawSel = Regex.Replace(rawSel, @"(?:at\s+least\s+)(\d+)", m => $"{dirKeyword} {double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) - 0.5}", RegexOptions.IgnoreCase);
                rawSel = Regex.Replace(rawSel, @"\btie\b", "draw", RegexOptions.IgnoreCase);
                rawSel = _powerSubRegex.Replace(rawSel, "");
                RawSel = rawSel;

                NormSel = _dotZeroRegex.Replace(TeamAliasMappingService.StripToAlphaNumeric(rawSel), "");

                var mSelNum = _numSuffixRegex.Match(NormSel);
                if (mSelNum.Success)
                {
                    NumSelSuccess = true;
                    NumSelVal = mSelNum.Value;
                    NumSelIndex = mSelNum.Index;
                }

                if (!string.IsNullOrEmpty(leg.Market))
                {
                    NormMarket = TeamAliasMappingService.RemoveDiacritics(leg.Market).ToLowerInvariant().Replace(" ", "").Replace("toscore", "").Replace("anytimegoalscorer", "");
                }

                if (!string.IsNullOrEmpty(opMarketName))
                {
                    NormOpMarket = TeamAliasMappingService.RemoveDiacritics(opMarketName).ToLowerInvariant().Replace(" ", "").Replace("toscore", "").Replace("anytimegoalscorer", "");
                }

                PlayerNameFromAiSel = NormSel.Replace("yes", "").Replace("no", "").Replace("over05", "").Replace("under05", "").Replace("toprovideanassist", "").Replace("powersub", "").Replace("-", "");

                string cleanMktForFb = StripTwoWayMarketPhrases(leg.Market ?? "");
                string fb = NormalizeLinePhrases((string.IsNullOrWhiteSpace(cleanMktForFb) ? "" : cleanMktForFb + " ") + leg.Selection);
                fb = Regex.Replace(fb, @"(\d+)\+", m => $"{dirKeyword} {double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) - 0.5}", RegexOptions.IgnoreCase);
                fb = Regex.Replace(fb, @"(?:to score\s+|to record\s+|to get\s+|to have\s+)?(\d+)\s+or\s+more", m => $"{dirKeyword} {double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) - 0.5}", RegexOptions.IgnoreCase);
                fb = Regex.Replace(fb, @"(?:at\s+least\s+)(\d+)", m => $"{dirKeyword} {double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) - 0.5}", RegexOptions.IgnoreCase);
                FallbackSel = fb;

                string normLineSel = NormalizeLinePhrases(leg.Selection ?? "");
                string normLineMkt = NormalizeLinePhrases(leg.Market ?? "");
                string fullLegText = $"{normLineMkt} {normLineSel}";
                HasHigherLineReq = Regex.IsMatch(
                    fullLegText,
                    @"(?:\bat\s+least\s+([2-9]|\d{2,})\b|\b([2-9]|\d{2,})\s*(?:\+(?!\d)|or\s+more\b|\s*(?:points?|goals?|assists?|shots?|saves?|cards?|tackles?|fouls?)\b)|\bover\s*(?:0\.[6-9]|[1-9]|\d{2,}|[1-9]\.\d+)\b)",
                    RegexOptions.IgnoreCase);

                // Target Line: prefer Selection line over Market line
                var selLineMatch = Regex.Match(
                    normLineSel,
                    @"(?:\b(?:over|under)\s*(\d+(?:\.\d+)?)\b|\bat\s+least\s+(\d+(?:\.\d+)?)\b|\b(\d+(?:\.\d+)?)\s*(?:\+(?!\d)|or\s+more\b))",
                    RegexOptions.IgnoreCase);

                var mktLineMatch = Regex.Match(
                    normLineMkt,
                    @"(?:\b(?:over|under)\s*(\d+(?:\.\d+)?)\b|\bat\s+least\s+(\d+(?:\.\d+)?)\b|\b(\d+(?:\.\d+)?)\s*(?:\+(?!\d)|or\s+more\b))",
                    RegexOptions.IgnoreCase);

                var activeLineMatch = selLineMatch.Success ? selLineMatch : mktLineMatch;
                if (activeLineMatch.Success)
                {
                    string valStr = !string.IsNullOrEmpty(activeLineMatch.Groups[1].Value) ? activeLineMatch.Groups[1].Value :
                                    (!string.IsNullOrEmpty(activeLineMatch.Groups[2].Value) ? activeLineMatch.Groups[2].Value : activeLineMatch.Groups[3].Value);

                    if (double.TryParse(valStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedVal))
                    {
                        TargetLine = !string.IsNullOrEmpty(activeLineMatch.Groups[1].Value)
                            ? parsedVal
                            : (parsedVal >= 1 ? parsedVal - 0.5 : parsedVal);
                    }
                }
                else
                {
                    var fallbackLineMatch = Regex.Match(
                        $"{normLineSel} {normLineMkt}",
                        @"(?:\b(?:total\s+goals?|total|goals?|mål|mal|o\/u|line)\s*(\d+(?:\.\d+)?)\b|\b(\d+(?:\.\d+)?)\s*(?:goals?|mål|mal)\b|(?<![+-])\b(\d+\.\d+)\b)",
                        RegexOptions.IgnoreCase);

                    if (fallbackLineMatch.Success)
                    {
                        string valStr = !string.IsNullOrEmpty(fallbackLineMatch.Groups[1].Value) ? fallbackLineMatch.Groups[1].Value :
                                        (!string.IsNullOrEmpty(fallbackLineMatch.Groups[2].Value) ? fallbackLineMatch.Groups[2].Value : fallbackLineMatch.Groups[3].Value);

                        if (double.TryParse(valStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedVal))
                        {
                            TargetLine = parsedVal;
                        }
                    }
                }

                if (TargetLine.HasValue)
                {
                    string cleanTarget = TargetLine.Value.ToString("0.0", CultureInfo.InvariantCulture);
                    CleanExpectedOver = $"over{cleanTarget}";
                    CleanExpectedUnder = $"under{cleanTarget}";
                }
            }
        }
    }
}
