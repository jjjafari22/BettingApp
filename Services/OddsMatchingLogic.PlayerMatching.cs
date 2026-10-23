using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace BettingApp.Services
{
    // Player prop line/direction evaluator, player initials matching, and token-based fuzzy player matching.
    public static partial class OddsMatchingLogic
    {
        private static bool IsPlayerPropOutcomeMatchFast(string normOutcomePart, OutcomeMatchingContext ctx)
        {
            bool isOutcomeMatch = false;

            if (ctx.TargetLine.HasValue)
            {
                string cleanOutcome = normOutcomePart.Replace(" ", "");
                bool isUnder = cleanOutcome.Contains("under") || cleanOutcome.Contains("no") || cleanOutcome.Contains("nei");
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
                        var numMatch = Regex.Match(cleanOutcome, @"\d+(?:\.\d+)?");
                        if (numMatch.Success && double.TryParse(numMatch.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedOutcomeLine))
                        {
                            if (Math.Abs(parsedOutcomeLine - ctx.TargetLine.Value) < 0.001)
                            {
                                isOutcomeMatch = true;
                            }
                        }
                        else if (Math.Abs(ctx.TargetLine.Value - 0.5) < 0.001)
                        {
                            if (!ctx.WantsUnder && (cleanOutcome == "yes" || cleanOutcome == "ja" || string.IsNullOrEmpty(cleanOutcome)))
                            {
                                isOutcomeMatch = true;
                            }
                            else if (ctx.WantsUnder && (cleanOutcome == "no" || cleanOutcome == "nei"))
                            {
                                isOutcomeMatch = true;
                            }
                        }
                    }
                }
            }
            else
            {
                if (!ctx.WantsUnder && !ctx.HasHigherLineReq)
                {
                    isOutcomeMatch = string.IsNullOrEmpty(normOutcomePart) ||
                                     normOutcomePart == "yes" ||
                                     normOutcomePart == "ja" ||
                                     normOutcomePart == "over0.5" ||
                                     normOutcomePart == "over05" ||
                                     normOutcomePart == "1+";
                }
                else if (ctx.WantsUnder && !ctx.HasHigherLineReq)
                {
                    isOutcomeMatch = normOutcomePart == "no" ||
                                     normOutcomePart == "nei" ||
                                     normOutcomePart == "under0.5" ||
                                     normOutcomePart == "under05";
                }

                if (!isOutcomeMatch && !string.IsNullOrEmpty(normOutcomePart) &&
                    normOutcomePart != "yes" && normOutcomePart != "no" &&
                    normOutcomePart != "ja" && normOutcomePart != "nei" &&
                    !normOutcomePart.StartsWith("over") && !normOutcomePart.StartsWith("under") &&
                    ctx.NormSel.Contains(normOutcomePart))
                {
                    bool outcomeIsNeg = normOutcomePart.Contains("not") || normOutcomePart.Contains("under");
                    if (outcomeIsNeg == ctx.WantsUnder)
                    {
                        isOutcomeMatch = true;
                    }
                }
            }

            if (!isOutcomeMatch && normOutcomePart.EndsWith("+") && !ctx.WantsUnder)
            {
                if (int.TryParse(normOutcomePart.TrimEnd('+'), out int plusVal))
                {
                    double eqLine = plusVal >= 1 ? plusVal - 0.5 : plusVal;
                    if (ctx.TargetLine.HasValue && Math.Abs(ctx.TargetLine.Value - eqLine) < 0.001)
                    {
                        isOutcomeMatch = true;
                    }
                    else if (!ctx.TargetLine.HasValue && !ctx.HasHigherLineReq && plusVal == 1)
                    {
                        isOutcomeMatch = true;
                    }
                }
            }

            return isOutcomeMatch;
        }

        public static bool CheckPlayerInitialsMatch(string firstNameFromOdds, string sel, string lastName)
        {
            if (string.IsNullOrWhiteSpace(firstNameFromOdds) || string.IsNullOrWhiteSpace(sel)) return false;

            string normSel = TeamAliasMappingService.RemoveDiacritics(sel).ToLowerInvariant();
            string cleanFirst = TeamAliasMappingService.RemoveDiacritics(firstNameFromOdds).ToLowerInvariant().Replace(".", "").Replace(" ", "").Replace("-", "");

            string cleanSel = Regex.Replace(normSel, @"\b(over|under|yes|no|total|points?|goals?|assists?|shots?|saves?)\b", " ");
            cleanSel = Regex.Replace(cleanSel, @"\d+(?:\.\d+)?", " ");
            cleanSel = Regex.Replace(cleanSel, @"[.+\-/:()"",]", " ");

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

            string norm1 = TeamAliasMappingService.RemoveDiacritics(str1).ToLowerInvariant();
            string norm2 = TeamAliasMappingService.RemoveDiacritics(str2).ToLowerInvariant();

            static string InjectImplicitOdds(string norm)
            {
                norm = StripTwoWayMarketPhrases(norm);
                norm = NormalizeLinePhrases(norm);
                norm = Regex.Replace(
                    norm,
                    @"\bwill\s+(not\s+)?(?=score\b|get\b|be\b|assist\b|give\b|provide\b|record\b|have\b|make\b|receive\b|hit\b|win\b)",
                    "$1",
                    RegexOptions.IgnoreCase);

                bool isNegative = Regex.IsMatch(norm, @"\b(?:under|no|nei|not)\b", RegexOptions.IgnoreCase) &&
                                  !Regex.IsMatch(norm, @"\b(?:over|yes|ja)\b", RegexOptions.IgnoreCase);
                string dir = isNegative ? "under" : "over";

                norm = Regex.Replace(norm, @"(\d+)\+", m => $"{dir} {double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) - 0.5}", RegexOptions.IgnoreCase);
                norm = Regex.Replace(norm, @"(?:to score\s+|to record\s+|to get\s+|to have\s+)?(\d+)\s+or\s+more", m => $"{dir} {double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) - 0.5}", RegexOptions.IgnoreCase);
                norm = Regex.Replace(norm, @"(?:at\s+least\s+)(\d+)", m => $"{dir} {double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) - 0.5}", RegexOptions.IgnoreCase);

                if (!norm.Contains("over ") && !norm.Contains("under ") &&
                    Regex.IsMatch(norm, @"\b(?:to\s+provide|to\s+give|to\s+assist|assist|assists|to\s+score|score|scorer|goalscorer|to\s+record|to\s+get|to\s+receive|to\s+be|anytime|carded|booked|yes|ja|no|nei)\b", RegexOptions.IgnoreCase))
                {
                    return !isNegative ? norm + " over 0.5" : norm + " under 0.5";
                }
                return norm;
            }

            norm1 = InjectImplicitOdds(norm1);
            norm2 = InjectImplicitOdds(norm2);

            // Handle "power sub", "super sub", "power play", and period ordinal phrases before number/token extraction
            norm1 = _powerSubRegex.Replace(norm1, "");
            norm2 = _powerSubRegex.Replace(norm2, "");
            norm1 = Regex.Replace(norm1, @"\b(?:power\s*play|1st|2nd|3rd|4th)\b", " ", RegexOptions.IgnoreCase);
            norm2 = Regex.Replace(norm2, @"\b(?:power\s*play|1st|2nd|3rd|4th)\b", " ", RegexOptions.IgnoreCase);

            // 1. Extract and compare numbers
            var numRegex = new Regex(@"([+-]?\d+(?:\.\d+)?)");
            var nums1 = numRegex.Matches(norm1).Cast<Match>().Select(m => m.Value).ToList();
            var nums2 = numRegex.Matches(norm2).Cast<Match>().Select(m => m.Value).ToList();

            if (nums1.Any() && nums2.Any())
            {
                if (double.TryParse(nums1.Last(), NumberStyles.Any, CultureInfo.InvariantCulture, out double n1) &&
                    double.TryParse(nums2.Last(), NumberStyles.Any, CultureInfo.InvariantCulture, out double n2))
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
                var signedRegex = new Regex(@"^[+-]\d+(?:\.\d+)?$");
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

            bool hasYes1 = allTokens1.Contains("yes") || allTokens1.Contains("ja");
            bool hasNo1 = allTokens1.Contains("no") || allTokens1.Contains("not") || allTokens1.Contains("nei");
            bool hasYes2 = allTokens2.Contains("yes") || allTokens2.Contains("ja");
            bool hasNo2 = allTokens2.Contains("no") || allTokens2.Contains("not") || allTokens2.Contains("nei");

            if ((hasYes1 || hasNo1) && (hasYes2 || hasNo2))
            {
                if (hasYes1 != hasYes2 || hasNo1 != hasNo2) return false;
            }

            bool isPos1 = (hasOver1 || hasYes1) && !hasUnder1 && !hasNo1;
            bool isNeg1 = (hasUnder1 || hasNo1) && !hasOver1 && !hasYes1;
            bool isPos2 = (hasOver2 || hasYes2) && !hasUnder2 && !hasNo2;
            bool isNeg2 = (hasUnder2 || hasNo2) && !hasOver2 && !hasYes2;

            if (isNeg1 != isNeg2 || (isPos1 && isNeg2) || (isNeg1 && isPos2))
            {
                return false;
            }

            // 3. Compare name tokens
            var ignoreWords = new HashSet<string>
            {
                "over", "under", "yes", "no", "ja", "nei", "not",
                "player", "players", "shots", "shot", "target", "score", "scorer", "scoring",
                "anytime", "goalscorer", "goal", "goals", "mal",
                "fouls", "foul", "committed", "suffered", "won", "drawn", "fouled", "win",
                "assists", "assist", "give", "provide",
                "cards", "card", "booked", "carded", "booking", "bookings", "shown", "receive", "yellow", "red",
                "points", "point", "pts", "ppp",
                "saves", "save", "goalkeeper", "keeper",
                "tackles", "tackle", "offsides", "offside", "passes", "pass",
                "make", "have", "get", "record", "hit",
                "to", "be", "a", "an", "the", "on", "in", "of", "from", "by", "with", "for", "during", "at", "least", "more", "or", "and",
                "total", "totals", "including", "incl", "inc", "overtime", "ot", "regular", "time", "full", "fulltime", "ft", "half", "halftime", "ht",
                "first", "second", "last", "header", "headers", "outside", "inside", "box", "penalty", "penalties", "shootout",
                "sub", "substitute", "super", "match", "game", "period", "touchdown", "touchdowns", "td"
            };

            var tokens1 = allTokens1
                .Where(t => t.Length >= 1 && !ignoreWords.Contains(t) && !double.TryParse(t, out _))
                .ToList();

            var tokens2 = allTokens2
                .Where(t => t.Length >= 1 && !ignoreWords.Contains(t) && !double.TryParse(t, out _))
                .ToList();

            if (!tokens1.Any() || !tokens2.Any()) return false;

            if (AreNameTokensMatched(tokens1, tokens2)) return true;
            if (AreNameTokensMatched(tokens2, tokens1)) return true;

            return false;
        }

        private static bool AreNameTokensMatched(List<string> sourceTokens, List<string> targetTokens)
        {
            var remainingTargets = targetTokens.ToList();
            var unmatchedSources = new List<string>();
            int exactOrInitialMatches = 0;

            foreach (var token in sourceTokens)
            {
                int matchIdx = remainingTargets.IndexOf(token);
                if (matchIdx >= 0)
                {
                    exactOrInitialMatches++;
                    remainingTargets.RemoveAt(matchIdx);
                }
                else if (token.Length == 1) // Single initial
                {
                    int initIdx = remainingTargets.FindIndex(o => o.StartsWith(token));
                    if (initIdx >= 0)
                    {
                        exactOrInitialMatches++;
                        remainingTargets.RemoveAt(initIdx);
                    }
                    else
                    {
                        unmatchedSources.Add(token);
                    }
                }
                else if (token.Length <= 3 && token.All(char.IsLetter)) // Multi-initial like "jj"
                {
                    var matchingIndices = new List<int>();
                    int charIdx = 0;
                    for (int j = 0; j < remainingTargets.Count && charIdx < token.Length; j++)
                    {
                        if (remainingTargets[j].StartsWith(token[charIdx].ToString()))
                        {
                            matchingIndices.Add(j);
                            charIdx++;
                        }
                    }
                    if (charIdx == token.Length)
                    {
                        exactOrInitialMatches++;
                        foreach (var idx in matchingIndices.OrderByDescending(x => x))
                        {
                            remainingTargets.RemoveAt(idx);
                        }
                    }
                    else
                    {
                        unmatchedSources.Add(token);
                    }
                }
                else
                {
                    unmatchedSources.Add(token);
                }
            }

            // Second pass: if at least one token (e.g. surname) matched and both sides have multiple tokens,
            // allow shortened first names / nicknames of length >= 3 (e.g. "Mitch" <-> "Mitchell", "Alex" <-> "Alexander")
            if (unmatchedSources.Any() && exactOrInitialMatches >= 1 && sourceTokens.Count >= 2 && targetTokens.Count >= 2)
            {
                foreach (var token in unmatchedSources.ToList())
                {
                    if (token.Length >= 3)
                    {
                        int prefixIdx = remainingTargets.FindIndex(o => o.Length >= 3 && (o.StartsWith(token) || token.StartsWith(o)));
                        if (prefixIdx >= 0)
                        {
                            unmatchedSources.Remove(token);
                            remainingTargets.RemoveAt(prefixIdx);
                        }
                    }
                }
            }

            return unmatchedSources.Count == 0;
        }
    }
}
