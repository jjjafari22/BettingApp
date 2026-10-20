using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BettingApp.Services
{
    // Core compiled regexes, OddsPapi handicap parsing, outcome translation, and UI table sorting.
    public static partial class OddsMatchingLogic
    {
        // =====================================================================
        // Compiled Regular Expressions
        // =====================================================================
        private static readonly Regex _hcOutcomeRegex = new(@"^(1|X|x|2)\s*\(([^)]+)\)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex _basicHcMatchRegex = new(@"^(1|X|x|2|1X|X1|X2|2X|12)(?:\s*\(([^)]+)\))?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex _hcSignRegex = new(@"(?:^|\s|\()([+-]\d+(?:\.\d+)?)\)?", RegexOptions.Compiled);
        private static readonly Regex _hcSuffixRegex = new(@"([+-]\d+(?:\.\d+)?)$", RegexOptions.Compiled);
        private static readonly Regex _numSuffixRegex = new(@"(\d+(?:\.\d+)?)$", RegexOptions.Compiled);
        private static readonly Regex _parenMatchRegex = new(@"\(([^,()]+),\s*([^()]+)\)", RegexOptions.Compiled);
        private static readonly Regex _simpleParenMatchRegex = new(@"\(([^()]+)\)$", RegexOptions.Compiled);
        private static readonly Regex _htftRegex = new(@"^(1|X|2)/(1|X|2)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex _powerSubRegex = new(@"\b(power sub|super sub)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex _dotZeroRegex = new(@"\.0+(?!\d)", RegexOptions.Compiled);
        private static readonly Regex _drawTieRegex = new(@"\b(?:draw|tie|ties|oavgjort|uavgjort|kryss|likt|x)\b|^x$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex _overUnderLineRegex = new(@"\b(over|under)\s*(\d+(?:\.\d+)?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // =====================================================================
        // 1. Handicap Parsing, Outcome Translation & Sorting
        // =====================================================================

        /// <summary>
        /// Parses an OddsPapi handicap string (e.g. "-0.5", "0.5", "+1.5", "0-2.0", "0:1")
        /// into the effective handicap from the perspective of the specified team ("1", "2", or "X").
        /// In OddsPapi raw data, unsigned/negative numbers in "2 (-0.5)" represent the Team 1 baseline (-0.5),
        /// meaning Team 2's effective handicap is inverted (+0.5).
        /// </summary>
        public static bool TryParseOddsPapiHandicap(string teamCode, string hcStr, out double teamHandicap)
        {
            teamHandicap = 0;
            if (string.IsNullOrWhiteSpace(hcStr)) return false;

            hcStr = hcStr.Trim();
            string upperTeam = (teamCode ?? "").Trim().ToUpperInvariant();

            // 1. Score-style handicap: "0-2", "0-2.0", "1-0" (hyphen not at index 0)
            if (hcStr.Contains('-') && hcStr.Length > 2 && !hcStr.StartsWith("-"))
            {
                var parts = hcStr.Split('-');
                if (parts.Length == 2 &&
                    double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double p1) &&
                    double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double p2))
                {
                    teamHandicap = upperTeam == "2" ? (p2 - p1) : (p1 - p2);
                    return true;
                }
            }

            // 2. Colon-style handicap: "0:1", "2:0"
            if (hcStr.Contains(':'))
            {
                var parts = hcStr.Split(':');
                if (parts.Length == 2 &&
                    double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double p1) &&
                    double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double p2))
                {
                    teamHandicap = upperTeam == "2" ? (p2 - p1) : (p1 - p2);
                    return true;
                }
            }

            // 3. Single numeric handicap: "-0.5", "0.5", "+1.5"
            if (double.TryParse(hcStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
            {
                if (hcStr.StartsWith("+"))
                {
                    teamHandicap = val;
                }
                else
                {
                    teamHandicap = upperTeam == "2" ? -val : val;
                }
                return true;
            }

            return false;
        }

        public static string GetTranslatedOutcomeName(string oName, string matchName)
        {
            string displayOName = oName;
            if (string.IsNullOrEmpty(matchName)) return displayOName;

            var matchSplit = matchName.Split(new[] { " vs ", " v ", " - " }, StringSplitOptions.None);
            if (matchSplit.Length < 2) return displayOName;

            string t1 = matchSplit[0].Trim();
            string t2 = matchSplit[1].Trim();

            var hcMatch = _hcOutcomeRegex.Match(displayOName);
            if (hcMatch.Success)
            {
                string teamSel = hcMatch.Groups[1].Value.ToUpperInvariant();
                string hcStr = hcMatch.Groups[2].Value.Trim();

                if (TryParseOddsPapiHandicap(teamSel, hcStr, out double effectiveHc))
                {
                    string prefix = effectiveHc > 0 ? "+" : "";
                    if (teamSel == "1") displayOName = $"{t1} {prefix}{effectiveHc.ToString(CultureInfo.InvariantCulture)}";
                    else if (teamSel == "2") displayOName = $"{t2} {prefix}{effectiveHc.ToString(CultureInfo.InvariantCulture)}";
                    else if (teamSel == "X") displayOName = $"Draw ({prefix}{effectiveHc.ToString(CultureInfo.InvariantCulture)})";
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
                else
                {
                    if (displayOName.StartsWith("1 ")) displayOName = t1 + displayOName.Substring(1);
                    else if (displayOName.StartsWith("2 ")) displayOName = t2 + displayOName.Substring(1);

                    displayOName = displayOName.Replace("1/", $"{t1}/").Replace("/1", $"/{t1}");
                    displayOName = displayOName.Replace("2/", $"{t2}/").Replace("/2", $"/{t2}");
                    displayOName = displayOName.Replace("X/", "Draw/").Replace("x/", "Draw/");
                    displayOName = displayOName.Replace("/X", "/Draw").Replace("/x", "/Draw");
                }
            }

            return displayOName;
        }

        public static string GetSortKey(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            // 1. Handicap & 1X2 pattern: "1", "2", "X", "1 (-1.5)", "2 (-0.5)", "X (0:1)"
            var hcMatch = Regex.Match(input.Trim(), @"^(1|X|x|2)(?:\s*\(([^)]+)\))?$", RegexOptions.IgnoreCase);
            if (hcMatch.Success)
            {
                string team = hcMatch.Groups[1].Value.ToUpperInvariant();
                int teamOrder = team == "1" ? 1 : (team == "X" ? 2 : 3);
                string hcStr = hcMatch.Groups[2].Success ? hcMatch.Groups[2].Value.Trim() : "";

                double actualHc = 0.0;
                if (!string.IsNullOrEmpty(hcStr))
                {
                    TryParseOddsPapiHandicap(team, hcStr, out actualHc);
                }

                return $"{teamOrder}_{(actualHc + 1000).ToString("0000.0000", CultureInfo.InvariantCulture)}";
            }

            // 2. Double Chance
            string upper = input.Trim().ToUpperInvariant();
            if (upper == "1X" || upper == "X1") return "dc_1";
            if (upper == "12") return "dc_2";
            if (upper == "X2" || upper == "2X") return "dc_3";

            // 3. BTTS / Yes/No
            if (upper == "YES" || upper == "JA") return "btts_1";
            if (upper == "NO" || upper == "NEI") return "btts_2";

            // 4. Default natural sort with space-delimited normalization so "1 (0.5)" doesn't merge digits into "10.5"
            string normalized = Regex.Replace(input, @"[()]", " ").Trim();

            return Regex.Replace(normalized, @"([-]?\d+(?:\.\d+)?)", m =>
            {
                if (double.TryParse(m.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
                {
                    return (val + 100000).ToString("000000.0000", CultureInfo.InvariantCulture);
                }
                return m.Value;
            });
        }
    }
}
