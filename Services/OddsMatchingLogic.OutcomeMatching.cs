using System;
using System.Globalization;
using System.Text.RegularExpressions;
using BettingApp.Models;

namespace BettingApp.Services
{
    // Strategy-routed outcome matching pipeline (HT/FT, 1X2 & Handicap, Pure Over/Under, Player Props, and Fallback).
    public static partial class OddsMatchingLogic
    {
        public static bool IsOutcomeMatch(string oName, string displayOName, AiVisionLeg? ActiveLookupLeg, OddsPapiSearchResult? PapiSearchResult, string? opMarketName = null)
        {
            var ctx = new OutcomeMatchingContext(ActiveLookupLeg, PapiSearchResult, opMarketName);
            return IsOutcomeMatch(oName, displayOName, ctx);
        }

        public static bool IsOutcomeMatch(string oName, string displayOName, OutcomeMatchingContext ctx)
        {
            if (ctx.Leg == null || string.IsNullOrEmpty(ctx.Leg.Selection)) return false;

            // Guard: Reject composite combination bets (e.g., "Player to Score or be Booked") against single-stat markets
            if (IsIncompatibleCombinationBet(ctx))
            {
                return false;
            }

            // Strategy 1: Half-Time / Full-Time ("1/1", "1/X", "Tottenham / Draw")
            bool? htftResult = TryMatchHalfTimeFullTime(oName, displayOName, ctx);
            if (htftResult.HasValue)
            {
                return htftResult.Value;
            }

            // Strategy 2: 1X2, Double Chance, & Handicap ("1", "X", "2", "1X", "2 (-0.5)", "1 (0-2.0)")
            bool? hcOr1X2Result = TryMatchHandicapOr1X2(oName, displayOName, ctx);
            if (hcOr1X2Result.HasValue)
            {
                return hcOr1X2Result.Value;
            }

            // Strategy 3: Pure Over/Under Totals ("Over 2.5", "Under 1.5", "Over 2.5 (0:0)")
            bool? pureOverUnderResult = TryMatchPureOverUnder(oName, displayOName, ctx);
            if (pureOverUnderResult.HasValue)
            {
                return pureOverUnderResult.Value;
            }

            // Strategy 4: Structured Player Props ("Over 1.5 (Last, First)", "2+ (Openda, Lois)", "Over 1.5 (Player Name)")
            bool? playerPropResult = TryMatchPlayerProp(oName, displayOName, ctx);
            if (playerPropResult.HasValue)
            {
                return playerPropResult.Value;
            }

            // Strategy 5: Direct Normalized, Numbered, or Fuzzy Fallback (BTTS Yes/No, non-1X2 team names, unparenthesized player props)
            return TryMatchFallbackOutcome(oName, displayOName, ctx);
        }

        /// <summary>
        /// Guard against composite player/team combination bets matching single-component markets.
        /// </summary>
        private static bool IsIncompatibleCombinationBet(OutcomeMatchingContext ctx)
        {
            bool selIsScoreAndBooked =
                ((ctx.NormMarket.Contains("score") || ctx.NormMarket.Contains("goal")) && (ctx.NormMarket.Contains("book") || ctx.NormMarket.Contains("card"))) ||
                ((ctx.NormSelRaw.Contains("score") || ctx.NormSelRaw.Contains("goal")) && (ctx.NormSelRaw.Contains("book") || ctx.NormSelRaw.Contains("card")));

            if (selIsScoreAndBooked)
            {
                string opMkt = ctx.OpMarketName ?? "";
                bool opHasScore = opMkt.Contains("score", StringComparison.OrdinalIgnoreCase) || opMkt.Contains("goal", StringComparison.OrdinalIgnoreCase);
                bool opHasBooked = opMkt.Contains("book", StringComparison.OrdinalIgnoreCase) || opMkt.Contains("card", StringComparison.OrdinalIgnoreCase);
                if (!opHasScore || !opHasBooked)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Strategy 1: Evaluates Half-Time / Full-Time outcomes and selections.
        /// Returns true/false definitively when HT/FT is involved, or null if neither outcome nor selection is HT/FT.
        /// </summary>
        private static bool? TryMatchHalfTimeFullTime(string oName, string displayOName, OutcomeMatchingContext ctx)
        {
            string opHtftTarget = ResolveOutcomeHtftTarget(oName, displayOName, ctx);

            if (!string.IsNullOrEmpty(ctx.HtftTarget) && !string.IsNullOrEmpty(opHtftTarget))
            {
                return string.Equals(opHtftTarget, ctx.HtftTarget, StringComparison.OrdinalIgnoreCase);
            }

            if (!string.IsNullOrEmpty(ctx.HtftTarget) && string.Equals(oName, ctx.HtftTarget, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (ctx.NormSelRaw.Contains("/") && !string.IsNullOrEmpty(displayOName) && displayOName.Contains("/"))
            {
                var selParts = ctx.NormSelRaw.Split('/', StringSplitOptions.RemoveEmptyEntries);
                string normDispRaw = TeamAliasMappingService.RemoveDiacritics(displayOName).ToLowerInvariant();
                var dispParts = normDispRaw.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (selParts.Length == 2 && dispParts.Length == 2)
                {
                    string s1 = selParts[0].Trim();
                    string s2 = selParts[1].Trim();
                    string d1 = dispParts[0].Trim();
                    string d2 = dispParts[1].Trim();
                    bool p1Match = s1 == d1 || TeamAliasMappingService.IsTeamMatch(s1, d1) || (_drawTieRegex.IsMatch(s1) && _drawTieRegex.IsMatch(d1));
                    bool p2Match = s2 == d2 || TeamAliasMappingService.IsTeamMatch(s2, d2) || (_drawTieRegex.IsMatch(s2) && _drawTieRegex.IsMatch(d2));
                    return p1Match && p2Match;
                }
            }

            if (!string.IsNullOrEmpty(ctx.HtftTarget) || !string.IsNullOrEmpty(opHtftTarget))
            {
                return false;
            }

            return null;
        }

        private static string ResolveOutcomeHtftTarget(string oName, string displayOName, OutcomeMatchingContext ctx)
        {
            string cleanOName = (oName ?? "").Replace(" ", "");
            if (_htftRegex.IsMatch(cleanOName))
            {
                return cleanOName.ToUpperInvariant();
            }

            if (!string.IsNullOrEmpty(oName) && (oName.Contains("/") || oName.Contains(" - ")))
            {
                string normONameRaw = TeamAliasMappingService.RemoveDiacritics(oName).ToLowerInvariant();
                string[]? opHtftSplit = normONameRaw.Contains("/")
                    ? normONameRaw.Split('/', StringSplitOptions.RemoveEmptyEntries)
                    : normONameRaw.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);

                if (opHtftSplit.Length == 2)
                {
                    string ht = opHtftSplit[0].Trim();
                    string ft = opHtftSplit[1].Trim();
                    int htTeam = TeamAliasMappingService.ResolveTeamSelection(ht, ctx.NormT1, ctx.NormT2, ctx.TargetOp1, ctx.TargetOp2);
                    int ftTeam = TeamAliasMappingService.ResolveTeamSelection(ft, ctx.NormT1, ctx.NormT2, ctx.TargetOp1, ctx.TargetOp2);
                    string htCode = ResolveHtftCode(ht, htTeam);
                    string ftCode = ResolveHtftCode(ft, ftTeam);
                    if (!string.IsNullOrEmpty(htCode) && !string.IsNullOrEmpty(ftCode))
                    {
                        return $"{htCode}/{ftCode}";
                    }
                }
            }

            if (!string.IsNullOrEmpty(displayOName) && displayOName.Contains("/"))
            {
                string normDispRaw = TeamAliasMappingService.RemoveDiacritics(displayOName).ToLowerInvariant();
                var dispSplit = normDispRaw.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (dispSplit.Length == 2)
                {
                    string ht = dispSplit[0].Trim();
                    string ft = dispSplit[1].Trim();
                    int htTeam = TeamAliasMappingService.ResolveTeamSelection(ht, ctx.NormT1, ctx.NormT2, ctx.TargetOp1, ctx.TargetOp2);
                    int ftTeam = TeamAliasMappingService.ResolveTeamSelection(ft, ctx.NormT1, ctx.NormT2, ctx.TargetOp1, ctx.TargetOp2);
                    string htCode = ResolveHtftCode(ht, htTeam);
                    string ftCode = ResolveHtftCode(ft, ftTeam);
                    if (!string.IsNullOrEmpty(htCode) && !string.IsNullOrEmpty(ftCode))
                    {
                        return $"{htCode}/{ftCode}";
                    }
                }
            }

            return "";
        }

        /// <summary>
        /// Strategy 2: Evaluates 1X2, Double Chance ("1X", "X2", "12"), and Handicap ("1 (-0.5)", "2 (-0.5)", "1 (0-2.0)") outcomes.
        /// Never falls through to loose substring matching when a handicap value or team code differs.
        /// </summary>
        private static bool? TryMatchHandicapOr1X2(string oName, string displayOName, OutcomeMatchingContext ctx)
        {
            var basicHcMatch = _basicHcMatchRegex.Match(oName ?? "");
            if (basicHcMatch.Success)
            {
                string opTarget = basicHcMatch.Groups[1].Value.ToUpperInvariant();
                if (opTarget == "2X") opTarget = "X2";
                if (opTarget == "X1") opTarget = "1X";

                string hcStr = basicHcMatch.Groups[2].Value.Trim();
                double? opHc = null;
                if (!string.IsNullOrEmpty(hcStr) && TryParseOddsPapiHandicap(opTarget, hcStr, out double parsedOpHc))
                {
                    opHc = parsedOpHc;
                }

                // A handicap outcome cannot match a non-handicap bet, and vice versa
                double? expectedHc = ctx.AiHc ?? (ctx.HasSelHc ? ctx.SelHcVal : null);
                if (opHc.HasValue != expectedHc.HasValue)
                {
                    return false;
                }

                if (opHc.HasValue && expectedHc.HasValue && Math.Abs(opHc.Value - expectedHc.Value) >= 0.001)
                {
                    return false;
                }

                if (!string.IsNullOrEmpty(ctx.AiTarget))
                {
                    string normAiTarget = ctx.AiTarget;
                    if (normAiTarget == "2X") normAiTarget = "X2";
                    if (normAiTarget == "X1") normAiTarget = "1X";

                    return normAiTarget == opTarget;
                }

                return null;
            }

            // Guard for explicit signed handicaps when oName is not in "1 (hc)" format
            var mONameHc = _hcSignRegex.Match(displayOName ?? "");
            bool hasOpHc = mONameHc.Success && double.TryParse(mONameHc.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out _);
            double? betHc = ctx.AiHc ?? (ctx.HasSelHc ? ctx.SelHcVal : null);

            if (hasOpHc != betHc.HasValue)
            {
                return false;
            }

            if (hasOpHc && betHc.HasValue &&
                double.TryParse(mONameHc.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double valO))
            {
                if (Math.Abs(valO - betHc.Value) > 0.001)
                {
                    return false;
                }
            }

            return null;
        }

        /// <summary>
        /// Strategy 3: Evaluates pure Over/Under total outcomes (Match Goals, Team Goals, Corners, Cards, etc.).
        /// Returns a definitive true/false when the outcome is a pure Over/Under line, or null if it is not.
        /// </summary>
        private static bool? TryMatchPureOverUnder(string oName, string? displayOName, OutcomeMatchingContext ctx)
        {
            // If outcome has player parentheses (LastName, FirstName), it's a player prop, not a pure total
            if (_parenMatchRegex.IsMatch(displayOName ?? "")) return null;

            var simpleParen = _simpleParenMatchRegex.Match(displayOName ?? "");
            if (simpleParen.Success && !Regex.IsMatch(simpleParen.Groups[1].Value, @"^\d+:\d+$|^\d+-\d+$"))
            {
                if (!double.TryParse(simpleParen.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                {
                    return null;
                }
            }

            // Check if outcome has combo words like " and " or " & "
            string dispLower = (displayOName ?? "").ToLowerInvariant();
            if (dispLower.Contains(" and ") || dispLower.Contains(" & ") || dispLower.Contains(" og ")) return null;

            // Extract line and direction from outcome
            var opMatch = _overUnderLineRegex.Match(displayOName ?? "");
            if (!opMatch.Success && !string.IsNullOrEmpty(oName))
            {
                opMatch = _overUnderLineRegex.Match(oName);
            }
            if (!opMatch.Success) return null;

            // Ensure the outcome itself does not contain a player name outside the Over/Under expression
            string outcomeRemainder = _overUnderLineRegex.Replace(displayOName ?? oName ?? "", " ");
            outcomeRemainder = Regex.Replace(outcomeRemainder, @"\([0-9:.-]+\)", " ");
            outcomeRemainder = Regex.Replace(outcomeRemainder, @"[^\w]", "");
            if (outcomeRemainder.Length > 2)
            {
                // Outcome contains extra words (e.g. "Erling Braut Haaland - Over 3.5"), so it's not a pure total outcome
                return null;
            }

            // Check if selection has a player name
            string selRemaining = GetRemainingSelectionTextWithoutTotalKeywords(ctx);
            if (selRemaining.Length > 2)
            {
                // Selection has a player name. Only allow if OpMarketName itself is named after the player (handled in Strategy 4/5)
                if (!string.IsNullOrEmpty(ctx.NormOpMarket) && ctx.PlayerNameFromAiSel.Length > 3 &&
                    (ctx.NormOpMarket.Contains(ctx.PlayerNameFromAiSel) || ctx.PlayerNameFromAiSel.Contains(ctx.NormOpMarket) ||
                     IsFuzzyPlayerMatch(ctx.OpMarketName ?? "", ctx.Leg?.Selection ?? "")))
                {
                    return null;
                }

                return false;
            }

            if (!ctx.TargetLine.HasValue) return false;

            // Team alignment check for Team 1 / Team 2 markets
            string combinedLeg = $"{(ctx.Leg?.Market ?? "")} {(ctx.Leg?.Selection ?? "")}";
            int legTeam = TeamAliasMappingService.ResolveTeamSelection(combinedLeg, ctx.NormT1, ctx.NormT2, ctx.TargetOp1, ctx.TargetOp2);
            string normOp = ctx.NormOpMarket ?? "";
            if (legTeam == 1 && normOp.Contains("team2")) return false;
            if (legTeam == 2 && normOp.Contains("team1")) return false;

            bool opIsUnder = opMatch.Groups[1].Value.Equals("under", StringComparison.OrdinalIgnoreCase);
            if (opIsUnder != ctx.WantsUnder) return false;

            if (double.TryParse(opMatch.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double opLine))
            {
                return Math.Abs(opLine - ctx.TargetLine.Value) < 0.001;
            }

            return false;
        }

        private static string GetRemainingSelectionTextWithoutTotalKeywords(OutcomeMatchingContext ctx)
        {
            string selRemaining = ctx.NormSelRaw ?? "";
            selRemaining = Regex.Replace(
                selRemaining,
                @"\b(?:over|under|o\/u|goals?|mål|mal|scoringer|points?|poeng|pts|cards?|kort|corners?|hjørnespark|hjornespark|shots?|skudd|saves?|redninger|tackles?|taklinger|fouls?|frispark|runs?|games?|sets?|innings?|frames?|power\s*sub|super\s*sub|total|totalt|antall|full\s*time|ft|regular\s*time|incl|overtime|penalties|ot|at\s+least|or\s+more)\b",
                " ",
                RegexOptions.IgnoreCase);
            selRemaining = Regex.Replace(selRemaining, @"\d+(?:\.\d+)?", " ");
            selRemaining = Regex.Replace(selRemaining, @"[^\w\s]", " ");
            if (!string.IsNullOrEmpty(ctx.NormT1))
            {
                selRemaining = Regex.Replace(selRemaining, $@"\b{Regex.Escape(ctx.NormT1)}\b", " ", RegexOptions.IgnoreCase);
            }
            if (!string.IsNullOrEmpty(ctx.NormT2))
            {
                selRemaining = Regex.Replace(selRemaining, $@"\b{Regex.Escape(ctx.NormT2)}\b", " ", RegexOptions.IgnoreCase);
            }
            return Regex.Replace(selRemaining, @"\s+", "").Trim();
        }

        /// <summary>
        /// Strategy 4: Evaluates structured parenthesized player props ("Over 1.5 (Last, First)", "2+ (Last, First)", "Over 1.5 (Player Name)").
        /// Returns a definitive true/false when the outcome has player parentheses, or null if not.
        /// </summary>
        private static bool? TryMatchPlayerProp(string oName, string displayOName, OutcomeMatchingContext ctx)
        {
            // 4A. "(LastName, FirstName)" format
            var parenMatch = _parenMatchRegex.Match(displayOName ?? "");
            if (parenMatch.Success)
            {
                string outcomePart = (displayOName ?? "").Substring(0, parenMatch.Index).Trim();
                string normOutcomePart = _dotZeroRegex.Replace(
                    TeamAliasMappingService.RemoveDiacritics(outcomePart).ToLowerInvariant()
                        .Replace(" ", "").Replace("(", "").Replace(")", "").Replace(":", "-"),
                    "");

                // Line and direction MUST match first; never allow wrong line/direction to fall through
                if (!IsPlayerPropOutcomeMatchFast(normOutcomePart, ctx))
                {
                    return false;
                }

                string lastName = TeamAliasMappingService.RemoveDiacritics(parenMatch.Groups[1].Value).ToLowerInvariant().Replace(" ", "");
                string firstName = TeamAliasMappingService.RemoveDiacritics(parenMatch.Groups[2].Value).ToLowerInvariant().Replace(" ", "");

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

                bool isValidNameMatch = matchLast && matchFirst;

                if (!isValidNameMatch && (matchLast || matchFirst))
                {
                    string matchedName = matchLast ? lastName : firstName;
                    string playerPartOnly = Regex.Replace(
                        ctx.NormSel.Replace(normOutcomePart, "").Replace("over", "").Replace("under", "").Replace("yes", "").Replace("no", "").Replace("-", ""),
                        @"\d+(\.\d+)?",
                        "");
                    string leftover = playerPartOnly.Replace(matchedName, "");
                    if (leftover.Length <= 5)
                    {
                        isValidNameMatch = true;
                    }
                }

                if (isValidNameMatch)
                {
                    return true;
                }

                // Only allow FallbackSel fuzzy matching if the selection itself has a player name (or market has player name not matching team names)
                if (GetRemainingSelectionTextWithoutTotalKeywords(ctx).Length <= 2 &&
                    (ctx.NormSel.StartsWith("over") || ctx.NormSel.StartsWith("under")) &&
                    !ctx.NormMarket.Contains(lastName) && !ctx.NormMarket.Contains(firstName))
                {
                    return false;
                }

                return IsFuzzyPlayerMatch(ctx.FallbackSel, displayOName ?? "");
            }

            // 4B. "(PlayerName)" format at end of string (where content inside parens is not a number or scoreline)
            var simpleParenMatch = _simpleParenMatchRegex.Match(displayOName ?? "");
            if (simpleParenMatch.Success)
            {
                string inner = simpleParenMatch.Groups[1].Value.Trim();
                bool isScoreOrNumber = Regex.IsMatch(inner, @"^\d+:\d+$|^\d+-\d+$") ||
                                       double.TryParse(inner, NumberStyles.Any, CultureInfo.InvariantCulture, out _);

                if (!isScoreOrNumber)
                {
                    string outcomePart = (displayOName ?? "").Substring(0, simpleParenMatch.Index).Trim();
                    string normOutcomePart = _dotZeroRegex.Replace(
                        TeamAliasMappingService.RemoveDiacritics(outcomePart).ToLowerInvariant()
                            .Replace(" ", "").Replace("(", "").Replace(")", "").Replace(":", "-"),
                        "");

                    if (!IsPlayerPropOutcomeMatchFast(normOutcomePart, ctx))
                    {
                        return false;
                    }

                    string pName = TeamAliasMappingService.RemoveDiacritics(inner).ToLowerInvariant().Replace(" ", "");
                    if (ctx.NormSel.Contains(pName))
                    {
                        return true;
                    }

                    return IsFuzzyPlayerMatch(ctx.FallbackSel, displayOName ?? "");
                }
            }

            return null;
        }

        /// <summary>
        /// Strategy 5: Evaluates remaining non-parenthesized outcomes (BTTS Yes/No, player-in-market props, direct team names, unparenthesized player props).
        /// </summary>
        private static bool TryMatchFallbackOutcome(string oName, string displayOName, OutcomeMatchingContext ctx)
        {
            string displayONameSafe = _powerSubRegex.Replace(displayOName ?? "", "");
            string normOName = _dotZeroRegex.Replace(TeamAliasMappingService.StripToAlphaNumeric(displayONameSafe), "");
            bool isLineSel = ctx.NormSel.StartsWith("over") || ctx.NormSel.StartsWith("under") ||
                             normOName.StartsWith("over") || normOName.StartsWith("under");

            bool hasAnyHandicap = ctx.AiHc.HasValue || ctx.HasSelHc || _hcSignRegex.IsMatch(displayOName ?? "");

            // 5A. Exact normalized match or non-handicap team alias match
            if (normOName == ctx.NormSel)
            {
                return true;
            }

            bool outcomeHasNegative = normOName.Contains("no") || normOName.Contains("under");

            if (!isLineSel && !hasAnyHandicap && (!ctx.WantsUnder || outcomeHasNegative) &&
                !ctx.NumSelSuccess && !_numSuffixRegex.IsMatch(normOName) &&
                !string.IsNullOrEmpty(normOName) && !string.IsNullOrEmpty(ctx.NormSel) &&
                TeamAliasMappingService.IsTeamMatch(normOName, ctx.NormSel))
            {
                return true;
            }

            // 5B. Player name in Leg.Market with Yes/No/Over0.5 selection
            if ((ctx.NormSel == "yes" || ctx.NormSel == "no" || ctx.NormSel == "over05") && !string.IsNullOrEmpty(ctx.NormMarket))
            {
                if (outcomeHasNegative == ctx.WantsUnder)
                {
                    string playerNameFromOdds = normOName.Replace("yes", "").Replace("no", "").Replace("over05", "").Replace("under05", "");
                    if (playerNameFromOdds.Length > 3 && (ctx.NormMarket.Contains(playerNameFromOdds) || playerNameFromOdds.Contains(ctx.NormMarket)))
                    {
                        return true;
                    }
                }
            }

            // 5C. Player name in OpMarketName with Yes/Over0.5 or No/Under0.5 outcome
            if ((((normOName == "yes" || normOName == "over05") && !ctx.WantsUnder) ||
                 ((normOName == "no" || normOName == "under05") && ctx.WantsUnder)) &&
                !string.IsNullOrEmpty(ctx.NormOpMarket))
            {
                if (ctx.PlayerNameFromAiSel.Length > 3 &&
                    (ctx.NormOpMarket.Contains(ctx.PlayerNameFromAiSel) ||
                     ctx.PlayerNameFromAiSel.Contains(ctx.NormOpMarket) ||
                     IsFuzzyPlayerMatch(ctx.OpMarketName ?? "", ctx.Leg?.Selection ?? "")))
                {
                    return true;
                }
            }

            // 5D. Numbered suffix comparison or substring comparison (only when no handicap is present)
            if (!hasAnyHandicap)
            {
                var numOName = _numSuffixRegex.Match(normOName);
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
                                if (baseOName == baseSel) return true;
                            }
                            else if (baseOName.Contains(baseSel) || baseSel.Contains(baseOName) || TeamAliasMappingService.IsTeamMatch(baseOName, baseSel))
                            {
                                return true;
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
                            if (!string.IsNullOrEmpty(ctx.NormMarket) && !string.IsNullOrEmpty(playerNameFromOdds) &&
                                (ctx.NormMarket.Contains(playerNameFromOdds) || playerNameFromOdds.Contains(ctx.NormMarket)))
                            {
                                return true;
                            }
                        }
                        else
                        {
                            return true;
                        }
                    }
                    else if (ctx.NormSel.Contains(normOName))
                    {
                        if (!normOName.StartsWith("over") && !normOName.StartsWith("under") && (!ctx.WantsUnder || outcomeHasNegative))
                        {
                            if (normOName.Length >= 4)
                            {
                                return true;
                            }
                            if (ctx.NormSel.EndsWith("-" + normOName) || ctx.NormSel.StartsWith(normOName + "-") || ctx.NormSel.Contains("-" + normOName + "-"))
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            // 5E. Final fallback: Fuzzy Player Match for unparenthesized player prop outcomes
            if (!string.IsNullOrEmpty(displayOName) && ctx.Leg != null && !string.IsNullOrEmpty(ctx.Leg.Selection))
            {
                return IsFuzzyPlayerMatch(ctx.FallbackSel, displayOName);
            }

            return false;
        }
    }
}
