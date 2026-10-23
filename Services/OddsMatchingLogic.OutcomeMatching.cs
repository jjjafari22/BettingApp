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

            // Strategy 1B: Exact Count ("Exact Sets") and Correct Score / Set Betting ("2:0", "0:2", "6:4")
            bool? exactOrScoreResult = TryMatchExactCountOrScore(oName, displayOName, ctx);
            if (exactOrScoreResult.HasValue)
            {
                return exactOrScoreResult.Value;
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
            bool isHtftMarket = IsHalfTimeFullTimeMarket(ctx.Leg?.Market) || IsHalfTimeFullTimeMarket(ctx.OpMarketName);
            string cleanOName = (oName ?? "").Replace(" ", "");
            bool isHtftOutcome = _htftRegex.IsMatch(cleanOName);

            if (!isHtftMarket && !isHtftOutcome)
            {
                return null;
            }

            string opHtftTarget = ResolveOutcomeHtftTarget(oName ?? "", displayOName, ctx);

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
        /// Strategy 1B: Evaluates Exact Count markets (e.g. "Exact Sets" with outcomes "2", "3", "4", "5")
        /// and Correct Score / Set Betting markets (outcomes "2:0", "0:2", "2:1", "6:4", etc.).
        /// </summary>
        private static bool? TryMatchExactCountOrScore(string oName, string displayOName, OutcomeMatchingContext ctx)
        {
            string trimmedOName = (oName ?? "").Trim();

            // 1. Exact Count markets (e.g. "Exact Sets", "Exact Aces", "Exact Double Faults", "Exact Goals")
            if (!string.IsNullOrEmpty(ctx.OpMarketName) &&
                ctx.OpMarketName.StartsWith("Exact ", StringComparison.OrdinalIgnoreCase))
            {
                if (Regex.IsMatch(trimmedOName, @"^\d+\+?$"))
                {
                    if (ctx.NormSelRaw.Contains("over") || ctx.NormSelRaw.Contains("under") ||
                        Regex.IsMatch(ctx.NormSelRaw, @"\b\d+\.\d+\b"))
                    {
                        return false;
                    }

                    string selText = ctx.NormSelRaw
                        .Replace("two", "2")
                        .Replace("three", "3")
                        .Replace("four", "4")
                        .Replace("five", "5");

                    var mInt = Regex.Match(selText, @"\b(\d+\+?)\b");
                    if (mInt.Success)
                    {
                        return string.Equals(mInt.Groups[1].Value, trimmedOName, StringComparison.OrdinalIgnoreCase);
                    }
                    return false;
                }
            }

            // 2. Correct Score / Set Betting outcomes ("2:0", "2:1", "0:2", "1:2", "6:4")
            var opScoreMatch = Regex.Match(trimmedOName, @"^(\d+):(\d+)$");
            if (opScoreMatch.Success &&
                int.TryParse(opScoreMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int opP1) &&
                int.TryParse(opScoreMatch.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int opP2))
            {
                if (ctx.HasSelHc || ctx.NormSelRaw.Contains("over") || ctx.NormSelRaw.Contains("under"))
                {
                    return false;
                }

                var selScoreMatch = Regex.Match(ctx.NormSelRaw, @"(?<![.\d])(\d+)\s*[-:]\s*(\d+)(?![.\d])");
                if (!selScoreMatch.Success ||
                    !int.TryParse(selScoreMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int s1) ||
                    !int.TryParse(selScoreMatch.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int s2))
                {
                    return false;
                }

                // Check if the selection also names a specific team/player (e.g. "Jiri Lehecka 2-0" or "Arsenal 2-1")
                string selWithoutScore = (ctx.Leg?.Selection ?? "");
                selWithoutScore = Regex.Replace(selWithoutScore, @"(?<![.\d])\d+\s*[-:]\s*\d+(?![.\d])", " ").Trim();
                int namedTeam = !string.IsNullOrWhiteSpace(selWithoutScore)
                    ? TeamAliasMappingService.ResolveTeamSelection(selWithoutScore, ctx.NormT1, ctx.NormT2, ctx.TargetOp1, ctx.TargetOp2)
                    : 0;

                int expectedOpP1;
                int expectedOpP2;

                if (namedTeam == 1)
                {
                    // Named team is OddsPapi Participant 1 -> they are the winner of the stated scoreline
                    expectedOpP1 = Math.Max(s1, s2);
                    expectedOpP2 = Math.Min(s1, s2);
                }
                else if (namedTeam == 2)
                {
                    // Named team is OddsPapi Participant 2 -> they are the winner of the stated scoreline
                    expectedOpP1 = Math.Min(s1, s2);
                    expectedOpP2 = Math.Max(s1, s2);
                }
                else
                {
                    // Pure scoreline ("0-2", "1-2", "2:0"): order follows the match string (inverted if fixture participants are swapped)
                    expectedOpP1 = ctx.IsSwapped ? s2 : s1;
                    expectedOpP2 = ctx.IsSwapped ? s1 : s2;
                }

                return opP1 == expectedOpP1 && opP2 == expectedOpP2;
            }

            return null;
        }

        /// <summary>
        /// Strategy 2: Evaluates 1X2, Double Chance ("1X", "X2", "12"), and Handicap ("1 (-0.5)", "2 (-0.5)", "1 (0-2.0)") outcomes.
        /// Never falls through to loose substring matching when a handicap value or team code differs.
        /// </summary>
        private static bool? TryMatchHandicapOr1X2(string oName, string displayOName, OutcomeMatchingContext ctx)
        {
            if (!string.IsNullOrEmpty(ctx.OpMarketName) &&
                ctx.OpMarketName.StartsWith("Exact ", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

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

            double? effectiveTargetLine = ctx.TargetLine;
            if (!effectiveTargetLine.HasValue &&
                (ctx.NormOpMarket.Contains("tiebreak") || ctx.NormMarket.Contains("tiebreak")) &&
                (ctx.NormSel == "yes" || ctx.NormSel == "no" || ctx.NormSel == "ja" || ctx.NormSel == "nei"))
            {
                effectiveTargetLine = 0.5;
            }

            if (!effectiveTargetLine.HasValue) return false;

            // Team/Participant alignment check for Team 1 / Team 2 and Participant 1 / Participant 2 markets
            string combinedLeg = $"{(ctx.Leg?.Market ?? "")} {(ctx.Leg?.Selection ?? "")}";
            int legTeam = TeamAliasMappingService.ResolveTeamSelection(combinedLeg, ctx.NormT1, ctx.NormT2, ctx.TargetOp1, ctx.TargetOp2);
            string normOp = ctx.NormOpMarket ?? "";
            if (legTeam == 1 && (normOp.Contains("team2") || normOp.Contains("participant2"))) return false;
            if (legTeam == 2 && (normOp.Contains("team1") || normOp.Contains("participant1"))) return false;
            if (legTeam == 0 && (normOp.Contains("participant1") || normOp.Contains("participant2"))) return false;
            if (legTeam != 0 &&
                !normOp.Contains("participant1") && !normOp.Contains("participant2") &&
                !normOp.Contains("team1") && !normOp.Contains("team2") &&
                (normOp == "totalgamesoverunder" || normOp == "totalaces" || normOp == "totaldoublefaults" ||
                 normOp == "totalbreaks" || normOp == "totalbreakpoints" || normOp == "totalservicegames"))
            {
                return false;
            }

            bool opIsUnder = opMatch.Groups[1].Value.Equals("under", StringComparison.OrdinalIgnoreCase);
            if (opIsUnder != ctx.WantsUnder) return false;

            if (double.TryParse(opMatch.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double opLine))
            {
                return Math.Abs(opLine - effectiveTargetLine.Value) < 0.001;
            }

            return false;
        }

        private static string GetRemainingSelectionTextWithoutTotalKeywords(OutcomeMatchingContext ctx)
        {
            string selRemaining = ctx.NormSelRaw ?? "";
            selRemaining = Regex.Replace(
                selRemaining,
                @"\b(?:over|under|o\/u|yes|no|ja|nei|goals?|mål|mal|scoringer|points?|poeng|pts|cards?|kort|corners?|hjørnespark|hjornespark|shots?|skudd|saves?|redninger|tackles?|taklinger|fouls?|frispark|runs?|games?|sets?|aces?|double\s+faults?|faults?|breaks?|service|tie\s*breaks?|tiebreaks?|innings?|frames?|power\s*sub|super\s*sub|total|totalt|antall|full\s*time|ft|regular\s*time|incl|overtime|penalties|ot|at\s+least|or\s+more|1st|2nd|3rd|4th|5th|first|second|third|fourth|fifth|match|player\s*[12]|participant\s*[12]|won|number|of|in)\b",
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
            if (!string.IsNullOrEmpty(ctx.TargetOp1))
            {
                selRemaining = Regex.Replace(selRemaining, $@"\b{Regex.Escape(ctx.TargetOp1)}\b", " ", RegexOptions.IgnoreCase);
            }
            if (!string.IsNullOrEmpty(ctx.TargetOp2))
            {
                selRemaining = Regex.Replace(selRemaining, $@"\b{Regex.Escape(ctx.TargetOp2)}\b", " ", RegexOptions.IgnoreCase);
            }

            string candidateWords = Regex.Replace(selRemaining, @"\s+", " ").Trim();
            if (!string.IsNullOrEmpty(candidateWords) &&
                TeamAliasMappingService.ResolveTeamSelection(candidateWords, ctx.NormT1, ctx.NormT2, ctx.TargetOp1, ctx.TargetOp2) != 0)
            {
                return "";
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

                string lastName = TeamAliasMappingService.StripToAlphaNumeric(parenMatch.Groups[1].Value);
                string firstName = TeamAliasMappingService.StripToAlphaNumeric(parenMatch.Groups[2].Value);

                bool matchLast = !string.IsNullOrEmpty(lastName) && ctx.NormSel.Contains(lastName);
                bool matchFirst = !string.IsNullOrEmpty(firstName) && ctx.NormSel.Contains(firstName);

                if (!matchFirst && matchLast)
                {
                    matchFirst = CheckPlayerInitialsMatch(parenMatch.Groups[2].Value, ctx.Leg?.Selection ?? "", lastName);
                }
                else if (!matchLast && matchFirst)
                {
                    matchLast = CheckPlayerInitialsMatch(parenMatch.Groups[1].Value, ctx.Leg?.Selection ?? "", firstName);
                }

                bool isValidNameMatch = matchLast && matchFirst;

                if (!isValidNameMatch && (matchLast || matchFirst))
                {
                    string matchedName = matchLast ? lastName : firstName;
                    string playerPartOnly = Regex.Replace(
                        ctx.NormSel.Replace(normOutcomePart, "").Replace("over", "").Replace("under", "").Replace("yes", "").Replace("no", "").Replace("ja", "").Replace("nei", "").Replace("-", ""),
                        @"\d+(\.\d+)?",
                        "");
                    string leftover = playerPartOnly.Replace(matchedName, "");
                    if (leftover.Length == 0)
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
                    (ctx.NormSel.StartsWith("over") || ctx.NormSel.StartsWith("under")))
                {
                    string mktNoTeams = ctx.NormMarket;
                    if (!string.IsNullOrEmpty(ctx.NormT1)) mktNoTeams = mktNoTeams.Replace(ctx.NormT1.Replace(" ", ""), "");
                    if (!string.IsNullOrEmpty(ctx.NormT2)) mktNoTeams = mktNoTeams.Replace(ctx.NormT2.Replace(" ", ""), "");
                    if (!string.IsNullOrEmpty(ctx.TargetOp1)) mktNoTeams = mktNoTeams.Replace(ctx.TargetOp1.Replace(" ", ""), "");
                    if (!string.IsNullOrEmpty(ctx.TargetOp2)) mktNoTeams = mktNoTeams.Replace(ctx.TargetOp2.Replace(" ", ""), "");

                    if (!mktNoTeams.Contains(lastName) && !mktNoTeams.Contains(firstName))
                    {
                        return false;
                    }
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
                (TeamAliasMappingService.IsTeamMatch(normOName, ctx.NormSel) ||
                 TeamAliasMappingService.IsTeamMatch(displayONameSafe, ctx.RawSel)))
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
