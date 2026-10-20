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
                        var numMatch = Regex.Match(cleanOutcome, @"\d+(?:\.\d+)?");
                        if (numMatch.Success && double.TryParse(numMatch.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedOutcomeLine))
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
                isOutcomeMatch = (string.IsNullOrEmpty(normOutcomePart) && !ctx.WantsUnder) ||
                                 ctx.NormSel.Contains(normOutcomePart) ||
                                 (normOutcomePart == "yes" && !ctx.WantsUnder && !ctx.NormSel.Contains("no") && !ctx.NormSel.Contains("under")) ||
                                 (normOutcomePart == "no" && ctx.WantsUnder && !ctx.HasHigherLineReq) ||
                                 (normOutcomePart == "over0.5" && !ctx.WantsUnder && ctx.NormSel.Contains("yes") && !ctx.NormSel.Contains("no") && !ctx.NormSel.Contains("under") && !ctx.HasHigherLineReq) ||
                                 (normOutcomePart == "under0.5" && ctx.WantsUnder && !ctx.HasHigherLineReq);
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
                    else if (!ctx.TargetLine.HasValue)
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
                bool isNegative = Regex.IsMatch(norm, @"\b(?:under|no|nei|not)\b", RegexOptions.IgnoreCase) &&
                                  !Regex.IsMatch(norm, @"\b(?:over|yes|ja)\b", RegexOptions.IgnoreCase);
                string dir = isNegative ? "under" : "over";

                norm = Regex.Replace(norm, @"(\d+)\+", m => $"{dir} {double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) - 0.5}", RegexOptions.IgnoreCase);
                norm = Regex.Replace(norm, @"(?:to score\s+|to record\s+|to get\s+|to have\s+)?(\d+)\s+or\s+more", m => $"{dir} {double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) - 0.5}", RegexOptions.IgnoreCase);
                norm = Regex.Replace(norm, @"(?:at\s+least\s+)(\d+)", m => $"{dir} {double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) - 0.5}", RegexOptions.IgnoreCase);

                if (!norm.Contains("over ") && !norm.Contains("under ") &&
                    (norm.Contains("to provide") || norm.Contains("to score") || norm.Contains("to record") || norm.Contains("to get") || norm.Contains("anytime") || norm.Contains("carded") || norm.Contains("booked")))
                {
                    return !isNegative ? norm + " over 0.5" : norm + " under 0.5";
                }
                return norm;
            }

            norm1 = InjectImplicitOdds(norm1);
            norm2 = InjectImplicitOdds(norm2);

            // Handle "power sub" or "super sub" promotions as whole phrases using word boundaries
            norm1 = _powerSubRegex.Replace(norm1, "");
            norm2 = _powerSubRegex.Replace(norm2, "");

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

            bool hasYes1 = allTokens1.Contains("yes");
            bool hasNo1 = allTokens1.Contains("no") || allTokens1.Contains("not") || allTokens1.Contains("nei");
            bool hasYes2 = allTokens2.Contains("yes");
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
                "over", "under", "yes", "no", "player", "shots", "target", "score", "anytime",
                "goalscorer", "fouls", "assists", "assist", "cards", "booked", "carded", "booking",
                "points", "provide", "make", "have", "to", "be", "shown", "a", "card", "on"
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
            int matchCount = 0;

            foreach (var token in sourceTokens)
            {
                int matchIdx = remainingTargets.IndexOf(token);
                if (matchIdx >= 0)
                {
                    matchCount++;
                    remainingTargets.RemoveAt(matchIdx);
                }
                else if (token.Length == 1) // Single initial
                {
                    int initIdx = remainingTargets.FindIndex(o => o.StartsWith(token));
                    if (initIdx >= 0)
                    {
                        matchCount++;
                        remainingTargets.RemoveAt(initIdx);
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
                        matchCount++;
                        foreach (var idx in matchingIndices.OrderByDescending(x => x))
                        {
                            remainingTargets.RemoveAt(idx);
                        }
                    }
                }
            }

            return matchCount == sourceTokens.Count;
        }
    }
}
