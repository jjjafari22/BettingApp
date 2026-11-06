using System;
using System.Collections.Generic;

namespace BettingApp.Services
{
    public class MarketMappingService
    {
        // This dictionary maps how different bookmakers (or the AI) write a market
        // into Kambi's exact expected market string. 
        // We can keep adding edge cases here instead of cluttering the UI code.
        private readonly Dictionary<string, string> _marketAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            // Example mappings (AI Output -> Kambi Category)
            { "Moneyline (incl. overtime and penalties)", "Winner (incl. overtime and penalties)|Moneyline" },
            { "Moneyline (incl. overtime and shootout)", "Winner (incl. overtime and penalties)|Moneyline" },
            { "Moneyline (including overtime and shootout)", "Winner (incl. overtime and penalties)|Moneyline" },
            { "Moneyline (incl. overtime & shootout)", "Winner (incl. overtime and penalties)|Moneyline" },
            { "Moneyline (including overtime & shootout)", "Winner (incl. overtime and penalties)|Moneyline" },
            { "Moneyline (incl. OT and Shootout)", "Winner (incl. overtime and penalties)|Moneyline" },
            { "Moneyline (incl. OT & Shootout)", "Winner (incl. overtime and penalties)|Moneyline" },
            { "Moneyline (incl. overtime)", "Winner (incl. overtime and penalties)|Winner (incl. overtime)|Moneyline" },
            { "Moneyline (including overtime)", "Winner (incl. overtime and penalties)|Winner (incl. overtime)|Moneyline" },
            { "Moneyline", "Winner (incl. overtime and penalties)|Winner (incl. overtime)|Full Time Result|Winner" },
            { "Money Line", "Winner (incl. overtime and penalties)|Winner (incl. overtime)|Full Time Result|Winner" },
            { "Money Line (Incl. Overtime and Shootout)", "Winner (incl. overtime and penalties)|Moneyline" },
            { "Money Line (incl. overtime and penalties)", "Winner (incl. overtime and penalties)|Moneyline" },
            { "Money Line (incl. overtime)", "Winner (incl. overtime and penalties)|Winner (incl. overtime)|Moneyline" },
            { "Money Line (including overtime)", "Winner (incl. overtime and penalties)|Winner (incl. overtime)|Moneyline" },
            { "Match Winner (incl. overtime and penalties)", "Winner (incl. overtime and penalties)|Moneyline|To Qualify" },
            { "Match Winner (incl. overtime and shootout)", "Winner (incl. overtime and penalties)|Moneyline|To Qualify" },
            { "Match Winner (including overtime and shootout)", "Winner (incl. overtime and penalties)|Moneyline|To Qualify" },
            { "Match Winner (incl. overtime)", "Winner (incl. overtime and penalties)|Winner (incl. overtime)|Moneyline|To Qualify" },
            { "Winner (incl. overtime and penalties)", "Winner (incl. overtime and penalties)|To Qualify" },
            { "Winner (incl. overtime and shootout)", "Winner (incl. overtime and penalties)|To Qualify" },
            { "Winner (including overtime and shootout)", "Winner (incl. overtime and penalties)|To Qualify" },
            { "Winner (incl. overtime)", "Winner (incl. overtime)|Winner (incl. overtime and penalties)|To Qualify" },
            { "To Win (incl. overtime and shootout)", "Winner (incl. overtime and penalties)|To Qualify" },
            { "To Win (including overtime and shootout)", "Winner (incl. overtime and penalties)|To Qualify" },
            { "To Win (incl. overtime and penalties)", "Winner (incl. overtime and penalties)|To Qualify" },
            { "To Win (including overtime and penalties)", "Winner (incl. overtime and penalties)|To Qualify" },
            { "To Win (incl. overtime)", "Winner (incl. overtime and penalties)|Winner (incl. overtime)|To Qualify" },
            { "To Win (including overtime)", "Winner (incl. overtime and penalties)|Winner (incl. overtime)|To Qualify" },
            { "Total Goals - Regular Time", "Total|Total Goals|Over Under Full Time" },
            { "Total Goals (incl. overtime and penalties)", "Total (incl. overtime and penalties)|Total|Total Goals|Over Under Full Time" },
            { "Anytime Goalscorer - Including Overtime", "Over Under Player Goals (incl. overtime)|Anytime Goal Scorer|Player Goals (incl. overtime)|Player Goals" },
            { "To Score 2 or More Goals - Including Overtime", "Over Under Player Goals (incl. overtime)|Player Goals (incl. overtime)|Player Goals" },
            { "To Score 3 or More Goals - Including Overtime", "Over Under Player Goals (incl. overtime)|Player Goals (incl. overtime)|Player Goals" },
            { "To Score 2 or More Goals", "Over Under Player Goals (incl. overtime)|Player Goals (incl. overtime)|Player Goals" },
            { "To Score 3 or More Goals", "Over Under Player Goals (incl. overtime)|Player Goals (incl. overtime)|Player Goals" },
            { "Player Points", "Over Under Player Points (incl. overtime)|Player Points (incl. overtime)" },
            { "Player Points (incl. overtime)", "Over Under Player Points (incl. overtime)|Player Points (incl. overtime)" },
            { "Player Points - Including Overtime", "Over Under Player Points (incl. overtime)|Player Points (incl. overtime)" },
            { "Player Points (Including Overtime)", "Over Under Player Points (incl. overtime)|Player Points (incl. overtime)" },
            { "Player Total Points", "Over Under Player Points (incl. overtime)|Player Points (incl. overtime)" },
            { "Player Total Points Over/Under", "Over Under Player Points (incl. overtime)|Player Points (incl. overtime)" },
            { "Player Points Over/Under", "Over Under Player Points (incl. overtime)|Player Points (incl. overtime)" },
            { "Player Total Points (incl. overtime)", "Over Under Player Points (incl. overtime)|Player Points (incl. overtime)" },
            { "Player Total Points - Including Overtime", "Over Under Player Points (incl. overtime)|Player Points (incl. overtime)" },
            { "Player Total Assists", "Over Under Player Assists (incl. overtime)|Player Assists (incl. overtime)" },
            { "Player Total Assists Over/Under", "Over Under Player Assists (incl. overtime)|Player Assists (incl. overtime)" },
            { "Player Assists Over/Under", "Over Under Player Assists (incl. overtime)|Player Assists (incl. overtime)" },
            { "Player to Record at Least 1 Point", "Over Under Player Points (incl. overtime)|Player Points (incl. overtime)" },
            { "Player to Record at Least 1 Point (Including Overtime)", "Over Under Player Points (incl. overtime)|Player Points (incl. overtime)" },
            { "Player to Record at Least 1 Point - Including Overtime", "Over Under Player Points (incl. overtime)|Player Points (incl. overtime)" },
            { "Player to Record at Least 2 Points", "Over Under Player Points (incl. overtime)|Player Points (incl. overtime)" },
            { "Player to Record at Least 2 Points (Including Overtime)", "Over Under Player Points (incl. overtime)|Player Points (incl. overtime)" },
            { "Player to Record at Least 2 Points - Including Overtime", "Over Under Player Points (incl. overtime)|Player Points (incl. overtime)" },
            { "Player to Score at Least 1 Point", "Over Under Player Points (incl. overtime)|Player Points (incl. overtime)" },
            { "Player to Score at Least 1 Point (Including Overtime)", "Over Under Player Points (incl. overtime)|Player Points (incl. overtime)" },
            { "Player to Score at Least 1 Point - Including Overtime", "Over Under Player Points (incl. overtime)|Player Points (incl. overtime)" },
            { "Player to Score at Least 2 Points", "Over Under Player Points (incl. overtime)|Player Points (incl. overtime)" },
            { "Player to Score at Least 2 Points (Including Overtime)", "Over Under Player Points (incl. overtime)|Player Points (incl. overtime)" },
            { "Player to Score at Least 2 Points - Including Overtime", "Over Under Player Points (incl. overtime)|Player Points (incl. overtime)" },
            { "Player Shots on Goal", "Over Under Player Shots On Goal (incl. overtime)|Player Shots On Goal (incl. overtime)|Player's shot on target" },
            { "Player Shots On Goal (incl. overtime)", "Over Under Player Shots On Goal (incl. overtime)|Player Shots On Goal (incl. overtime)" },
            { "Asian Over/Under", "Over Under Full Time" },
            { "1. Half: Goals Handicap", "Asian Handicap First Half|3-Way Handicap First Half|Half Time Handicap" },
            { "Yellow Cards: Total", "Total Cards" },
            { "Total Goals", "Over Under Full Time" },
            { "Total Goals 2", "Over Under Full Time" },
            { "Total Goals 1", "Over Under Full Time" },
            { "Total Goals 3", "Over Under Full Time" },
            { "Total Goals 4", "Over Under Full Time" },
            { "1st Half Goals 1", "Over Under First Half" },
            { "1st Half Goals 2", "Over Under First Half" },
            { "1st Half Goals", "Over Under First Half" },
            { "First Half Goals", "Over Under First Half" },
            { "Player Goalkeeper Saves", "Over Under Player Goalkeeper Saves (incl. overtime)|Player Goalkeeper Saves (incl. overtime)|Player Goalkeeper Saves" },
            { "Player Saves", "Over Under Player Goalkeeper Saves (incl. overtime)|Player Goalkeeper Saves (incl. overtime)|Player Goalkeeper Saves" },
            { "Player Shots on Target", "Over Under Player Shots On Goal (incl. overtime)|Player Shots On Goal (incl. overtime)|Player's shot on target|Player Shots on Target|Player Shots on Target (incl. overtime)" },
            { "Player Shots", "Over Under Player Shots (incl. overtime)|Player Shots (incl. overtime)|Player Shots" },
            { "Player Fouls Committed", "Over Under Player Fouls Committed (incl. overtime)|Player Fouls Committed (incl. overtime)|Player Fouls Committed" },
            { "Player Fouls Won", "Over Under Player Fouls Won (incl. overtime)|Player Fouls Won (incl. overtime)|Player Fouls Won|Over Under Player Fouls Drawn (incl. overtime)|Player Fouls Drawn (incl. overtime)|Player Fouls Drawn|Over Under Player Fouls Suffered (incl. overtime)|Player Fouls Suffered (incl. overtime)|Player Fouls Suffered|Over Under Player To Be Fouled (incl. overtime)|Player To Be Fouled (incl. overtime)|Player To Be Fouled|Fouls Won|Fouls Drawn|Fouls Suffered" },
            { "Player Fouls Drawn", "Over Under Player Fouls Won (incl. overtime)|Player Fouls Won (incl. overtime)|Player Fouls Won|Over Under Player Fouls Drawn (incl. overtime)|Player Fouls Drawn (incl. overtime)|Player Fouls Drawn|Over Under Player Fouls Suffered (incl. overtime)|Player Fouls Suffered (incl. overtime)|Player Fouls Suffered|Over Under Player To Be Fouled (incl. overtime)|Player To Be Fouled (incl. overtime)|Player To Be Fouled|Fouls Won|Fouls Drawn|Fouls Suffered" },
            { "Player Fouls Suffered", "Over Under Player Fouls Won (incl. overtime)|Player Fouls Won (incl. overtime)|Player Fouls Won|Over Under Player Fouls Drawn (incl. overtime)|Player Fouls Drawn (incl. overtime)|Player Fouls Drawn|Over Under Player Fouls Suffered (incl. overtime)|Player Fouls Suffered (incl. overtime)|Player Fouls Suffered|Over Under Player To Be Fouled (incl. overtime)|Player To Be Fouled (incl. overtime)|Player To Be Fouled|Fouls Won|Fouls Drawn|Fouls Suffered" },
            { "Player To Be Fouled", "Over Under Player Fouls Won (incl. overtime)|Player Fouls Won (incl. overtime)|Player Fouls Won|Over Under Player Fouls Drawn (incl. overtime)|Player Fouls Drawn (incl. overtime)|Player Fouls Drawn|Over Under Player Fouls Suffered (incl. overtime)|Player Fouls Suffered (incl. overtime)|Player Fouls Suffered|Over Under Player To Be Fouled (incl. overtime)|Player To Be Fouled (incl. overtime)|Player To Be Fouled|Fouls Won|Fouls Drawn|Fouls Suffered" },
            { "Fouls Won", "Over Under Player Fouls Won (incl. overtime)|Player Fouls Won (incl. overtime)|Player Fouls Won|Over Under Player Fouls Drawn (incl. overtime)|Player Fouls Drawn (incl. overtime)|Player Fouls Drawn|Over Under Player Fouls Suffered (incl. overtime)|Player Fouls Suffered (incl. overtime)|Player Fouls Suffered|Over Under Player To Be Fouled (incl. overtime)|Player To Be Fouled (incl. overtime)|Player To Be Fouled|Fouls Won|Fouls Drawn|Fouls Suffered" },
            { "Fouls Drawn", "Over Under Player Fouls Won (incl. overtime)|Player Fouls Won (incl. overtime)|Player Fouls Won|Over Under Player Fouls Drawn (incl. overtime)|Player Fouls Drawn (incl. overtime)|Player Fouls Drawn|Over Under Player Fouls Suffered (incl. overtime)|Player Fouls Suffered (incl. overtime)|Player Fouls Suffered|Over Under Player To Be Fouled (incl. overtime)|Player To Be Fouled (incl. overtime)|Player To Be Fouled|Fouls Won|Fouls Drawn|Fouls Suffered" },
            { "Fouls Suffered", "Over Under Player Fouls Won (incl. overtime)|Player Fouls Won (incl. overtime)|Player Fouls Won|Over Under Player Fouls Drawn (incl. overtime)|Player Fouls Drawn (incl. overtime)|Player Fouls Drawn|Over Under Player Fouls Suffered (incl. overtime)|Player Fouls Suffered (incl. overtime)|Player Fouls Suffered|Over Under Player To Be Fouled (incl. overtime)|Player To Be Fouled (incl. overtime)|Player To Be Fouled|Fouls Won|Fouls Drawn|Fouls Suffered" },
            { "Full Time", "Full Time Result|Regular Time Result|Winner" },
            { "Match Odds", "Regular Time Result|Full Time Result|Winner (incl. overtime)|Winner" },
            { "Match Winner", "Winner (incl. overtime)|Regular Time Result|Winner (incl. overtime and penalties)|Full Time Result|Winner" },
            { "Winner", "Winner|Winner (incl. overtime)|Winner (incl. overtime and penalties)|Full Time Result|Regular Time Result" },
            { "1x2", "Regular Time Result|Full Time Result" },
            { "Match Result", "Regular Time Result|Full Time Result|Winner (incl. overtime)|Winner" },
            { "To Win", "Winner (incl. overtime and penalties)|Winner (incl. overtime)|Regular Time Result|Full Time Result|Winner" },
            { "To Win Match", "Winner|Winner (incl. overtime)|Winner (incl. overtime and penalties)|Regular Time Result|Full Time Result" },
            { "Head to Head", "Winner|Winner (incl. overtime)|Winner (incl. overtime and penalties)|Regular Time Result|Full Time Result" },
            { "Head-to-Head", "Winner|Winner (incl. overtime)|Winner (incl. overtime and penalties)|Regular Time Result|Full Time Result" },
            { "Match Result (1X2)", "Regular Time Result|Full Time Result" },
            { "Match Odds - Regular Time", "Regular Time Result|Full Time Result" },
            { "Match Odds (Regular Time)", "Regular Time Result|Full Time Result" },
            { "Match Result - Regular Time", "Regular Time Result|Full Time Result" },
            { "Match Result (Regular Time)", "Regular Time Result|Full Time Result" },
            { "1X2 - Regular Time", "Regular Time Result|Full Time Result" },
            { "1X2 (Regular Time)", "Regular Time Result|Full Time Result" },
            { "3-Way - Regular Time", "Regular Time Result|Full Time Result" },
            { "3-Way (Regular Time)", "Regular Time Result|Full Time Result" },
            { "3-Way Result", "Regular Time Result|Full Time Result" },
            { "3 Way Result", "Regular Time Result|Full Time Result" },
            { "Regular Time Result", "Regular Time Result" },
            { "Regular Time 1X2", "Regular Time Result" },
            { "Double Chance", "Double Chance Full Time|Double Chance" },
            { "Double Chance (1X2)", "Double Chance Full Time|Double Chance" },
            { "Double Chance - Regular Time", "Double Chance|Double Chance Full Time" },
            { "Draw No Bet", "Draw No Bet|Draw No Bet (incl. overtime and penalties)" },
            { "Draw No Bet - Regular Time", "Draw No Bet" },
            { "1st Half Result", "First Half Result" },
            { "1. Half Result", "First Half Result" },
            { "Half Time Result", "First Half Result" },
            { "Half Time", "First Half Result" },
            { "First Half", "First Half Result" },
            { "1st Half 1x2", "First Half Result" },
            { "2nd Half Result", "Second Half Result" },
            { "2. Half Result", "Second Half Result" },
            { "2nd Half 1x2", "Second Half Result" },
            { "Correct Score", "Correct Score Full Time|Correct Score" },
            { "Set Betting", "Correct Score" },
            { "Correct Set Score", "Correct Score" },
            { "Set Score", "Correct Score" },
            { "Early Win", "2Up - Full Time Result" },
            { "Early Win (Anytime 2 Goal Lead)", "2Up - Full Time Result" },
            { "Early Payout", "2Up - Full Time Result" },
            { "Full Time - 2UP", "2Up - Full Time Result" },
            { "2UP", "2Up - Full Time Result" },
            { "Anytime Goalscorer", "Over Under Player Goals (incl. overtime)|Anytime Goal Scorer|Player Goals (incl. overtime)|Player Goals" },
            { "To Be Booked", "Player To Be Carded (incl. overtime)|Player To Be Carded|Player Cards" },
            { "Player to be Booked", "Player To Be Carded (incl. overtime)|Player To Be Carded|Player Cards" },
            { "Player To Receive A Card", "Player To Be Carded (incl. overtime)|Player To Be Carded|Player Cards" },
            { "Player Cards", "Player To Be Carded (incl. overtime)|Player To Be Carded|Player Cards" },
            { "Will/Will not get Booked", "Player To Be Carded (incl. overtime)|Player To Be Carded|Player Cards" },
            { "Player Assists", "Over Under Player Assists (incl. overtime)|Player Assists (incl. overtime)|Player Assists" },
            { "Player Tackles", "Over Under Player Tackles (incl. overtime)|Player Tackles (incl. overtime)|Player Tackles" },
            { "To Score or Assist", "Over Under Player Goals or Assists (incl. overtime)|Player Goal or Assist|To Score or Assist|Goal or Assist" },
            { "Score or Assist", "Over Under Player Goals or Assists (incl. overtime)|Player Goal or Assist|To Score or Assist|Goal or Assist" },
            { "Goal or Assist", "Over Under Player Goals or Assists (incl. overtime)|Player Goal or Assist|To Score or Assist|Goal or Assist" },
            { "To Score or Provide an Assist", "Over Under Player Goals or Assists (incl. overtime)|Player Goal or Assist|To Score or Assist|Goal or Assist" },
            { "Score or Provide an Assist", "Over Under Player Goals or Assists (incl. overtime)|Player Goal or Assist|To Score or Assist|Goal or Assist" },
            { "Goal or Provide an Assist", "Over Under Player Goals or Assists (incl. overtime)|Player Goal or Assist|To Score or Assist|Goal or Assist" },
            { "Anytime Goal or Assist", "Over Under Player Goals or Assists (incl. overtime)|Player Goal or Assist|To Score or Assist|Goal or Assist" },
            { "Player Goal or Assist", "Over Under Player Goals or Assists (incl. overtime)|Player Goal or Assist|To Score or Assist|Goal or Assist" },
            { "Player to Score or Assist", "Over Under Player Goals or Assists (incl. overtime)|Player Goal or Assist|To Score or Assist|Goal or Assist" },
            { "To Score / Assist", "Over Under Player Goals or Assists (incl. overtime)|Player Goal or Assist|To Score or Assist|Goal or Assist" },
            { "Goal / Assist", "Over Under Player Goals or Assists (incl. overtime)|Player Goal or Assist|To Score or Assist|Goal or Assist" },
            { "Score / Assist", "Over Under Player Goals or Assists (incl. overtime)|Player Goal or Assist|To Score or Assist|Goal or Assist" },
            { "Goal/Assist", "Over Under Player Goals or Assists (incl. overtime)|Player Goal or Assist|To Score or Assist|Goal or Assist" },
            { "Score/Assist", "Over Under Player Goals or Assists (incl. overtime)|Player Goal or Assist|To Score or Assist|Goal or Assist" },
            { "Goals or Assists", "Over Under Player Goals or Assists (incl. overtime)|Player Goal or Assist|To Score or Assist|Goal or Assist" },
            { "Goals and Assists", "Over Under Player Goals or Assists (incl. overtime)|Player Goal or Assist|To Score or Assist|Goal or Assist" },
            { "Goal and Assist", "Over Under Player Goals or Assists (incl. overtime)|Player Goal or Assist|To Score or Assist|Goal or Assist" },
            { "Score and Assist", "Over Under Player Goals or Assists (incl. overtime)|Player Goal or Assist|To Score or Assist|Goal or Assist" },
            { "To Score and Assist", "Over Under Player Goals or Assists (incl. overtime)|Player Goal or Assist|To Score or Assist|Goal or Assist" },
            { "Player Rebounds", "Over Under Player Rebounds (incl. overtime)|Player Rebounds (incl. overtime)" },
            { "Player Total Rebounds", "Over Under Player Rebounds (incl. overtime)|Player Rebounds (incl. overtime)" },
            { "Player Rebounds Over/Under", "Over Under Player Rebounds (incl. overtime)|Player Rebounds (incl. overtime)" },
            { "Player 3 Point FG", "Over Under Player 3 Point FG (incl. overtime)|Player 3 Point FG (incl. overtime)" },
            { "Player 3-Point FG", "Over Under Player 3 Point FG (incl. overtime)|Player 3 Point FG (incl. overtime)" },
            { "Player 3-Pointers", "Over Under Player 3 Point FG (incl. overtime)|Player 3 Point FG (incl. overtime)" },
            { "Player 3 Pointers", "Over Under Player 3 Point FG (incl. overtime)|Player 3 Point FG (incl. overtime)" },
            { "Player Three Pointers", "Over Under Player 3 Point FG (incl. overtime)|Player 3 Point FG (incl. overtime)" },
            { "Player Threes", "Over Under Player 3 Point FG (incl. overtime)|Player 3 Point FG (incl. overtime)" },
            { "Player Threes Made", "Over Under Player 3 Point FG (incl. overtime)|Player 3 Point FG (incl. overtime)" },
            { "Player 3-Pointers Made", "Over Under Player 3 Point FG (incl. overtime)|Player 3 Point FG (incl. overtime)" },
            { "Player Points + Rebounds", "Over Under Player Points + Rebounds (incl. overtime)|Player Points + Rebounds (incl. overtime)" },
            { "Player Points + Assists", "Over Under Player Points + Assists (incl. overtime)|Player Points + Assists (incl. overtime)" },
            { "Player Assists + Rebounds", "Over Under Player Assists + Rebounds (incl. overtime)|Player Assists + Rebounds (incl. overtime)" },
            { "Player Rebounds + Assists", "Over Under Player Assists + Rebounds (incl. overtime)|Player Assists + Rebounds (incl. overtime)" },
            { "Player Points + Assists + Rebounds", "Over Under Player Points + Assists + Rebounds (incl. overtime)|Player Points + Assists + Rebounds (incl. overtime)" },
            { "Player Points + Rebounds + Assists", "Over Under Player Points + Assists + Rebounds (incl. overtime)|Player Points + Assists + Rebounds (incl. overtime)" },
            { "Player Steals", "Over Under Player Steals (incl. overtime)|Player Steals (incl. overtime)" },
            { "Player Total Steals", "Over Under Player Steals (incl. overtime)|Player Steals (incl. overtime)" },
            { "Player Blocks", "Over Under Player Blocks (incl. overtime)|Player Blocks (incl. overtime)" },
            { "Player Total Blocks", "Over Under Player Blocks (incl. overtime)|Player Blocks (incl. overtime)" },
            { "Player Blocked Shots", "Over Under Player Blocks (incl. overtime)|Player Blocks (incl. overtime)" },
            { "Player Steals + Blocks", "Over Under Player Steals + Blocks (incl. overtime)|Player Steals + Blocks (incl. overtime)" },
            { "Player Blocks + Steals", "Over Under Player Steals + Blocks (incl. overtime)|Player Steals + Blocks (incl. overtime)" },
            { "Player Turnovers", "Over Under Player Turnovers (incl. overtime)|Player Turnovers (incl. overtime)" },
            { "Player Total Turnovers", "Over Under Player Turnovers (incl. overtime)|Player Turnovers (incl. overtime)" },
            { "Player Double - Double", "Player Double - Double" },
            { "Player Double-Double", "Player Double - Double" },
            { "Player Double Double", "Player Double - Double" },
            { "Double-Double", "Player Double - Double" },
            { "Double Double", "Player Double - Double" },
            { "Double - Double", "Player Double - Double" },
            { "To Record a Double-Double", "Player Double - Double" },
            { "To Record a Double Double", "Player Double - Double" },
            { "Player Triple - Double", "Player Triple - Double" },
            { "Player Triple-Double", "Player Triple - Double" },
            { "Player Triple Double", "Player Triple - Double" },
            { "Triple-Double", "Player Triple - Double" },
            { "Triple Double", "Player Triple - Double" },
            { "Triple - Double", "Player Triple - Double" },
            { "To Record a Triple-Double", "Player Triple - Double" },
            { "To Record a Triple Double", "Player Triple - Double" },
            { "Player First Point", "Player First Point" },
            { "First Basket", "Player First Point" },
            { "First Point Scorer", "Player First Point" },
            { "Player First 3 Point FG", "Player First 3 Point FG" },
            { "First 3 Point FG", "Player First 3 Point FG" },
            { "First 3-Point FG", "Player First 3 Point FG" }
        };

        public List<string> NormalizeMarketName(string rawMarketName, string? matchName = null, string? selectionName = null, string? opMatchName = null)
        {
            if (string.IsNullOrWhiteSpace(rawMarketName)) return new List<string>();

            var clean = rawMarketName.Trim();

            // Bet Builder / Combination markets (e.g. "Both Teams To Score & Over 2.5 Goals", "Match Result and Both Teams to Score")
            // OddsPapi does not provide combined odds for Bet Builders or multi-market combinations.
            // Do NOT normalize or reduce combination markets to a single sub-market (like pure BTTS or pure Total Goals).
            if (IsCombinationMarket(clean, matchName) || (!string.IsNullOrEmpty(selectionName) && IsCombinationMarket(selectionName, matchName)))
            {
                return new List<string> { clean };
            }
            
            if (clean.StartsWith("Alternative ", StringComparison.OrdinalIgnoreCase))
            {
                clean = clean.Substring(12).Trim();
            }

            if (clean.Contains("Early Win", StringComparison.OrdinalIgnoreCase) ||
                clean.Contains("Early Payout", StringComparison.OrdinalIgnoreCase) ||
                System.Text.RegularExpressions.Regex.IsMatch(clean, @"\b2\s*-?\s*up\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                return new List<string> { "2Up - Full Time Result" };
            }
            
            // Detect if the market explicitly specifies a half, period, or quarter
            string halfSuffix = "";
            if (clean.Contains("1st Half", StringComparison.OrdinalIgnoreCase) || clean.Contains("First Half", StringComparison.OrdinalIgnoreCase) || clean.Contains("1. Half", StringComparison.OrdinalIgnoreCase) || System.Text.RegularExpressions.Regex.IsMatch(clean, @"\b1h\b|\bhalf\s*1\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                halfSuffix = " First Half";
            else if (clean.Contains("2nd Half", StringComparison.OrdinalIgnoreCase) || clean.Contains("Second Half", StringComparison.OrdinalIgnoreCase) || clean.Contains("2. Half", StringComparison.OrdinalIgnoreCase) || System.Text.RegularExpressions.Regex.IsMatch(clean, @"\b2h\b|\bhalf\s*2\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                halfSuffix = " Second Half";

            string periodPrefix = "";
            if (System.Text.RegularExpressions.Regex.IsMatch(clean, @"\b(?:1st|first|1\.)\s+period\b|\bperiod\s*1\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                periodPrefix = "First Period ";
            else if (System.Text.RegularExpressions.Regex.IsMatch(clean, @"\b(?:2nd|second|2\.)\s+period\b|\bperiod\s*2\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                periodPrefix = "Second Period ";
            else if (System.Text.RegularExpressions.Regex.IsMatch(clean, @"\b(?:3rd|third|3\.)\s+period\b|\bperiod\s*3\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                periodPrefix = "Third Period ";

            string quarterOrdinal = "";
            if (System.Text.RegularExpressions.Regex.IsMatch(clean, @"\b(?:1st|first|1\.)\s*(?:quarter|qtr)\b|\b(?:quarter|qtr)\s*1\b|\bq1\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                quarterOrdinal = "First Quarter";
            else if (System.Text.RegularExpressions.Regex.IsMatch(clean, @"\b(?:2nd|second|2\.)\s*(?:quarter|qtr)\b|\b(?:quarter|qtr)\s*2\b|\bq2\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                quarterOrdinal = "Second Quarter";
            else if (System.Text.RegularExpressions.Regex.IsMatch(clean, @"\b(?:3rd|third|3\.)\s*(?:quarter|qtr)\b|\b(?:quarter|qtr)\s*3\b|\bq3\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                quarterOrdinal = "Third Quarter";
            else if (System.Text.RegularExpressions.Regex.IsMatch(clean, @"\b(?:4th|fourth|4\.)\s*(?:quarter|qtr)\b|\b(?:quarter|qtr)\s*4\b|\bq4\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                quarterOrdinal = "Fourth Quarter";

            bool hasSubPeriodModifier = !string.IsNullOrEmpty(halfSuffix) ||
                                        !string.IsNullOrEmpty(periodPrefix) ||
                                        !string.IsNullOrEmpty(quarterOrdinal) ||
                                        System.Text.RegularExpressions.Regex.IsMatch(clean, @"\b(?:1st|2nd|3rd|4th|first|second|third|fourth|1\.|2\.|3\.|4\.)\s*(?:quarter|qtr|period|inning|half)\b|\b(?:q[1-4]|p[1-3])\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // Detect if the market specifies a Tennis set ordinal (First Set .. Fifth Set)
            string setOrdinal = TryResolveTennisSetOrdinal(clean);
            bool isSpecificSingleGame = System.Text.RegularExpressions.Regex.IsMatch(
                clean,
                @"\b(?:\d+(?:st|nd|rd|th)|first|second|third|fourth|fifth|sixth|seventh|eighth|ninth|tenth|next)\s+game\b|\bgame\s+\d+\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (!string.IsNullOrEmpty(setOrdinal) && !isSpecificSingleGame)
            {
                if (clean.Contains("Tie Break", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("Tie-Break", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("Tiebreak", StringComparison.OrdinalIgnoreCase))
                {
                    return new List<string> { $"Tie Break {setOrdinal}" };
                }

                if (clean.Contains("Correct Score", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("Score", StringComparison.OrdinalIgnoreCase))
                {
                    return new List<string> { $"Correct Score {setOrdinal}" };
                }

                if (clean.Contains("Handicap", StringComparison.OrdinalIgnoreCase))
                {
                    return new List<string> { $"Game Handicap {setOrdinal}" };
                }

                if (clean.Contains("Odd", StringComparison.OrdinalIgnoreCase) &&
                    clean.Contains("Even", StringComparison.OrdinalIgnoreCase))
                {
                    return new List<string> { $"Odd Even Games {setOrdinal}" };
                }

                if (clean.Contains("Total", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("Games", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("Over", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("Under", StringComparison.OrdinalIgnoreCase))
                {
                    return new List<string> { $"Total Games {setOrdinal}" };
                }

                if (clean.Contains("Winner", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("Result", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("To Win", StringComparison.OrdinalIgnoreCase) ||
                    clean.Equals(setOrdinal, StringComparison.OrdinalIgnoreCase) ||
                    System.Text.RegularExpressions.Regex.IsMatch(clean, @"^(?:1st|2nd|3rd|4th|5th|first|second|third|fourth|fifth|1\.|2\.|3\.|4\.|5\.)\s+set$|^set\s*[1-5]$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    return new List<string> { $"{setOrdinal} Winner" };
                }
            }

            // Handle Team Specific Markets dynamically if matchName or opMatchName is provided
            string? effectiveMatchName = !string.IsNullOrWhiteSpace(matchName) ? matchName : opMatchName;
            if (!string.IsNullOrWhiteSpace(effectiveMatchName))
            {
                var split = TeamAliasMappingService.SplitMatchTeams(effectiveMatchName);
                if (split.Length >= 2)
                {
                    string team1 = split[0].Trim();
                    string team2 = split[1].Trim();

                    var dm1 = System.Text.RegularExpressions.Regex.Match(team1, @"\((?:Starts:\s*)?([^)]+)\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (dm1.Success) team1 = team1.Substring(0, dm1.Index).Trim();

                    var dm2 = System.Text.RegularExpressions.Regex.Match(team2, @"\((?:Starts:\s*)?([^)]+)\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (dm2.Success) team2 = team2.Substring(0, dm2.Index).Trim();

                    string? opTeam1 = null;
                    string? opTeam2 = null;
                    if (!string.IsNullOrWhiteSpace(opMatchName))
                    {
                        var opSplit = TeamAliasMappingService.SplitMatchTeams(opMatchName);
                        if (opSplit.Length >= 2)
                        {
                            opTeam1 = opSplit[0].Trim();
                            opTeam2 = opSplit[1].Trim();
                            bool ai1MatchesOp1 = TeamAliasMappingService.IsTeamMatch(team1, opTeam1);
                            bool ai2MatchesOp2 = TeamAliasMappingService.IsTeamMatch(team2, opTeam2);
                            bool ai1MatchesOp2 = TeamAliasMappingService.IsTeamMatch(team1, opTeam2);
                            bool ai2MatchesOp1 = TeamAliasMappingService.IsTeamMatch(team2, opTeam1);
                            if ((ai1MatchesOp2 || ai2MatchesOp1) && !ai1MatchesOp1 && !ai2MatchesOp2)
                            {
                                (team1, team2) = (team2, team1);
                            }
                        }
                    }

                    bool IsTeam1Target() =>
                        MatchesEitherTeam(team1, opTeam1, clean) ||
                        clean.Contains("Home", StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(selectionName) && MatchesEitherTeam(team1, opTeam1, selectionName)) ||
                        TeamAliasMappingService.ResolveTeamSelection(clean, team1, team2, opTeam1, opTeam2) == 1 ||
                        (!string.IsNullOrEmpty(selectionName) && TeamAliasMappingService.ResolveTeamSelection(selectionName, team1, team2, opTeam1, opTeam2) == 1);

                    bool IsTeam2Target() =>
                        MatchesEitherTeam(team2, opTeam2, clean) ||
                        clean.Contains("Away", StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(selectionName) && MatchesEitherTeam(team2, opTeam2, selectionName)) ||
                        TeamAliasMappingService.ResolveTeamSelection(clean, team1, team2, opTeam1, opTeam2) == 2 ||
                        (!string.IsNullOrEmpty(selectionName) && TeamAliasMappingService.ResolveTeamSelection(selectionName, team1, team2, opTeam1, opTeam2) == 2);

                    // Corners Team 1/2
                    if (clean.Contains("Corners", StringComparison.OrdinalIgnoreCase))
                    {
                        if (clean.Contains("Handicap", StringComparison.OrdinalIgnoreCase))
                        {
                            return new List<string> { "Corners - Handicap" + halfSuffix };
                        }

                        if (clean.Contains("Most Corners", StringComparison.OrdinalIgnoreCase) || clean.Contains("Corners 1X2", StringComparison.OrdinalIgnoreCase) || clean.Contains("More Corners", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!string.IsNullOrEmpty(halfSuffix)) return new List<string> { "Corners - 1X2" + halfSuffix };
                            return new List<string> { "Corners - 1X2" };
                        }

                        if (IsTeam1Target())
                            return new List<string> { "Corners - Over Under Team 1" + halfSuffix };
                        if (IsTeam2Target())
                            return new List<string> { "Corners - Over Under Team 2" + halfSuffix };

                        // Otherwise, generic corners (checking for 'Total Corners', 'Corners', or 'Half Corners')
                        if (clean.Contains("Total Corners", StringComparison.OrdinalIgnoreCase) || clean.Contains("Corners", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!string.IsNullOrEmpty(halfSuffix)) return new List<string> { "Corners - Over/Under" + halfSuffix };
                            return new List<string> { "Corners - Over/Under Full Time" };
                        }
                    }

                    // Goals / Points / Team Total Team 1/2
                    bool isOtherTeamStat = clean.Contains("Corner", StringComparison.OrdinalIgnoreCase) ||
                                           clean.Contains("Card", StringComparison.OrdinalIgnoreCase) ||
                                           clean.Contains("Booking", StringComparison.OrdinalIgnoreCase) ||
                                           clean.Contains("Save", StringComparison.OrdinalIgnoreCase) ||
                                           clean.Contains("Foul", StringComparison.OrdinalIgnoreCase) ||
                                           clean.Contains("Shot", StringComparison.OrdinalIgnoreCase) ||
                                           clean.Contains("Tackle", StringComparison.OrdinalIgnoreCase) ||
                                           clean.Contains("Offside", StringComparison.OrdinalIgnoreCase) ||
                                           clean.Contains("Set", StringComparison.OrdinalIgnoreCase) ||
                                           clean.Contains("Game", StringComparison.OrdinalIgnoreCase) ||
                                           clean.Contains("Ace", StringComparison.OrdinalIgnoreCase) ||
                                           clean.Contains("Fault", StringComparison.OrdinalIgnoreCase) ||
                                           clean.Contains("Break", StringComparison.OrdinalIgnoreCase) ||
                                           clean.Contains("Point", StringComparison.OrdinalIgnoreCase) ||
                                           clean.Contains("Rebound", StringComparison.OrdinalIgnoreCase) ||
                                           clean.Contains("Assist", StringComparison.OrdinalIgnoreCase) ||
                                           clean.Contains("Steal", StringComparison.OrdinalIgnoreCase) ||
                                           clean.Contains("Block", StringComparison.OrdinalIgnoreCase) ||
                                           clean.Contains("Turnover", StringComparison.OrdinalIgnoreCase) ||
                                           clean.Contains("Three", StringComparison.OrdinalIgnoreCase) ||
                                           clean.Contains("Shutout", StringComparison.OrdinalIgnoreCase);

                    bool isBasketballTeamOrMatchPointsTotal =
                        !clean.Contains("Player", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("Player's", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("Break Point", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("Power Play", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("Powerplay", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("3 Point", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("3-Point", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("Three Point", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("First Point", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("Rebound", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("Assist", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("Steal", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("Block", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("Turnover", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("Handicap", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("Spread", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("Result", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("Winner", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("Moneyline", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("1X2", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("Exact", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("Odd", StringComparison.OrdinalIgnoreCase) &&
                        ((clean.Contains("Total Points", StringComparison.OrdinalIgnoreCase) ||
                          clean.Contains("Points Over/Under", StringComparison.OrdinalIgnoreCase) ||
                          clean.Contains("Points Over Under", StringComparison.OrdinalIgnoreCase) ||
                          clean.Contains("Over/Under Points", StringComparison.OrdinalIgnoreCase) ||
                          clean.Contains("Over Under Points", StringComparison.OrdinalIgnoreCase) ||
                          clean.Contains("Team Total", StringComparison.OrdinalIgnoreCase) ||
                          ((IsTeam1Target() || IsTeam2Target() || !string.IsNullOrEmpty(quarterOrdinal) || !string.IsNullOrEmpty(halfSuffix)) &&
                           clean.Contains("Points", StringComparison.OrdinalIgnoreCase))) &&
                         (!HasPlayerName(selectionName, effectiveMatchName) || IsTeam1Target() || IsTeam2Target()));

                    if (isBasketballTeamOrMatchPointsTotal)
                    {
                        bool includesOvertime = clean.Contains("incl. overtime", StringComparison.OrdinalIgnoreCase) ||
                                                clean.Contains("including overtime", StringComparison.OrdinalIgnoreCase) ||
                                                clean.Contains("incl. ot", StringComparison.OrdinalIgnoreCase) ||
                                                clean.Contains("including ot", StringComparison.OrdinalIgnoreCase);

                        if (IsTeam1Target())
                        {
                            if (!string.IsNullOrEmpty(quarterOrdinal))
                                return includesOvertime
                                    ? new List<string> { $"Over Under Team 1 {quarterOrdinal} (incl. overtime)", $"Over Under Team 1 {quarterOrdinal}" }
                                    : new List<string> { $"Over Under Team 1 {quarterOrdinal}", $"Over Under Team 1 {quarterOrdinal} (incl. overtime)" };
                            if (!string.IsNullOrEmpty(halfSuffix))
                                return includesOvertime
                                    ? new List<string> { "Over Under Team 1" + halfSuffix + " (incl. overtime)", "Over Under Team 1" + halfSuffix }
                                    : new List<string> { "Over Under Team 1" + halfSuffix, "Over Under Team 1" + halfSuffix + " (incl. overtime)" };
                            if (!string.IsNullOrEmpty(periodPrefix))
                                return new List<string> { $"{periodPrefix}Team 1 Total" };
                            return new List<string> { "Over Under Team 1 (incl. overtime)", "Team 1 Total (incl. overtime and penalties)", "Over Under Team 1", "Team 1 Total" };
                        }
                        if (IsTeam2Target())
                        {
                            if (!string.IsNullOrEmpty(quarterOrdinal))
                                return includesOvertime
                                    ? new List<string> { $"Over Under Team 2 {quarterOrdinal} (incl. overtime)", $"Over Under Team 2 {quarterOrdinal}" }
                                    : new List<string> { $"Over Under Team 2 {quarterOrdinal}", $"Over Under Team 2 {quarterOrdinal} (incl. overtime)" };
                            if (!string.IsNullOrEmpty(halfSuffix))
                                return includesOvertime
                                    ? new List<string> { "Over Under Team 2" + halfSuffix + " (incl. overtime)", "Over Under Team 2" + halfSuffix }
                                    : new List<string> { "Over Under Team 2" + halfSuffix, "Over Under Team 2" + halfSuffix + " (incl. overtime)" };
                            if (!string.IsNullOrEmpty(periodPrefix))
                                return new List<string> { $"{periodPrefix}Team 2 Total" };
                            return new List<string> { "Over Under Team 2 (incl. overtime)", "Team 2 Total (incl. overtime and penalties)", "Over Under Team 2", "Team 2 Total" };
                        }

                        if (!HasPlayerName(clean, effectiveMatchName))
                        {
                            if (!string.IsNullOrEmpty(quarterOrdinal))
                                return includesOvertime
                                    ? new List<string> { $"Over Under {quarterOrdinal} (incl. overtime)", $"Over Under {quarterOrdinal}" }
                                    : new List<string> { $"Over Under {quarterOrdinal}", $"Over Under {quarterOrdinal} (incl. overtime)" };
                            if (!string.IsNullOrEmpty(halfSuffix))
                                return includesOvertime
                                    ? new List<string> { "Over Under" + halfSuffix + " (incl. overtime)", "Over Under" + halfSuffix }
                                    : new List<string> { "Over Under" + halfSuffix, "Over Under" + halfSuffix + " (incl. overtime)" };
                            if (!string.IsNullOrEmpty(periodPrefix))
                                return new List<string> { $"{periodPrefix}Total" };
                            return new List<string> { "Over Under (incl. overtime)", "Total (incl. overtime and penalties)", "Over Under Full Time", "Total", "Total Goals" };
                        }
                    }

                    if ((clean.Contains("Total Goals", StringComparison.OrdinalIgnoreCase) ||
                         clean.Contains("Goals", StringComparison.OrdinalIgnoreCase) ||
                         (!isOtherTeamStat && clean.Contains("Total", StringComparison.OrdinalIgnoreCase) && (IsTeam1Target() || IsTeam2Target()))) &&
                        !clean.Contains("Player", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("Handicap", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("Spread", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("Both", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("Result", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("1X2", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("Exact", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("Odd", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("First Goal", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("Last Goal", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("Next Goal", StringComparison.OrdinalIgnoreCase))
                    {
                        bool includesOvertime = clean.Contains("incl. overtime", StringComparison.OrdinalIgnoreCase) ||
                                                clean.Contains("including overtime", StringComparison.OrdinalIgnoreCase) ||
                                                clean.Contains("incl. ot", StringComparison.OrdinalIgnoreCase) ||
                                                clean.Contains("including ot", StringComparison.OrdinalIgnoreCase) ||
                                                clean.Contains("shootout", StringComparison.OrdinalIgnoreCase) ||
                                                clean.Contains("penalties", StringComparison.OrdinalIgnoreCase);

                        if (IsTeam1Target())
                        {
                            if (!string.IsNullOrEmpty(quarterOrdinal))
                                return includesOvertime
                                    ? new List<string> { $"Over Under Team 1 {quarterOrdinal} (incl. overtime)", $"Over Under Team 1 {quarterOrdinal}" }
                                    : new List<string> { $"Over Under Team 1 {quarterOrdinal}", $"Over Under Team 1 {quarterOrdinal} (incl. overtime)" };
                            if (!string.IsNullOrEmpty(periodPrefix))
                                return new List<string> { $"{periodPrefix}Team 1 Total" };
                            if (!string.IsNullOrEmpty(halfSuffix))
                                return includesOvertime
                                    ? new List<string> { "Over Under Team 1" + halfSuffix + " (incl. overtime)", "Over Under Team 1" + halfSuffix }
                                    : new List<string> { "Over Under Team 1" + halfSuffix, "Over Under Team 1" + halfSuffix + " (incl. overtime)" };
                            if (includesOvertime)
                                return new List<string> { "Team 1 Total (incl. overtime and penalties)", "Over Under Team 1 (incl. overtime)", "Over Under Team 1", "Team 1 Total" };
                            return new List<string> { "Over Under Team 1", "Over Under Team 1 (incl. overtime)", "Team 1 Total" };
                        }
                        if (IsTeam2Target())
                        {
                            if (!string.IsNullOrEmpty(quarterOrdinal))
                                return includesOvertime
                                    ? new List<string> { $"Over Under Team 2 {quarterOrdinal} (incl. overtime)", $"Over Under Team 2 {quarterOrdinal}" }
                                    : new List<string> { $"Over Under Team 2 {quarterOrdinal}", $"Over Under Team 2 {quarterOrdinal} (incl. overtime)" };
                            if (!string.IsNullOrEmpty(periodPrefix))
                                return new List<string> { $"{periodPrefix}Team 2 Total" };
                            if (!string.IsNullOrEmpty(halfSuffix))
                                return includesOvertime
                                    ? new List<string> { "Over Under Team 2" + halfSuffix + " (incl. overtime)", "Over Under Team 2" + halfSuffix }
                                    : new List<string> { "Over Under Team 2" + halfSuffix, "Over Under Team 2" + halfSuffix + " (incl. overtime)" };
                            if (includesOvertime)
                                return new List<string> { "Team 2 Total (incl. overtime and penalties)", "Over Under Team 2 (incl. overtime)", "Over Under Team 2", "Team 2 Total" };
                            return new List<string> { "Over Under Team 2", "Over Under Team 2 (incl. overtime)", "Team 2 Total" };
                        }
                            
                        // Otherwise, generic total goals for the match
                        if (clean.Contains("Total Goals", StringComparison.OrdinalIgnoreCase) ||
                            ((!string.IsNullOrEmpty(halfSuffix) || !string.IsNullOrEmpty(periodPrefix)) && clean.Contains("Goals", StringComparison.OrdinalIgnoreCase)))
                        {
                            if (!string.IsNullOrEmpty(periodPrefix)) return new List<string> { $"{periodPrefix}Total" };
                            if (!string.IsNullOrEmpty(halfSuffix)) return new List<string> { "Over Under" + halfSuffix };
                            if (includesOvertime)
                            {
                                return new List<string> { "Total (incl. overtime and penalties)", "Total", "Total Goals", "Over Under Full Time" };
                            }
                            return new List<string> { "Total", "Total Goals", "Over Under Full Time" };
                        }
                    }

                    // Cards Team 1/2 or Match Total
                    if ((clean.Contains("Total Cards", StringComparison.OrdinalIgnoreCase) || clean.Contains("Cards", StringComparison.OrdinalIgnoreCase) || clean.Contains("Bookings", StringComparison.OrdinalIgnoreCase)) &&
                        !IsPlayerCardMarket(clean, selectionName))
                    {
                        if (clean.Contains("Handicap", StringComparison.OrdinalIgnoreCase))
                        {
                            return new List<string> { "Bookings - Handicap" + halfSuffix };
                        }

                        if (clean.Contains("Most Cards", StringComparison.OrdinalIgnoreCase) || clean.Contains("Cards 1X2", StringComparison.OrdinalIgnoreCase) || clean.Contains("More Cards", StringComparison.OrdinalIgnoreCase) || clean.Contains("Most Bookings", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!string.IsNullOrEmpty(halfSuffix)) return new List<string> { "Bookings - 1X2" + halfSuffix };
                            return new List<string> { "Bookings - 1X2" };
                        }

                        if (IsTeam1Target())
                            return new List<string> { $"Bookings - Over/Under [{team1}]" + halfSuffix, "Bookings - Over Under Team 1" + halfSuffix, $"Bookings - Over Under [{team1}]" + halfSuffix };
                        if (IsTeam2Target())
                            return new List<string> { $"Bookings - Over/Under [{team2}]" + halfSuffix, "Bookings - Over Under Team 2" + halfSuffix, $"Bookings - Over Under [{team2}]" + halfSuffix };

                        if (!string.IsNullOrEmpty(halfSuffix))
                            return new List<string> { "Bookings - Over Under" + halfSuffix, "Bookings - Over/Under" + halfSuffix };
                        return new List<string> { "Bookings - Over Under Full Time", "Bookings - Over/Under Full Time", "Total Cards" };
                    }

                    // To Win At Least One Half
                    if (clean.Contains("To Win At Least One Half", StringComparison.OrdinalIgnoreCase) || clean.Contains("To Win Either Half", StringComparison.OrdinalIgnoreCase))
                    {
                        if (IsTeam1Target())
                            return new List<string> { "Team 1 To Win Either Halves" };
                        if (IsTeam2Target())
                            return new List<string> { "Team 2 To Win Either Halves" };
                    }

                    // To Win Both Halves
                    if (clean.Contains("To Win Both Halves", StringComparison.OrdinalIgnoreCase))
                    {
                        if (IsTeam1Target())
                            return new List<string> { "Team 1 to win both halves" };
                        if (IsTeam2Target())
                            return new List<string> { "Team 2 to win both halves" };
                    }

                    bool isHandicapOr1X2Stat = clean.Contains("Handicap", StringComparison.OrdinalIgnoreCase) ||
                                               clean.Contains("1X2", StringComparison.OrdinalIgnoreCase) ||
                                               clean.Contains("Most ", StringComparison.OrdinalIgnoreCase) ||
                                               clean.Contains("More ", StringComparison.OrdinalIgnoreCase);

                    // Goalkeeper Saves Team 1/2 or Match Total
                    if (!isHandicapOr1X2Stat && (clean.Contains("Goalkeeper Saves", StringComparison.OrdinalIgnoreCase) || clean.Contains("Goal Keeper Saves", StringComparison.OrdinalIgnoreCase) || clean.Contains("Saves", StringComparison.OrdinalIgnoreCase)))
                    {
                        if (!clean.Contains("Player", StringComparison.OrdinalIgnoreCase))
                        {
                            if (IsTeam1Target())
                                return new List<string> { $"Goalkeeper Saves - Over Under [{team1}]" + halfSuffix, "Goalkeeper Saves - Over Under Team 1" + halfSuffix, clean };
                            if (IsTeam2Target())
                                return new List<string> { $"Goalkeeper Saves - Over Under [{team2}]" + halfSuffix, "Goalkeeper Saves - Over Under Team 2" + halfSuffix, clean };

                            if (clean.StartsWith("Total ", StringComparison.OrdinalIgnoreCase) || clean.Contains("Total", StringComparison.OrdinalIgnoreCase))
                                return new List<string> { "Goalkeeper Saves - Over/Under Full Time", clean };
                        }
                    }

                    // Fouls Team 1/2 or Match Total
                    if (!isHandicapOr1X2Stat &&
                        (clean.Contains("Fouls", StringComparison.OrdinalIgnoreCase) || clean.Contains("Foul", StringComparison.OrdinalIgnoreCase)) &&
                        !clean.Contains("Suffered", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("Fouled", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("Won", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("Drawn", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!clean.Contains("Player", StringComparison.OrdinalIgnoreCase))
                        {
                            if (IsTeam1Target())
                                return new List<string> { $"Fouls - Over Under [{team1}]" + halfSuffix, "Fouls - Over Under Team 1" + halfSuffix, clean };
                            if (IsTeam2Target())
                                return new List<string> { $"Fouls - Over Under [{team2}]" + halfSuffix, "Fouls - Over Under Team 2" + halfSuffix, clean };

                            if (clean.StartsWith("Total ", StringComparison.OrdinalIgnoreCase) || clean.Contains("Total", StringComparison.OrdinalIgnoreCase))
                                return new List<string> { "Fouls - Over/Under Full Time", clean };
                        }
                    }

                    // Shots / Shots on Target Team 1/2 or Match Total
                    if (!isHandicapOr1X2Stat && (clean.Contains("Shots", StringComparison.OrdinalIgnoreCase) || clean.Contains("Shot", StringComparison.OrdinalIgnoreCase)))
                    {
                        if (!clean.Contains("Player", StringComparison.OrdinalIgnoreCase))
                        {
                            bool isOnTarget = clean.Contains("Target", StringComparison.OrdinalIgnoreCase) || clean.Contains("Goal", StringComparison.OrdinalIgnoreCase);
                            string shotPrefix = isOnTarget ? "Shots On Goal" : "Shots";
                            if (IsTeam1Target())
                                return new List<string> { $"{shotPrefix} - Over Under [{team1}]" + halfSuffix, $"{shotPrefix} - Over Under Team 1" + halfSuffix, clean };
                            if (IsTeam2Target())
                                return new List<string> { $"{shotPrefix} - Over Under [{team2}]" + halfSuffix, $"{shotPrefix} - Over Under Team 2" + halfSuffix, clean };

                            if (clean.StartsWith("Total ", StringComparison.OrdinalIgnoreCase) || clean.Contains("Total", StringComparison.OrdinalIgnoreCase))
                                return new List<string> { $"{shotPrefix} - Over/Under Full Time", clean };
                        }
                    }

                    // Tackles Team 1/2 or Match Total
                    if (!isHandicapOr1X2Stat && (clean.Contains("Tackles", StringComparison.OrdinalIgnoreCase) || clean.Contains("Tackle", StringComparison.OrdinalIgnoreCase)))
                    {
                        if (!clean.Contains("Player", StringComparison.OrdinalIgnoreCase))
                        {
                            if (IsTeam1Target())
                                return new List<string> { $"Tackles - Over Under [{team1}]" + halfSuffix, "Tackles - Over Under Team 1" + halfSuffix, clean };
                            if (IsTeam2Target())
                                return new List<string> { $"Tackles - Over Under [{team2}]" + halfSuffix, "Tackles - Over Under Team 2" + halfSuffix, clean };

                            if (clean.StartsWith("Total ", StringComparison.OrdinalIgnoreCase) || clean.Contains("Total", StringComparison.OrdinalIgnoreCase))
                                return new List<string> { "Tackles - Over/Under Full Time", clean };
                        }
                    }

                    // Tennis: To Win a Set / Both Players To Win a Set / Set To Nil
                    if (string.IsNullOrEmpty(setOrdinal) && clean.Contains("Set", StringComparison.OrdinalIgnoreCase))
                    {
                        if (clean.Contains("Both", StringComparison.OrdinalIgnoreCase) &&
                            (clean.Contains("Win", StringComparison.OrdinalIgnoreCase) || clean.Contains("Take", StringComparison.OrdinalIgnoreCase)))
                        {
                            return new List<string> { "Both Participants To Win a Set" };
                        }

                        if (clean.Contains("Nil", StringComparison.OrdinalIgnoreCase))
                        {
                            if (IsTeam1Target()) return new List<string> { "Participant 1 To Win a Set To Nil" };
                            if (IsTeam2Target()) return new List<string> { "Participant 2 To Win a Set To Nil" };
                            return new List<string> { "Any Set To Nil" };
                        }

                        if (clean.Contains("To Win a Set", StringComparison.OrdinalIgnoreCase) ||
                            clean.Contains("Win a Set", StringComparison.OrdinalIgnoreCase) ||
                            clean.Contains("To Win At Least One Set", StringComparison.OrdinalIgnoreCase) ||
                            clean.Contains("To Win At Least 1 Set", StringComparison.OrdinalIgnoreCase) ||
                            clean.Contains("To Win 1+ Set", StringComparison.OrdinalIgnoreCase))
                        {
                            if (IsTeam1Target()) return new List<string> { "Participant 1 To Win a Set" };
                            if (IsTeam2Target()) return new List<string> { "Participant 2 To Win a Set" };
                        }
                    }

                    // Tennis: Service Games
                    if (clean.Contains("Service Game", StringComparison.OrdinalIgnoreCase))
                    {
                        if (IsTeam1Target()) return new List<string> { "Participant 1 Total Service Games" };
                        if (IsTeam2Target()) return new List<string> { "Participant 2 Total Service Games" };
                        return new List<string> { "Total Service Games" };
                    }

                    // Tennis: Games (Handicap, Most Games, Participant 1/2 Total Games, Match Total Games)
                    if (!isSpecificSingleGame && string.IsNullOrEmpty(setOrdinal) &&
                        !clean.Contains("Service", StringComparison.OrdinalIgnoreCase) &&
                        (clean.Contains("Games", StringComparison.OrdinalIgnoreCase) || clean.Contains("Game Handicap", StringComparison.OrdinalIgnoreCase)))
                    {
                        if (clean.Contains("Handicap", StringComparison.OrdinalIgnoreCase))
                        {
                            return new List<string> { "Game Handicap" };
                        }

                        if (clean.Contains("Most Games", StringComparison.OrdinalIgnoreCase) ||
                            clean.Contains("More Games", StringComparison.OrdinalIgnoreCase) ||
                            clean.Contains("Games 1X2", StringComparison.OrdinalIgnoreCase))
                        {
                            return new List<string> { "Most Games" };
                        }

                        if (clean.Contains("Odd", StringComparison.OrdinalIgnoreCase) &&
                            clean.Contains("Even", StringComparison.OrdinalIgnoreCase))
                        {
                            return new List<string> { "Odd Even Games" };
                        }

                        if (!clean.Contains("Winner", StringComparison.OrdinalIgnoreCase) &&
                            !clean.Contains("Result", StringComparison.OrdinalIgnoreCase))
                        {
                            if (IsTeam1Target()) return new List<string> { "Participant 1 Total Games" };
                            if (IsTeam2Target()) return new List<string> { "Participant 2 Total Games" };
                            return new List<string> { "Total Games Over Under", "Total Games" };
                        }
                    }

                    // Tennis: Aces
                    if (clean.Contains("Aces", StringComparison.OrdinalIgnoreCase) ||
                        System.Text.RegularExpressions.Regex.IsMatch(clean, @"\bAce\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    {
                        if (clean.Contains("Handicap", StringComparison.OrdinalIgnoreCase))
                            return new List<string> { "Aces Handicap" };
                        if (clean.Contains("Draw No Bet", StringComparison.OrdinalIgnoreCase) || clean.Contains("DNB", StringComparison.OrdinalIgnoreCase))
                            return new List<string> { "Aces Draw No Bet" };
                        if (clean.Contains("Most", StringComparison.OrdinalIgnoreCase) || clean.Contains("More", StringComparison.OrdinalIgnoreCase) ||
                            clean.Contains("1X2", StringComparison.OrdinalIgnoreCase) || clean.Contains("Result", StringComparison.OrdinalIgnoreCase) ||
                            clean.Contains("Winner", StringComparison.OrdinalIgnoreCase))
                            return new List<string> { "Aces Result" };
                        if (clean.Contains("Exact", StringComparison.OrdinalIgnoreCase))
                            return new List<string> { "Exact Aces" };
                        if (IsTeam1Target()) return new List<string> { "Participant 1 Total Aces" };
                        if (IsTeam2Target()) return new List<string> { "Participant 2 Total Aces" };
                        return new List<string> { "Total Aces" };
                    }

                    // Tennis: Double Faults
                    if (clean.Contains("Double Fault", StringComparison.OrdinalIgnoreCase))
                    {
                        if (clean.Contains("Handicap", StringComparison.OrdinalIgnoreCase))
                            return new List<string> { "Double Faults Handicap" };
                        if (clean.Contains("Draw No Bet", StringComparison.OrdinalIgnoreCase) || clean.Contains("DNB", StringComparison.OrdinalIgnoreCase))
                            return new List<string> { "Double Faults Draw No Bet" };
                        if (clean.Contains("Most", StringComparison.OrdinalIgnoreCase) || clean.Contains("More", StringComparison.OrdinalIgnoreCase) ||
                            clean.Contains("1X2", StringComparison.OrdinalIgnoreCase) || clean.Contains("Result", StringComparison.OrdinalIgnoreCase) ||
                            clean.Contains("Winner", StringComparison.OrdinalIgnoreCase))
                            return new List<string> { "Double Faults Result" };
                        if (clean.Contains("Exact", StringComparison.OrdinalIgnoreCase))
                            return new List<string> { "Exact Double Faults" };
                        if (IsTeam1Target()) return new List<string> { "Participant 1 Total Double Faults" };
                        if (IsTeam2Target()) return new List<string> { "Participant 2 Total Double Faults" };
                        return new List<string> { "Total Double Faults" };
                    }

                    // Tennis: Break Points
                    if (clean.Contains("Break Point", StringComparison.OrdinalIgnoreCase))
                    {
                        if (IsTeam1Target()) return new List<string> { "Participant 1 Total Break Points" };
                        if (IsTeam2Target()) return new List<string> { "Participant 2 Total Break Points" };
                        return new List<string> { "Total Break Points" };
                    }

                    // Tennis: Breaks / Service Breaks
                    if ((clean.Contains("Breaks", StringComparison.OrdinalIgnoreCase) ||
                         clean.Contains("Break of Serve", StringComparison.OrdinalIgnoreCase) ||
                         clean.Contains("Service Break", StringComparison.OrdinalIgnoreCase) ||
                         clean.Contains("First Break", StringComparison.OrdinalIgnoreCase) ||
                         clean.Contains("1st Break", StringComparison.OrdinalIgnoreCase)) &&
                        !clean.Contains("Tie", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("Point", StringComparison.OrdinalIgnoreCase))
                    {
                        if (clean.Contains("First Break", StringComparison.OrdinalIgnoreCase) || clean.Contains("1st Break", StringComparison.OrdinalIgnoreCase))
                            return new List<string> { "First Break" };
                        if (clean.Contains("Handicap", StringComparison.OrdinalIgnoreCase))
                            return new List<string> { "Breaks Handicap" };
                        if (clean.Contains("Draw No Bet", StringComparison.OrdinalIgnoreCase) || clean.Contains("DNB", StringComparison.OrdinalIgnoreCase))
                            return new List<string> { "Breaks Draw No Bet" };
                        if (clean.Contains("Most", StringComparison.OrdinalIgnoreCase) || clean.Contains("More", StringComparison.OrdinalIgnoreCase) ||
                            clean.Contains("1X2", StringComparison.OrdinalIgnoreCase) || clean.Contains("Result", StringComparison.OrdinalIgnoreCase) ||
                            clean.Contains("Winner", StringComparison.OrdinalIgnoreCase))
                            return new List<string> { "Breaks Result" };
                        if (IsTeam1Target()) return new List<string> { "Participant 1 Total Breaks" };
                        if (IsTeam2Target()) return new List<string> { "Participant 2 Total Breaks" };
                        return new List<string> { "Total Breaks" };
                    }
                }
            }
            
            // 1. Check if we have an explicit override for this issue
            if (_marketAliases.TryGetValue(clean, out var mapped))
            {
                if (mapped.Contains('|'))
                {
                    return mapped.Split('|').Select(m => m.Trim()).ToList();
                }
                return new List<string> { mapped };
            }

            string cleanBase = System.Text.RegularExpressions.Regex.Replace(clean, @"\s+(?:Over\s*[\/-]\s*Under|Over\/Under|Under\/Over)(?:\s+\d+(?:\.\d+)?)?$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            if (!string.Equals(cleanBase, clean, StringComparison.OrdinalIgnoreCase) && _marketAliases.TryGetValue(cleanBase, out var mappedBase))
            {
                if (mappedBase.Contains('|'))
                {
                    return mappedBase.Split('|').Select(m => m.Trim()).ToList();
                }
                return new List<string> { mappedBase };
            }

            if (!hasSubPeriodModifier &&
                (cleanBase.Equals("Total Points", StringComparison.OrdinalIgnoreCase) || clean.StartsWith("Total Points", StringComparison.OrdinalIgnoreCase)) && 
                !string.IsNullOrEmpty(selectionName) && HasPlayerName(selectionName, effectiveMatchName))
            {
                return new List<string> { "Over Under Player Points (incl. overtime)", "Player Points (incl. overtime)", "Player Points" };
            }

            // Clean up common AI hallucinations where it appends numbers to Total Goals
            if (clean.StartsWith("Total Goals", StringComparison.OrdinalIgnoreCase))
            {
                if (clean.Contains("incl. overtime", StringComparison.OrdinalIgnoreCase) || clean.Contains("including overtime", StringComparison.OrdinalIgnoreCase))
                {
                    return new List<string> { "Total (incl. overtime and penalties)", "Total", "Total Goals", "Over Under Full Time" };
                }
                return new List<string> { "Total", "Total Goals", "Over Under Full Time" };
            }

            if (clean.Contains("Regular Time", StringComparison.OrdinalIgnoreCase))
            {
                if (clean.Contains("Handicap", StringComparison.OrdinalIgnoreCase) || clean.Contains("Spread", StringComparison.OrdinalIgnoreCase))
                {
                    return new List<string> { "Handicap", "Asian Handicap" };
                }
                if (clean.Contains("Total", StringComparison.OrdinalIgnoreCase) || clean.Contains("Goals", StringComparison.OrdinalIgnoreCase) || clean.Contains("Points", StringComparison.OrdinalIgnoreCase) || clean.Contains("Over", StringComparison.OrdinalIgnoreCase) || clean.Contains("Under", StringComparison.OrdinalIgnoreCase))
                {
                    return new List<string> { "Total", "Total Goals", "Over Under Full Time" };
                }
                if (clean.Contains("Both Teams", StringComparison.OrdinalIgnoreCase) || clean.Contains("BTTS", StringComparison.OrdinalIgnoreCase))
                {
                    return new List<string> { "Both Teams To Score" };
                }
                if (clean.Contains("Double Chance", StringComparison.OrdinalIgnoreCase))
                {
                    return new List<string> { "Double Chance", "Double Chance Full Time" };
                }
                if (clean.Contains("Draw No Bet", StringComparison.OrdinalIgnoreCase) || clean.Contains("DNB", StringComparison.OrdinalIgnoreCase))
                {
                    return new List<string> { "Draw No Bet" };
                }
                if (clean.Contains("Odds", StringComparison.OrdinalIgnoreCase) || 
                    clean.Contains("Result", StringComparison.OrdinalIgnoreCase) || 
                    clean.Contains("Winner", StringComparison.OrdinalIgnoreCase) || 
                    clean.Contains("1X2", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("3-Way", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("3 Way", StringComparison.OrdinalIgnoreCase) ||
                    clean.Equals("Regular Time", StringComparison.OrdinalIgnoreCase))
                {
                    return new List<string> { "Regular Time Result", "Full Time Result" };
                }
            }
            
            if (!hasSubPeriodModifier &&
                (clean.Contains("Moneyline", StringComparison.OrdinalIgnoreCase) || 
                 clean.Contains("Money Line", StringComparison.OrdinalIgnoreCase) ||
                 clean.Contains("Winner", StringComparison.OrdinalIgnoreCase) ||
                 clean.Contains("To Win", StringComparison.OrdinalIgnoreCase) ||
                 clean.Contains("To Qualify", StringComparison.OrdinalIgnoreCase) ||
                 clean.Contains("Advance", StringComparison.OrdinalIgnoreCase)))
            {
                if (clean.Contains("overtime", StringComparison.OrdinalIgnoreCase) || 
                    clean.Contains("extra time", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("shootout", StringComparison.OrdinalIgnoreCase) || 
                    clean.Contains("penalties", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("ot", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("To Qualify", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("Advance", StringComparison.OrdinalIgnoreCase))
                {
                    return new List<string> { "Winner (incl. overtime and penalties)", "Winner (incl. overtime)", "Moneyline", "To Qualify" };
                }
            }

            if (clean.Contains("Corners", StringComparison.OrdinalIgnoreCase) && clean.Contains("Handicap", StringComparison.OrdinalIgnoreCase))
            {
                return new List<string> { "Corners Handicap" + halfSuffix };
            }

            if ((clean.Contains("Most", StringComparison.OrdinalIgnoreCase) || clean.Contains("More", StringComparison.OrdinalIgnoreCase) || clean.Contains("1X2", StringComparison.OrdinalIgnoreCase)) && clean.Contains("Corners", StringComparison.OrdinalIgnoreCase))
            {
                return new List<string> { "Most Corners" + halfSuffix };
            }

            if ((clean.Contains("Most", StringComparison.OrdinalIgnoreCase) || clean.Contains("More", StringComparison.OrdinalIgnoreCase) || clean.Contains("1X2", StringComparison.OrdinalIgnoreCase)) && (clean.Contains("Cards", StringComparison.OrdinalIgnoreCase) || clean.Contains("Bookings", StringComparison.OrdinalIgnoreCase)))
            {
                return new List<string> { "Most Cards" + halfSuffix };
            }

            if ((clean.Contains("Total", StringComparison.OrdinalIgnoreCase) || clean.Contains("Over", StringComparison.OrdinalIgnoreCase) || clean.Contains("Under", StringComparison.OrdinalIgnoreCase)) &&
                (clean.Contains("Cards", StringComparison.OrdinalIgnoreCase) || clean.Contains("Bookings", StringComparison.OrdinalIgnoreCase)) &&
                !IsPlayerCardMarket(clean, selectionName))
            {
                if (!string.IsNullOrEmpty(halfSuffix))
                    return new List<string> { "Cards Over Under" + halfSuffix, "Total Cards" + halfSuffix };
                return new List<string> { "Total Cards", "Cards Over Under", "Total Bookings", "Bookings Over/Under" };
            }

            if ((clean.Contains("Both Teams", StringComparison.OrdinalIgnoreCase) && clean.Contains("Score", StringComparison.OrdinalIgnoreCase)) ||
                clean.Contains("Both to Score", StringComparison.OrdinalIgnoreCase) ||
                clean.Contains("BTTS", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(halfSuffix))
                    return new List<string> { "Both Teams To Score" + halfSuffix, "Both Teams To Score -" + halfSuffix };
                if (!string.IsNullOrEmpty(periodPrefix))
                    return new List<string> { $"{periodPrefix}Both Teams To Score" };
                return new List<string> { "Both Teams To Score" };
            }

            if ((clean.StartsWith("Total Goalkeeper Saves", StringComparison.OrdinalIgnoreCase) ||
                 clean.StartsWith("Total Goal Keeper Saves", StringComparison.OrdinalIgnoreCase) ||
                 clean.StartsWith("Total Fouls", StringComparison.OrdinalIgnoreCase) ||
                 clean.StartsWith("Total Shots", StringComparison.OrdinalIgnoreCase) ||
                 clean.StartsWith("Total Tackles", StringComparison.OrdinalIgnoreCase)) &&
                !HasPlayerName(selectionName, effectiveMatchName))
            {
                return new List<string> { clean };
            }

            // --- Dynamic Player Prop Catch-Alls (handles when AI appends player names to the market, plus First Quarter Basketball player props) ---
            bool isFirstQuarter = string.Equals(quarterOrdinal, "First Quarter", StringComparison.OrdinalIgnoreCase);
            if (!hasSubPeriodModifier || isFirstQuarter)
            {
                string bballPlayerSuffix = isFirstQuarter ? " First Quarter" : " (incl. overtime)";

                bool isExplicitPlayerProp = clean.Contains("Player", StringComparison.OrdinalIgnoreCase) ||
                                            clean.Contains("Player's", StringComparison.OrdinalIgnoreCase);

                bool isTeamOrTotalStat = !isExplicitPlayerProp && (
                                         clean.Contains("Team 1", StringComparison.OrdinalIgnoreCase) ||
                                         clean.Contains("Team 2", StringComparison.OrdinalIgnoreCase) ||
                                         ((clean.Contains("Team", StringComparison.OrdinalIgnoreCase) ||
                                           clean.Contains("Home", StringComparison.OrdinalIgnoreCase) ||
                                           clean.Contains("Away", StringComparison.OrdinalIgnoreCase) ||
                                           clean.Contains("Total", StringComparison.OrdinalIgnoreCase) ||
                                           isFirstQuarter) && 
                                          !HasPlayerName(selectionName, effectiveMatchName) && !HasPlayerName(clean, effectiveMatchName)));

                if (!isFirstQuarter)
                {
                    // Basketball Yes-only Player Specials: Double-Double, Triple-Double, First Point, First 3 Point FG
                    if (System.Text.RegularExpressions.Regex.IsMatch(clean, @"\btriple\s*(?:-\s*|\s+)double\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
                        (!string.IsNullOrEmpty(selectionName) && System.Text.RegularExpressions.Regex.IsMatch(selectionName, @"\btriple\s*(?:-\s*|\s+)double\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
                    {
                        return new List<string> { "Player Triple - Double" };
                    }

                    if (!clean.Contains("Double Fault", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("Double Chance", StringComparison.OrdinalIgnoreCase) &&
                        (System.Text.RegularExpressions.Regex.IsMatch(clean, @"\bdouble\s*(?:-\s*|\s+)double\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
                         (!string.IsNullOrEmpty(selectionName) && System.Text.RegularExpressions.Regex.IsMatch(selectionName, @"\bdouble\s*(?:-\s*|\s+)double\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))))
                    {
                        return new List<string> { "Player Double - Double" };
                    }

                    if (System.Text.RegularExpressions.Regex.IsMatch(clean, @"\b(?:first|1st)\s+(?:3\s*(?:-\s*|\s+)?point(?:er)?(?:\s+fg|\s+field\s+goal)?|three\s*(?:-\s*|\s+)?point(?:er)?(?:\s+fg|\s+field\s+goal)?|3pt|three)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    {
                        return new List<string> { "Player First 3 Point FG" };
                    }

                    if (System.Text.RegularExpressions.Regex.IsMatch(clean, @"\b(?:player\s+)?(?:to\s+score\s+)?(?:first|1st)\s+(?:point|basket|field\s+goal)(?:\s+scorer)?\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    {
                        return new List<string> { "Player First Point" };
                    }
                }

                // Combined Goal or Assist / Score or Assist (distinct from pure goals or pure assists)
                bool isGoalOrAssist = !isFirstQuarter &&
                                      (((clean.Contains("Score", StringComparison.OrdinalIgnoreCase) || clean.Contains("Goal", StringComparison.OrdinalIgnoreCase)) &&
                                        clean.Contains("Assist", StringComparison.OrdinalIgnoreCase) &&
                                        !clean.Contains("Point", StringComparison.OrdinalIgnoreCase) &&
                                        !clean.Contains("Rebound", StringComparison.OrdinalIgnoreCase)) ||
                                       (!string.IsNullOrEmpty(selectionName) &&
                                        (selectionName.Contains("Score", StringComparison.OrdinalIgnoreCase) || selectionName.Contains("Goal", StringComparison.OrdinalIgnoreCase)) &&
                                        selectionName.Contains("Assist", StringComparison.OrdinalIgnoreCase) &&
                                        !selectionName.Contains("Point", StringComparison.OrdinalIgnoreCase) &&
                                        !selectionName.Contains("Rebound", StringComparison.OrdinalIgnoreCase)));

                if (isGoalOrAssist)
                {
                    return new List<string> { "Over Under Player Goals or Assists (incl. overtime)", "Player Goal or Assist", "To Score or Assist", "Goal or Assist" };
                }

                // Basketball Player Stat Combinations & Single Stats
                string statSearchText = clean;
                if (!string.IsNullOrEmpty(selectionName) &&
                    (isExplicitPlayerProp || clean.Equals("Over/Under", StringComparison.OrdinalIgnoreCase) || clean.Equals("Over Under", StringComparison.OrdinalIgnoreCase) || clean.Equals("Total", StringComparison.OrdinalIgnoreCase)))
                {
                    statSearchText = $"{clean} {selectionName}";
                }

                bool has3PtStat = System.Text.RegularExpressions.Regex.IsMatch(
                    statSearchText,
                    @"\b(?:3\s*(?:-\s*|\s+)?point(?:er)?s?(?:\s+fg|\s+field\s+goals?|\s+made)?|three\s*(?:-\s*|\s+)?point(?:er)?s?(?:\s+fg|\s+field\s+goals?|\s+made)?|3\s*pt(?:s)?(?:\s+fg|\s+made)?|threes(?:\s+made)?|made\s+threes|3fg)\b",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                string textWithout3Pt = System.Text.RegularExpressions.Regex.Replace(
                    statSearchText,
                    @"\b(?:3\s*(?:-\s*|\s+)?point(?:er)?s?(?:\s+fg|\s+field\s+goals?|\s+made)?|three\s*(?:-\s*|\s+)?point(?:er)?s?(?:\s+fg|\s+field\s+goals?|\s+made)?|3\s*pt(?:s)?(?:\s+fg|\s+made)?|threes(?:\s+made)?|made\s+threes|3fg|break\s+points?|power\s*play\s+points?)\b",
                    " ",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                bool hasPraShorthand = System.Text.RegularExpressions.Regex.IsMatch(textWithout3Pt, @"\bpra\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                bool hasStocksShorthand = System.Text.RegularExpressions.Regex.IsMatch(textWithout3Pt, @"\bstocks\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                bool hasPtsStat = hasPraShorthand || System.Text.RegularExpressions.Regex.IsMatch(textWithout3Pt, @"\b(?:points?|pts)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                bool hasRebStat = hasPraShorthand || System.Text.RegularExpressions.Regex.IsMatch(textWithout3Pt, @"\b(?:rebounds?|rebs?)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                bool hasAstStat = hasPraShorthand || System.Text.RegularExpressions.Regex.IsMatch(textWithout3Pt, @"\b(?:assists?|asts?)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                bool hasStlStat = hasStocksShorthand || System.Text.RegularExpressions.Regex.IsMatch(textWithout3Pt, @"\b(?:steals?|stls?)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                bool hasBlkStat = hasStocksShorthand || System.Text.RegularExpressions.Regex.IsMatch(textWithout3Pt, @"\b(?:blocks?|blks?|blocked\s+shots?)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                bool hasTovStat = System.Text.RegularExpressions.Regex.IsMatch(textWithout3Pt, @"\b(?:turnovers?|tovs?)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                bool hasPlayerIndicator = isExplicitPlayerProp ||
                                          HasPlayerName(clean, effectiveMatchName) ||
                                          HasPlayerName(selectionName, effectiveMatchName) ||
                                          clean.Contains("to record", StringComparison.OrdinalIgnoreCase) ||
                                          clean.Contains("to score", StringComparison.OrdinalIgnoreCase) ||
                                          clean.Contains("at least", StringComparison.OrdinalIgnoreCase) ||
                                          clean.Contains("over under", StringComparison.OrdinalIgnoreCase) ||
                                          clean.Contains("over/under", StringComparison.OrdinalIgnoreCase);

                if (hasPtsStat && hasRebStat && hasAstStat)
                {
                    if (isFirstQuarter) return new List<string> { "Over Under Player Points + Assists + Rebounds First Quarter" };
                    return new List<string> { "Over Under Player Points + Assists + Rebounds (incl. overtime)", "Player Points + Assists + Rebounds (incl. overtime)" };
                }

                if (hasPtsStat && hasRebStat && !hasAstStat)
                {
                    if (isFirstQuarter) return new List<string> { "Over Under Player Points + Rebounds First Quarter" };
                    return new List<string> { "Over Under Player Points + Rebounds (incl. overtime)", "Player Points + Rebounds (incl. overtime)" };
                }

                if (hasPtsStat && hasAstStat && !hasRebStat)
                {
                    if (isFirstQuarter) return new List<string> { "Over Under Player Points + Assists First Quarter" };
                    return new List<string> { "Over Under Player Points + Assists (incl. overtime)", "Player Points + Assists (incl. overtime)" };
                }

                if (hasRebStat && hasAstStat && !hasPtsStat)
                {
                    if (isFirstQuarter) return new List<string> { "Over Under Player Assists + Rebounds First Quarter" };
                    return new List<string> { "Over Under Player Assists + Rebounds (incl. overtime)", "Player Assists + Rebounds (incl. overtime)" };
                }

                if (hasStlStat && hasBlkStat)
                {
                    if (isFirstQuarter) return new List<string> { "Over Under Player Steals + Blocks First Quarter" };
                    return new List<string> { "Over Under Player Steals + Blocks (incl. overtime)", "Player Steals + Blocks (incl. overtime)" };
                }

                if (!isTeamOrTotalStat && has3PtStat && (hasPlayerIndicator || !clean.Contains("Team", StringComparison.OrdinalIgnoreCase)))
                {
                    if (isFirstQuarter) return new List<string> { "Over Under Player 3 Point FG First Quarter" };
                    return new List<string> { "Over Under Player 3 Point FG (incl. overtime)", "Player 3 Point FG (incl. overtime)" };
                }

                if (!isTeamOrTotalStat && hasRebStat)
                {
                    if (isFirstQuarter) return new List<string> { "Over Under Player Rebounds First Quarter" };
                    return new List<string> { "Over Under Player Rebounds (incl. overtime)", "Player Rebounds (incl. overtime)" };
                }

                if (!isTeamOrTotalStat && hasStlStat)
                {
                    if (isFirstQuarter) return new List<string> { "Over Under Player Steals First Quarter" };
                    return new List<string> { "Over Under Player Steals (incl. overtime)", "Player Steals (incl. overtime)" };
                }

                if (!isTeamOrTotalStat && hasBlkStat && (hasPlayerIndicator || !clean.Contains("Team", StringComparison.OrdinalIgnoreCase)))
                {
                    if (isFirstQuarter) return new List<string> { "Over Under Player Blocks First Quarter" };
                    return new List<string> { "Over Under Player Blocks (incl. overtime)", "Player Blocks (incl. overtime)" };
                }

                if (!isTeamOrTotalStat && hasTovStat && (hasPlayerIndicator || !clean.Contains("Team", StringComparison.OrdinalIgnoreCase)))
                {
                    if (isFirstQuarter) return new List<string> { "Over Under Player Turnovers First Quarter" };
                    return new List<string> { "Over Under Player Turnovers (incl. overtime)", "Player Turnovers (incl. overtime)" };
                }

                if (!isTeamOrTotalStat && (clean.Contains("Points", StringComparison.OrdinalIgnoreCase) || clean.Contains("Point", StringComparison.OrdinalIgnoreCase) || hasPtsStat))
                {
                    if (hasPlayerIndicator)
                    {
                        if (!isFirstQuarter && (clean.Contains("Power Play", StringComparison.OrdinalIgnoreCase) || clean.Contains("Powerplay", StringComparison.OrdinalIgnoreCase) || System.Text.RegularExpressions.Regex.IsMatch(clean, @"\bPPP\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
                        {
                            return new List<string> { "Over Under Player Power Play Points (incl. overtime)", "Player Power Play Points (incl. overtime)" };
                        }
                        if (isFirstQuarter)
                        {
                            return new List<string> { "Over Under Player Points First Quarter" };
                        }
                        return new List<string> { "Over Under Player Points (incl. overtime)", "Player Points (incl. overtime)", "Player Points" };
                    }
                }

                if (!isFirstQuarter)
                {
                    if ((clean.Contains("Score", StringComparison.OrdinalIgnoreCase) || clean.Contains("Goal", StringComparison.OrdinalIgnoreCase)) &&
                        (clean.Contains("Book", StringComparison.OrdinalIgnoreCase) || clean.Contains("Card", StringComparison.OrdinalIgnoreCase)))
                    {
                        return new List<string> { "Player to Score or be Booked", "Player to Score or Carded", "To Score or be Booked", "Score or Booked" };
                    }

                    bool isFirstLastOrNextGoalscorer = System.Text.RegularExpressions.Regex.IsMatch(
                        clean,
                        @"\b(?:first|last|next|1st|2nd|3rd)\s+(?:goal\s*scorer|player\s+to\s+score|to\s+score)\b|\bGoal\s+\d+\b",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                    if (!isFirstLastOrNextGoalscorer &&
                        !clean.Contains("Assist", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("Point", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("Book", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("Card", StringComparison.OrdinalIgnoreCase) &&
                       (clean.Contains("Goalscorer", StringComparison.OrdinalIgnoreCase) || clean.Contains("Goal Scorer", StringComparison.OrdinalIgnoreCase) || 
                       (clean.Contains("To Score", StringComparison.OrdinalIgnoreCase) && !clean.Contains("Team", StringComparison.OrdinalIgnoreCase) && !clean.Contains("Both", StringComparison.OrdinalIgnoreCase)) ||
                       clean.Contains("Will/Will not Score", StringComparison.OrdinalIgnoreCase) ||
                       clean.Equals("Will Score", StringComparison.OrdinalIgnoreCase) ||
                       (clean.Contains("Player", StringComparison.OrdinalIgnoreCase) && clean.Contains("Goal", StringComparison.OrdinalIgnoreCase) && !clean.Contains("Goalkeeper", StringComparison.OrdinalIgnoreCase) && !clean.Contains("Shot", StringComparison.OrdinalIgnoreCase))))
                    {
                        return new List<string> { "Over Under Player Goals (incl. overtime)", "Anytime Goal Scorer", "Player Goals (incl. overtime)", "Player Goals", "Goals", "Goal" };
                    }
                    if (!isTeamOrTotalStat && (clean.Contains("Shots on Target", StringComparison.OrdinalIgnoreCase) || clean.Contains("Shots On Goal", StringComparison.OrdinalIgnoreCase)))
                    {
                        if (isExplicitPlayerProp || HasPlayerName(clean, effectiveMatchName) || HasPlayerName(selectionName, effectiveMatchName))
                        {
                            return new List<string> { "Over Under Player Shots On Goal (incl. overtime)", "Player Shots On Goal (incl. overtime)", "Player's shot on target", "Player Shots on Target", "Player Shots on Target (incl. overtime)", "Shots on Target", "Shots On Goal" };
                        }
                        return new List<string> { clean };
                    }
                    if (!isTeamOrTotalStat && (clean.Contains("Goalkeeper Saves", StringComparison.OrdinalIgnoreCase) || clean.Contains("Goal Keeper Saves", StringComparison.OrdinalIgnoreCase)))
                    {
                        if (isExplicitPlayerProp || HasPlayerName(clean, effectiveMatchName) || HasPlayerName(selectionName, effectiveMatchName))
                        {
                            return new List<string> { "Over Under Player Goalkeeper Saves (incl. overtime)", "Player Goalkeeper Saves (incl. overtime)", "Player Goalkeeper Saves", "Goalkeeper Saves", "Goal Keeper Saves", "Saves" };
                        }
                        return new List<string> { clean };
                    }
                    if (!isTeamOrTotalStat && clean.Contains("Shots", StringComparison.OrdinalIgnoreCase))
                    {
                        if (isExplicitPlayerProp || HasPlayerName(clean, effectiveMatchName) || HasPlayerName(selectionName, effectiveMatchName))
                        {
                            return new List<string> { "Over Under Player Shots (incl. overtime)", "Player Shots (incl. overtime)", "Player Shots", "Shots", "Shot" };
                        }
                        return new List<string> { clean };
                    }
                    bool isFoulsWonOrDrawn = ((clean.Contains("Fouls", StringComparison.OrdinalIgnoreCase) || clean.Contains("Foul", StringComparison.OrdinalIgnoreCase)) &&
                                              (clean.Contains("Suffered", StringComparison.OrdinalIgnoreCase) ||
                                               clean.Contains("Won", StringComparison.OrdinalIgnoreCase) ||
                                               clean.Contains("Drawn", StringComparison.OrdinalIgnoreCase) ||
                                               System.Text.RegularExpressions.Regex.IsMatch(clean, @"\bwin\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))) ||
                                             clean.Contains("Fouled", StringComparison.OrdinalIgnoreCase);

                    if (!isTeamOrTotalStat && isFoulsWonOrDrawn)
                    {
                        if (isExplicitPlayerProp || HasPlayerName(clean, effectiveMatchName) || HasPlayerName(selectionName, effectiveMatchName) ||
                            clean.Contains("Fouled", StringComparison.OrdinalIgnoreCase))
                        {
                            return new List<string>
                            {
                                "Over Under Player Fouls Won (incl. overtime)",
                                "Player Fouls Won (incl. overtime)",
                                "Player To Win A Foul (incl. overtime)",
                                "Player Fouls Won",
                                "Over Under Player Fouls Drawn (incl. overtime)",
                                "Player Fouls Drawn (incl. overtime)",
                                "Player Fouls Drawn",
                                "Over Under Player Fouls Suffered (incl. overtime)",
                                "Player Fouls Suffered (incl. overtime)",
                                "Player Fouls Suffered",
                                "Over Under Player To Be Fouled (incl. overtime)",
                                "Player To Be Fouled (incl. overtime)",
                                "Player To Be Fouled",
                                "Fouls Won",
                                "Fouls Drawn",
                                "Fouls Suffered"
                            };
                        }
                        return new List<string> { clean };
                    }

                    if (!isTeamOrTotalStat && clean.Contains("Fouls", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("Suffered", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("Fouled", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("Won", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("Drawn", StringComparison.OrdinalIgnoreCase))
                    {
                        if (isExplicitPlayerProp || HasPlayerName(clean, effectiveMatchName) || HasPlayerName(selectionName, effectiveMatchName))
                        {
                            return new List<string> { "Over Under Player Fouls Committed (incl. overtime)", "Player Fouls Committed (incl. overtime)", "Player Fouls Committed", "Fouls Committed", "Fouls", "Foul" };
                        }
                        return new List<string> { clean };
                    }
                }

                if (!clean.Contains("Score", StringComparison.OrdinalIgnoreCase) && !clean.Contains("Goal", StringComparison.OrdinalIgnoreCase) &&
                   (clean.Contains("Assists", StringComparison.OrdinalIgnoreCase) || clean.Contains("Assist", StringComparison.OrdinalIgnoreCase) || hasAstStat))
                {
                    if (isFirstQuarter) return new List<string> { "Over Under Player Assists First Quarter" };
                    return new List<string> { "Over Under Player Assists (incl. overtime)", "Player Assists (incl. overtime)", "Player Assists", "To Provide An Assist", "Anytime Assist", "Assists", "Assist" };
                }

                if (!isFirstQuarter)
                {
                    if (!isTeamOrTotalStat && (clean.Contains("Tackles", StringComparison.OrdinalIgnoreCase) || clean.Contains("Tackle", StringComparison.OrdinalIgnoreCase)))
                    {
                        if (isExplicitPlayerProp || HasPlayerName(clean, effectiveMatchName) || HasPlayerName(selectionName, effectiveMatchName))
                        {
                            return new List<string> { "Over Under Player Tackles (incl. overtime)", "Player Tackles (incl. overtime)", "Player Tackles", "Tackles", "Tackle" };
                        }
                        return new List<string> { clean };
                    }
                    if (!clean.Contains("Score", StringComparison.OrdinalIgnoreCase) && !clean.Contains("Goal", StringComparison.OrdinalIgnoreCase) &&
                       (clean.Contains("To Be Booked", StringComparison.OrdinalIgnoreCase) ||
                        clean.Contains("To Be Carded", StringComparison.OrdinalIgnoreCase) ||
                        clean.Contains("To Receive a Card", StringComparison.OrdinalIgnoreCase) ||
                        clean.Contains("To Get a Card", StringComparison.OrdinalIgnoreCase) ||
                        clean.Contains("Shown a Card", StringComparison.OrdinalIgnoreCase) ||
                        clean.Contains("get Booked", StringComparison.OrdinalIgnoreCase) ||
                        (clean.Contains("Player", StringComparison.OrdinalIgnoreCase) && (clean.Contains("Card", StringComparison.OrdinalIgnoreCase) || clean.Contains("Book", StringComparison.OrdinalIgnoreCase)))))
                    {
                        return new List<string> { "Player To Be Carded (incl. overtime)", "Player To Be Carded", "Player Cards", "To Be Carded", "To Be Booked", "Cards", "Card" };
                    }
                }
            }
            // -----------------------------------------------------------------------------------------

            // Quarter-level markets (First Quarter .. Fourth Quarter)
            if (!string.IsNullOrEmpty(quarterOrdinal))
            {
                bool qIncludesOt = clean.Contains("incl. overtime", StringComparison.OrdinalIgnoreCase) ||
                                   clean.Contains("including overtime", StringComparison.OrdinalIgnoreCase) ||
                                   clean.Contains("incl. ot", StringComparison.OrdinalIgnoreCase) ||
                                   clean.Contains("including ot", StringComparison.OrdinalIgnoreCase);

                if (clean.Contains("Handicap", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("Spread", StringComparison.OrdinalIgnoreCase))
                {
                    return qIncludesOt
                        ? new List<string> { $"Handicap {quarterOrdinal} (incl. overtime)", $"Handicap {quarterOrdinal}" }
                        : new List<string> { $"Handicap {quarterOrdinal}", $"Handicap {quarterOrdinal} (incl. overtime)" };
                }

                if (clean.Contains("Total", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("Over", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("Under", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("Points", StringComparison.OrdinalIgnoreCase))
                {
                    if (clean.Contains("Team 1", StringComparison.OrdinalIgnoreCase) || clean.Contains("Home", StringComparison.OrdinalIgnoreCase))
                        return qIncludesOt
                            ? new List<string> { $"Over Under Team 1 {quarterOrdinal} (incl. overtime)", $"Over Under Team 1 {quarterOrdinal}" }
                            : new List<string> { $"Over Under Team 1 {quarterOrdinal}", $"Over Under Team 1 {quarterOrdinal} (incl. overtime)" };
                    if (clean.Contains("Team 2", StringComparison.OrdinalIgnoreCase) || clean.Contains("Away", StringComparison.OrdinalIgnoreCase))
                        return qIncludesOt
                            ? new List<string> { $"Over Under Team 2 {quarterOrdinal} (incl. overtime)", $"Over Under Team 2 {quarterOrdinal}" }
                            : new List<string> { $"Over Under Team 2 {quarterOrdinal}", $"Over Under Team 2 {quarterOrdinal} (incl. overtime)" };

                    return qIncludesOt
                        ? new List<string> { $"Over Under {quarterOrdinal} (incl. overtime)", $"Over Under {quarterOrdinal}" }
                        : new List<string> { $"Over Under {quarterOrdinal}", $"Over Under {quarterOrdinal} (incl. overtime)" };
                }

                bool wantsQuarter3Way = clean.Contains("1X2", StringComparison.OrdinalIgnoreCase) ||
                                        clean.Contains("3-Way", StringComparison.OrdinalIgnoreCase) ||
                                        clean.Contains("3 Way", StringComparison.OrdinalIgnoreCase) ||
                                        clean.Contains("Result", StringComparison.OrdinalIgnoreCase) ||
                                        (!string.IsNullOrEmpty(selectionName) && System.Text.RegularExpressions.Regex.IsMatch(selectionName.Trim(), @"^(?:draw|tie|x|uavgjort|oavgjort)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase));

                if (wantsQuarter3Way)
                {
                    return new List<string> { $"{quarterOrdinal} Result", $"{quarterOrdinal} Winner" };
                }

                if (clean.Contains("Winner", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("Moneyline", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("Money Line", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("To Win", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("Draw No Bet", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("DNB", StringComparison.OrdinalIgnoreCase) ||
                    clean.Equals(quarterOrdinal, StringComparison.OrdinalIgnoreCase))
                {
                    return new List<string> { $"{quarterOrdinal} Winner", $"{quarterOrdinal} Result" };
                }
            }

            // Half-level Winner / Moneyline / Result markets
            if (!string.IsNullOrEmpty(halfSuffix))
            {
                string halfName = halfSuffix.Trim(); // "First Half" or "Second Half"
                if (clean.Contains("Moneyline", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("Money Line", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("Winner", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("To Win", StringComparison.OrdinalIgnoreCase))
                {
                    return new List<string> { $"{halfName} Winner", $"Draw No Bet {halfName}", $"{halfName} Result" };
                }
                if (clean.Contains("1X2", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("3-Way", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("3 Way", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("Result", StringComparison.OrdinalIgnoreCase))
                {
                    return new List<string> { $"{halfName} Result", $"{halfName} Winner" };
                }
            }

            // Strip off any trailing scores (like "0 - 1") for 3-Way Handicaps
            if (clean.StartsWith("Handicap (3 Way)", StringComparison.OrdinalIgnoreCase) || 
                clean.StartsWith("3-Way Handicap", StringComparison.OrdinalIgnoreCase) ||
                clean.Contains("3 Way Handicap", StringComparison.OrdinalIgnoreCase) ||
                clean.Contains("3-Way Handicap", StringComparison.OrdinalIgnoreCase) ||
                clean.Contains("Handicap (3 Way)", StringComparison.OrdinalIgnoreCase) ||
                clean.Contains("Handicap 3 Way", StringComparison.OrdinalIgnoreCase) ||
                clean.Contains("European Handicap", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(halfSuffix)) return new List<string> { "3-Way Handicap" + halfSuffix };
                return new List<string> { "3-Way Handicap" };
            }

            // Strip off any trailing scores (like "-1.0" or "(0-1)" or "0 - 1") for Asian / Goals Handicaps
            if (clean.Contains("Set Handicap", StringComparison.OrdinalIgnoreCase) ||
                clean.Contains("Sets Handicap", StringComparison.OrdinalIgnoreCase) ||
                clean.Contains("Handicap Sets", StringComparison.OrdinalIgnoreCase) ||
                clean.Contains("Handicap (Sets)", StringComparison.OrdinalIgnoreCase) ||
                clean.Contains("Asian Handicap (Sets)", StringComparison.OrdinalIgnoreCase) ||
                clean.Contains("Asian Handicap Sets", StringComparison.OrdinalIgnoreCase))
            {
                return new List<string> { "Set Handicap" };
            }

            if (clean.Contains("Game Handicap", StringComparison.OrdinalIgnoreCase) ||
                clean.Contains("Games Handicap", StringComparison.OrdinalIgnoreCase) ||
                clean.Contains("Handicap Games", StringComparison.OrdinalIgnoreCase) ||
                clean.Contains("Handicap (Games)", StringComparison.OrdinalIgnoreCase) ||
                clean.Contains("Asian Handicap (Games)", StringComparison.OrdinalIgnoreCase) ||
                clean.Contains("Asian Handicap Games", StringComparison.OrdinalIgnoreCase))
            {
                return new List<string> { "Game Handicap" };
            }

            if (clean.Contains("Point Handicap", StringComparison.OrdinalIgnoreCase) ||
                clean.Contains("Points Handicap", StringComparison.OrdinalIgnoreCase))
            {
                return new List<string> { "Point Handicap", "Handicap (incl. overtime)", "Handicap" };
            }

            // Tennis match-level totals & specials (when matchName was not provided or for match-wide markets)
            if (string.IsNullOrEmpty(setOrdinal))
            {
                if (clean.Contains("Tie Break", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("Tie-Break", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("Tiebreak", StringComparison.OrdinalIgnoreCase))
                {
                    return new List<string> { "Total Tie Breaks" };
                }

                if (clean.Contains("Total Sets", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("Sets Over/Under", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("Over/Under Sets", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("Number of Sets", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("Exact Sets", StringComparison.OrdinalIgnoreCase))
                {
                    bool wantsExactSets = clean.Contains("Exact", StringComparison.OrdinalIgnoreCase) ||
                                          (!string.IsNullOrEmpty(selectionName) &&
                                           !System.Text.RegularExpressions.Regex.IsMatch(selectionName, @"\b(?:over|under)\b|\d+\.\d+", System.Text.RegularExpressions.RegexOptions.IgnoreCase) &&
                                           System.Text.RegularExpressions.Regex.IsMatch(selectionName.Trim(), @"^[2-5](?:\s+sets?)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
                    if (wantsExactSets)
                        return new List<string> { "Exact Sets", "Total Sets Over Under" };
                    return new List<string> { "Total Sets Over Under", "Exact Sets" };
                }

                if (!isSpecificSingleGame && !clean.Contains("Service", StringComparison.OrdinalIgnoreCase) &&
                    clean.Contains("Games", StringComparison.OrdinalIgnoreCase))
                {
                    if (clean.Contains("Odd", StringComparison.OrdinalIgnoreCase) && clean.Contains("Even", StringComparison.OrdinalIgnoreCase))
                        return new List<string> { "Odd Even Games" };
                    if (clean.Contains("Most", StringComparison.OrdinalIgnoreCase) || clean.Contains("More", StringComparison.OrdinalIgnoreCase))
                        return new List<string> { "Most Games" };
                    if (!clean.Contains("Winner", StringComparison.OrdinalIgnoreCase) && !clean.Contains("Result", StringComparison.OrdinalIgnoreCase))
                        return new List<string> { "Total Games Over Under", "Total Games" };
                }

                if (clean.Contains("Odd", StringComparison.OrdinalIgnoreCase) &&
                    clean.Contains("Even", StringComparison.OrdinalIgnoreCase) &&
                    clean.Contains("Set", StringComparison.OrdinalIgnoreCase))
                {
                    return new List<string> { "Odd Even Sets" };
                }

                if (clean.Contains("Both", StringComparison.OrdinalIgnoreCase) &&
                    clean.Contains("Set", StringComparison.OrdinalIgnoreCase) &&
                    (clean.Contains("Win", StringComparison.OrdinalIgnoreCase) || clean.Contains("Take", StringComparison.OrdinalIgnoreCase)))
                {
                    return new List<string> { "Both Participants To Win a Set" };
                }

                if (clean.Contains("Any Set To Nil", StringComparison.OrdinalIgnoreCase) ||
                    (clean.Contains("Set", StringComparison.OrdinalIgnoreCase) && clean.Contains("Nil", StringComparison.OrdinalIgnoreCase)))
                {
                    return new List<string> { "Any Set To Nil" };
                }
            }

            if (clean.Contains("Asian Handicap", StringComparison.OrdinalIgnoreCase) ||
                clean.Contains("Goals Handicap", StringComparison.OrdinalIgnoreCase) ||
                clean.Contains("Goal Handicap", StringComparison.OrdinalIgnoreCase) ||
                clean.Contains("Point Spread", StringComparison.OrdinalIgnoreCase) ||
                clean.Contains("Spread", StringComparison.OrdinalIgnoreCase) ||
                clean.StartsWith("Handicap", StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(halfSuffix) && clean.Contains("Handicap", StringComparison.OrdinalIgnoreCase)))
            {
                if (!string.IsNullOrEmpty(halfSuffix))
                {
                    return new List<string> { "Asian Handicap" + halfSuffix, "Asian Handicap -" + halfSuffix, "Handicap" + halfSuffix, "Handicap" + halfSuffix + " (incl. overtime)", "3-Way Handicap" + halfSuffix };
                }
                if (!string.IsNullOrEmpty(periodPrefix))
                {
                    return new List<string> { $"{periodPrefix}Handicap" };
                }
                if (clean.Contains("Spread", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("overtime", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("ot", StringComparison.OrdinalIgnoreCase))
                {
                    return new List<string> { "Handicap (incl. overtime)", "Handicap (incl. overtime and penalties)", "Asian Handicap", "Handicap", "3-Way Handicap", "Game Handicap", "Set Handicap" };
                }
                if (!clean.Contains("Goal", StringComparison.OrdinalIgnoreCase))
                {
                    return new List<string> { "Asian Handicap", "Handicap (incl. overtime)", "Handicap (incl. overtime and penalties)", "Handicap", "3-Way Handicap", "Game Handicap", "Set Handicap" };
                }
                return new List<string> { "Asian Handicap", "3-Way Handicap" };
            }

            if (clean.StartsWith("1st Half Goals", StringComparison.OrdinalIgnoreCase) || 
                clean.StartsWith("First Half Goals", StringComparison.OrdinalIgnoreCase) ||
                clean.StartsWith("Half Time Goals", StringComparison.OrdinalIgnoreCase))
            {
                return new List<string> { "Over Under First Half" };
            }

            if (clean.StartsWith("2nd Half Goals", StringComparison.OrdinalIgnoreCase) || 
                clean.StartsWith("Second Half Goals", StringComparison.OrdinalIgnoreCase))
            {
                return new List<string> { "Over Under Second Half" };
            }

            // Route Asian Total variants to Over Under
            if (clean.StartsWith("Asian Total", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(halfSuffix)) return new List<string> { "Over Under" + halfSuffix, "Over Under" + halfSuffix + " (incl. overtime)" };
                return new List<string> { "Over Under Full Time", "Over Under (incl. overtime)" };
            }

            // Match-level Total Points / Half Total Points / Over Under (incl. overtime) when matchName was null
            if (!string.IsNullOrEmpty(halfSuffix) &&
                (clean.Contains("Points", StringComparison.OrdinalIgnoreCase) ||
                 clean.Contains("Total", StringComparison.OrdinalIgnoreCase) ||
                 clean.Contains("Over", StringComparison.OrdinalIgnoreCase) ||
                 clean.Contains("Under", StringComparison.OrdinalIgnoreCase)))
            {
                return new List<string> { "Over Under" + halfSuffix, "Over Under" + halfSuffix + " (incl. overtime)" };
            }

            if (!hasSubPeriodModifier &&
                (clean.Equals("Total Points", StringComparison.OrdinalIgnoreCase) ||
                 clean.Equals("Total Points (incl. overtime)", StringComparison.OrdinalIgnoreCase) ||
                 clean.Equals("Points Over/Under", StringComparison.OrdinalIgnoreCase) ||
                 clean.Equals("Points Over Under", StringComparison.OrdinalIgnoreCase) ||
                 clean.Equals("Over/Under Points", StringComparison.OrdinalIgnoreCase) ||
                 clean.Equals("Over Under Points", StringComparison.OrdinalIgnoreCase) ||
                 clean.Equals("Over/Under (incl. overtime)", StringComparison.OrdinalIgnoreCase) ||
                 clean.Equals("Over Under (incl. overtime)", StringComparison.OrdinalIgnoreCase) ||
                 clean.Equals("Total (incl. overtime)", StringComparison.OrdinalIgnoreCase) ||
                 clean.Equals("Match Total", StringComparison.OrdinalIgnoreCase) ||
                 clean.Equals("Game Total", StringComparison.OrdinalIgnoreCase) ||
                 (clean.Contains("Total Points", StringComparison.OrdinalIgnoreCase) && !clean.Contains("Team", StringComparison.OrdinalIgnoreCase))))
            {
                return new List<string> { "Over Under (incl. overtime)", "Total (incl. overtime and penalties)", "Over Under Full Time", "Total", "Total Goals" };
            }

            // 2. Otherwise return what the AI gave us
            return new List<string> { clean };
        }

        private static string TryResolveTennisSetOrdinal(string marketName)
        {
            if (string.IsNullOrWhiteSpace(marketName)) return "";
            if (System.Text.RegularExpressions.Regex.IsMatch(marketName, @"\b(?:1st|first|1\.)\s+set\b|\bset\s*1\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return "First Set";
            if (System.Text.RegularExpressions.Regex.IsMatch(marketName, @"\b(?:2nd|second|2\.)\s+set\b|\bset\s*2\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return "Second Set";
            if (System.Text.RegularExpressions.Regex.IsMatch(marketName, @"\b(?:3rd|third|3\.)\s+set\b|\bset\s*3\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return "Third Set";
            if (System.Text.RegularExpressions.Regex.IsMatch(marketName, @"\b(?:4th|fourth|4\.)\s+set\b|\bset\s*4\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return "Fourth Set";
            if (System.Text.RegularExpressions.Regex.IsMatch(marketName, @"\b(?:5th|fifth|5\.)\s+set\b|\bset\s*5\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return "Fifth Set";
            return "";
        }

        private bool MatchesEitherTeam(string teamName, string? opTeamName, string marketName)
        {
            if (MatchesTeam(teamName, marketName)) return true;
            if (!string.IsNullOrWhiteSpace(opTeamName) && MatchesTeam(opTeamName, marketName)) return true;
            return false;
        }

        private bool MatchesTeam(string teamName, string marketName)
        {
            if (string.IsNullOrWhiteSpace(teamName) || string.IsNullOrWhiteSpace(marketName)) return false;
            if (marketName.Contains(teamName, StringComparison.OrdinalIgnoreCase)) return true;
            if (BettingApp.Services.TeamAliasMappingService.IsTeamMatch(teamName, marketName)) return true;
            
            string candidate = System.Text.RegularExpressions.Regex.Replace(marketName, @"\([+-]?\d+(?:\.\d+)?\)", " ").Trim();
            candidate = System.Text.RegularExpressions.Regex.Replace(candidate, @"[+-]?\d+(?:\.\d+)?(?![a-zA-Z])", " ").Trim();
            candidate = System.Text.RegularExpressions.Regex.Replace(candidate, @"\b(over|under|o\/u|total|goals?|corners?|cards?|bookings?|points?|rebounds?|steals?|blocks?|turnovers?|threes?|shots?|on|target|saves?|goalkeeper|goal\s*keeper|fouls?|committed|tackles?|offsides?|aces?|double\s+faults?|faults?|games?|sets?|breaks?|service|tie\s*breaks?|tiebreaks?|won|number|of|player|participant|1st|2nd|3rd|4th|5th|first|second|third|fourth|fifth|half|halves|quarter|quarters|qtr|spread|either|both|at|least|one|team|most|more|to|win|starts?|full|time|regular|incl|including|overtime|ot)\b", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            candidate = System.Text.RegularExpressions.Regex.Replace(candidate, @"[^\w\s]", " ").Trim();
            candidate = System.Text.RegularExpressions.Regex.Replace(candidate, @"\s+", " ").Trim();
            if (!string.IsNullOrWhiteSpace(candidate) && BettingApp.Services.TeamAliasMappingService.IsTeamMatch(teamName, candidate))
            {
                return true;
            }

            var words = teamName.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            var significantWords = System.Linq.Enumerable.Where(words, w => 
                w.Length >= 4 && 
                !w.Equals("City", StringComparison.OrdinalIgnoreCase) && 
                !w.Equals("United", StringComparison.OrdinalIgnoreCase) && 
                !w.Equals("Club", StringComparison.OrdinalIgnoreCase) && 
                !w.Equals("FC", StringComparison.OrdinalIgnoreCase));
                
            foreach (var w in significantWords)
            {
                if (marketName.Contains(w, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static bool IsPlayerCardMarket(string marketName, string? selectionName)
        {
            if (string.IsNullOrWhiteSpace(marketName)) return false;
            if (marketName.Contains("Player", StringComparison.OrdinalIgnoreCase) ||
                marketName.Contains("Player's", StringComparison.OrdinalIgnoreCase) ||
                marketName.Contains("To Be Booked", StringComparison.OrdinalIgnoreCase) ||
                marketName.Contains("To Be Carded", StringComparison.OrdinalIgnoreCase) ||
                marketName.Contains("To Receive a Card", StringComparison.OrdinalIgnoreCase) ||
                marketName.Contains("To Get a Card", StringComparison.OrdinalIgnoreCase) ||
                marketName.Contains("Shown a Card", StringComparison.OrdinalIgnoreCase) ||
                marketName.Contains("get Booked", StringComparison.OrdinalIgnoreCase) ||
                marketName.Contains("Score", StringComparison.OrdinalIgnoreCase) ||
                marketName.Contains("Goal", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (!string.IsNullOrWhiteSpace(selectionName) && HasPlayerName(selectionName))
            {
                return true;
            }
            return false;
        }

        private static bool HasPlayerName(string? text, string? matchName = null)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (text.Contains("Player", StringComparison.OrdinalIgnoreCase) || text.Contains("Player's", StringComparison.OrdinalIgnoreCase)) return true;

            string stripped = text;
            if (!string.IsNullOrWhiteSpace(matchName))
            {
                var split = TeamAliasMappingService.SplitMatchTeams(matchName);
                foreach (var teamPart in split)
                {
                    var t = teamPart.Trim();
                    var dm = System.Text.RegularExpressions.Regex.Match(t, @"\((?:Starts:\s*)?([^)]+)\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (dm.Success) t = t.Substring(0, dm.Index).Trim();
                    if (!string.IsNullOrWhiteSpace(t))
                    {
                        stripped = stripped.Replace(t, " ", StringComparison.OrdinalIgnoreCase);
                        foreach (var w in t.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (w.Length >= 3)
                            {
                                stripped = System.Text.RegularExpressions.Regex.Replace(
                                    stripped,
                                    $@"\b{System.Text.RegularExpressions.Regex.Escape(w)}\b",
                                    " ",
                                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            }
                        }
                    }
                }
            }

            stripped = System.Text.RegularExpressions.Regex.Replace(stripped, @"\b(over|under|yes|no|total|team|home|away|goalkeeper|goal|keeper|saves|save|fouls|foul|fouled|won|drawn|suffered|win|shots|shot|target|tackles|tackle|points|point|pts|rebounds|rebound|rebs|reb|steals|steal|stls|stl|blocks|block|blks|blk|blocked|turnovers|turnover|tovs|tov|threes|three|pointers|pointer|fg|field|goals|made|triple|cards|card|bookings|booking|corners|corner|offsides|offside|assists|assist|asts|ast|pra|stocks|committed|aces|ace|double|faults|fault|games|game|sets|set|breaks|break|service|tiebreaks|tiebreak|tie|1st|2nd|3rd|4th|5th|first|second|third|fourth|fifth|half|halves|quarter|quarters|qtr|q1|q2|q3|q4|1h|2h|full|time|regular|match|in|of|number|handicap|spread|most|more|odd|even|to|be|or|record|score|at|least|incl|including|overtime|ot)\b", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            stripped = System.Text.RegularExpressions.Regex.Replace(stripped, @"[0-9\.\+\-:\(\)\/]", "").Trim();
            return stripped.Length >= 3;
        }

        public static bool IsCombinationMarket(string? marketName, string? matchName = null)
        {
            if (string.IsNullOrWhiteSpace(marketName)) return false;

            var clean = marketName.Trim();

            // 1. Explicit Bet Builder / SGP keywords
            if (clean.Contains("Bet Builder", StringComparison.OrdinalIgnoreCase) ||
                clean.Contains("Same Game Parlay", StringComparison.OrdinalIgnoreCase) ||
                clean.Contains("SGP", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Strip out team names if matchName is provided so team names with & or "and" (e.g., Brighton & Hove Albion)
            // don't trigger false positives.
            string cleanWithoutTeams = clean;
            if (!string.IsNullOrWhiteSpace(matchName))
            {
                var split = TeamAliasMappingService.SplitMatchTeams(matchName);
                if (split.Length >= 2)
                {
                    string t1 = split[0].Trim();
                    string t2 = split[1].Trim();
                    var dm1 = System.Text.RegularExpressions.Regex.Match(t1, @"\((?:Starts:\s*)?([^)]+)\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (dm1.Success) t1 = t1.Substring(0, dm1.Index).Trim();
                    var dm2 = System.Text.RegularExpressions.Regex.Match(t2, @"\((?:Starts:\s*)?([^)]+)\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (dm2.Success) t2 = t2.Substring(0, dm2.Index).Trim();

                    cleanWithoutTeams = cleanWithoutTeams.Replace(t1, "", StringComparison.OrdinalIgnoreCase)
                                                         .Replace(t2, "", StringComparison.OrdinalIgnoreCase);

                    // For Tennis Doubles (where team names contain '/' or '&'), also strip partner pairs connected by '/', '&', or 'and'
                    cleanWithoutTeams = StripDoublesPairTokens(cleanWithoutTeams, t1);
                    cleanWithoutTeams = StripDoublesPairTokens(cleanWithoutTeams, t2);
                }
            }

            // Also strip well-known entity names that contain & or "and" if matchName wasn't supplied
            cleanWithoutTeams = cleanWithoutTeams.Replace("Brighton & Hove Albion", "", StringComparison.OrdinalIgnoreCase)
                                                 .Replace("Brighton and Hove Albion", "", StringComparison.OrdinalIgnoreCase)
                                                 .Replace("Dagenham & Redbridge", "", StringComparison.OrdinalIgnoreCase)
                                                 .Replace("Dagenham and Redbridge", "", StringComparison.OrdinalIgnoreCase)
                                                 .Replace("Trinidad & Tobago", "", StringComparison.OrdinalIgnoreCase)
                                                 .Replace("Trinidad and Tobago", "", StringComparison.OrdinalIgnoreCase)
                                                 .Replace("Bosnia & Herzegovina", "", StringComparison.OrdinalIgnoreCase)
                                                 .Replace("Bosnia and Herzegovina", "", StringComparison.OrdinalIgnoreCase)
                                                 .Replace("Antigua & Barbuda", "", StringComparison.OrdinalIgnoreCase);
            cleanWithoutTeams = System.Text.RegularExpressions.Regex.Replace(
                cleanWithoutTeams,
                @"\b(?:including\s+|incl\.?\s+)?(?:overtime|ot)\s*(?:and|&|\/)\s*(?:penalties|penalty|shootouts?|so)\b",
                "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            cleanWithoutTeams = System.Text.RegularExpressions.Regex.Replace(
                cleanWithoutTeams,
                @"\b(?:one|two|three|four|five|\d+)\s+and\s+more\b",
                "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // Strip valid single-market Basketball player stat combinations (e.g. "Player Points + Rebounds + Assists",
            // "Points, Rebounds & Assists", "Points and Rebounds", "Steals + Blocks") so they aren't rejected as SGP/Bet Builder combos.
            cleanWithoutTeams = System.Text.RegularExpressions.Regex.Replace(
                cleanWithoutTeams,
                @"\b(?:points?|pts|rebounds?|rebs?|assists?|asts?|steals?|stls?|blocks?|blks?)(?:\s*(?:\+|&|,|\/|\band\b)\s*(?:points?|pts|rebounds?|rebs?|assists?|asts?|steals?|stls?|blocks?|blks?))+\b",
                "PlayerComboStat",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // 2. Contains ampersand '&'
            if (cleanWithoutTeams.Contains('&'))
            {
                return true;
            }

            // 3. Contains isolated '+' (e.g. "Match Result + Total Goals", but NOT "1+" or "+1.5")
            if (System.Text.RegularExpressions.Regex.IsMatch(cleanWithoutTeams, @"\s+\+\s+"))
            {
                return true;
            }

            // 4. Contains isolated 'and' (e.g. "Both Teams to Score and Over/Under 2.5 Goals", "Match Result and Both Teams to Score")
            if (System.Text.RegularExpressions.Regex.IsMatch(cleanWithoutTeams, @"\b(?:and)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                return true;
            }

            // 5. Slash combinations (e.g. "Result / Both Teams to Score", "Match Result / Total Goals")
            // Exclude single standard markets: Over/Under, Half Time / Full Time, HT/FT, 1/X/2, etc.
            if (cleanWithoutTeams.Contains('/') && 
                (cleanWithoutTeams.Contains("Both Teams To Score", StringComparison.OrdinalIgnoreCase) || 
                 cleanWithoutTeams.Contains("BTTS", StringComparison.OrdinalIgnoreCase) || 
                 cleanWithoutTeams.Contains("Both to Score", StringComparison.OrdinalIgnoreCase) ||
                 cleanWithoutTeams.Contains("Total Goals", StringComparison.OrdinalIgnoreCase)) &&
                (cleanWithoutTeams.Contains("Result", StringComparison.OrdinalIgnoreCase) || 
                 cleanWithoutTeams.Contains("1X2", StringComparison.OrdinalIgnoreCase) || 
                 cleanWithoutTeams.Contains("Double Chance", StringComparison.OrdinalIgnoreCase) || 
                 cleanWithoutTeams.Contains("Winner", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            // 6. Both Teams To Score combined with another market without explicit connector
            // e.g. "Both Teams To Score Over 2.5 Goals"
            bool mentionsBtts = cleanWithoutTeams.Contains("Both Teams to Score", StringComparison.OrdinalIgnoreCase) ||
                                cleanWithoutTeams.Contains("Both to Score", StringComparison.OrdinalIgnoreCase) ||
                                System.Text.RegularExpressions.Regex.IsMatch(cleanWithoutTeams, @"\bBTTS\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (mentionsBtts)
            {
                bool mentionsOtherMarket = cleanWithoutTeams.Contains("Over", StringComparison.OrdinalIgnoreCase) ||
                                           cleanWithoutTeams.Contains("Under", StringComparison.OrdinalIgnoreCase) ||
                                           cleanWithoutTeams.Contains("Total Goals", StringComparison.OrdinalIgnoreCase) ||
                                           cleanWithoutTeams.Contains("Result", StringComparison.OrdinalIgnoreCase) ||
                                           cleanWithoutTeams.Contains("1X2", StringComparison.OrdinalIgnoreCase) ||
                                           cleanWithoutTeams.Contains("Double Chance", StringComparison.OrdinalIgnoreCase) ||
                                           cleanWithoutTeams.Contains("Winner", StringComparison.OrdinalIgnoreCase) ||
                                           cleanWithoutTeams.Contains("Corners", StringComparison.OrdinalIgnoreCase) ||
                                           cleanWithoutTeams.Contains("Cards", StringComparison.OrdinalIgnoreCase) ||
                                           cleanWithoutTeams.Contains("Bookings", StringComparison.OrdinalIgnoreCase) ||
                                           cleanWithoutTeams.Contains("Handicap", StringComparison.OrdinalIgnoreCase);

                if (mentionsOtherMarket)
                {
                    return true;
                }
            }

            // 7. Player combo markets combining goals/scoring and cards/bookings (e.g. "To Score or be Booked", "Score or be Carded", "To Score or Carded", "Score or Booked")
            if ((cleanWithoutTeams.Contains("Score", StringComparison.OrdinalIgnoreCase) || cleanWithoutTeams.Contains("Goal", StringComparison.OrdinalIgnoreCase)) &&
                (cleanWithoutTeams.Contains("Book", StringComparison.OrdinalIgnoreCase) || cleanWithoutTeams.Contains("Card", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return false;
        }

        private static string StripDoublesPairTokens(string text, string teamName)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(teamName)) return text;
            if (!teamName.Contains('/') && !teamName.Contains('&')) return text;

            var partners = teamName.Split(new[] { '/', '&' }, StringSplitOptions.RemoveEmptyEntries);
            if (partners.Length != 2) return text;

            var p1Tokens = System.Text.RegularExpressions.Regex.Split(partners[0], @"[^\w]+")
                .Where(t => t.Length >= 2)
                .ToList();
            var p2Tokens = System.Text.RegularExpressions.Regex.Split(partners[1], @"[^\w]+")
                .Where(t => t.Length >= 2)
                .ToList();

            foreach (var tok1 in p1Tokens)
            {
                foreach (var tok2 in p2Tokens)
                {
                    string pattern1 = $@"\b(?:[a-zA-Z]\.?\s+)?{System.Text.RegularExpressions.Regex.Escape(tok1)}(?:\s+[a-zA-Z]\.?)?\s*(?:\/|&|\band\b)\s*(?:[a-zA-Z]\.?\s+)?{System.Text.RegularExpressions.Regex.Escape(tok2)}(?:\s+[a-zA-Z]\.?)?\b";
                    string pattern2 = $@"\b(?:[a-zA-Z]\.?\s+)?{System.Text.RegularExpressions.Regex.Escape(tok2)}(?:\s+[a-zA-Z]\.?)?\s*(?:\/|&|\band\b)\s*(?:[a-zA-Z]\.?\s+)?{System.Text.RegularExpressions.Regex.Escape(tok1)}(?:\s+[a-zA-Z]\.?)?\b";
                    text = System.Text.RegularExpressions.Regex.Replace(text, pattern1, "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    text = System.Text.RegularExpressions.Regex.Replace(text, pattern2, "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                }
            }

            return text;
        }
    }
}
