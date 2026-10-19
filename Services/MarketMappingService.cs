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
            { "Moneyline (incl. overtime)", "Winner (incl. overtime and penalties)|Moneyline" },
            { "Moneyline (including overtime)", "Winner (incl. overtime and penalties)|Moneyline" },
            { "Moneyline", "Winner (incl. overtime and penalties)|Full Time Result" },
            { "Money Line", "Winner (incl. overtime and penalties)|Full Time Result" },
            { "Money Line (Incl. Overtime and Shootout)", "Winner (incl. overtime and penalties)|Moneyline" },
            { "Money Line (incl. overtime and penalties)", "Winner (incl. overtime and penalties)|Moneyline" },
            { "Money Line (incl. overtime)", "Winner (incl. overtime and penalties)|Moneyline" },
            { "Money Line (including overtime)", "Winner (incl. overtime and penalties)|Moneyline" },
            { "Match Winner (incl. overtime and penalties)", "Winner (incl. overtime and penalties)|Moneyline" },
            { "Match Winner (incl. overtime and shootout)", "Winner (incl. overtime and penalties)|Moneyline" },
            { "Match Winner (including overtime and shootout)", "Winner (incl. overtime and penalties)|Moneyline" },
            { "Match Winner (incl. overtime)", "Winner (incl. overtime and penalties)|Moneyline" },
            { "Winner (incl. overtime and penalties)", "Winner (incl. overtime and penalties)" },
            { "Winner (incl. overtime and shootout)", "Winner (incl. overtime and penalties)" },
            { "Winner (including overtime and shootout)", "Winner (incl. overtime and penalties)" },
            { "To Win (incl. overtime and shootout)", "Winner (incl. overtime and penalties)" },
            { "To Win (including overtime and shootout)", "Winner (incl. overtime and penalties)" },
            { "To Win (incl. overtime and penalties)", "Winner (incl. overtime and penalties)" },
            { "To Win (including overtime and penalties)", "Winner (incl. overtime and penalties)" },
            { "To Win (incl. overtime)", "Winner (incl. overtime and penalties)" },
            { "To Win (including overtime)", "Winner (incl. overtime and penalties)" },
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
            { "1. Half: Goals Handicap", "Half Time Handicap" },
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
            { "Full Time", "Full Time Result|Regular Time Result" },
            { "Match Odds", "Regular Time Result|Full Time Result" },
            { "Match Winner", "Regular Time Result|Winner (incl. overtime and penalties)|Full Time Result" },
            { "1x2", "Regular Time Result|Full Time Result" },
            { "Match Result", "Regular Time Result|Full Time Result" },
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
            { "Correct Score", "Correct Score Full Time" },
            { "Early Win", "2Up - Full Time Result" },
            { "Early Win (Anytime 2 Goal Lead)", "2Up - Full Time Result" },
            { "Early Payout", "2Up - Full Time Result" },
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
            { "To Score and Assist", "Over Under Player Goals or Assists (incl. overtime)|Player Goal or Assist|To Score or Assist|Goal or Assist" }
        };

        public List<string> NormalizeMarketName(string rawMarketName, string? matchName = null, string? selectionName = null)
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
            
            // Detect if the market explicitly specifies a half
            string halfSuffix = "";
            if (clean.Contains("1st Half", StringComparison.OrdinalIgnoreCase) || clean.Contains("First Half", StringComparison.OrdinalIgnoreCase) || clean.Contains("1. Half", StringComparison.OrdinalIgnoreCase))
                halfSuffix = " First Half";
            else if (clean.Contains("2nd Half", StringComparison.OrdinalIgnoreCase) || clean.Contains("Second Half", StringComparison.OrdinalIgnoreCase) || clean.Contains("2. Half", StringComparison.OrdinalIgnoreCase))
                halfSuffix = " Second Half";

            // Handle Team Specific Markets dynamically if matchName is provided
            if (!string.IsNullOrWhiteSpace(matchName))
            {
                var split = matchName.Split(new[] { " vs ", " v ", " - " }, StringSplitOptions.None);
                if (split.Length >= 2)
                {
                    string team1 = split[0].Trim();
                    string team2 = split[1].Trim();

                    var dm1 = System.Text.RegularExpressions.Regex.Match(team1, @"\((?:Starts:\s*)?([^)]+)\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (dm1.Success) team1 = team1.Substring(0, dm1.Index).Trim();

                    var dm2 = System.Text.RegularExpressions.Regex.Match(team2, @"\((?:Starts:\s*)?([^)]+)\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (dm2.Success) team2 = team2.Substring(0, dm2.Index).Trim();

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

                        if (MatchesTeam(team1, clean) || clean.Contains("Home", StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(selectionName) && MatchesTeam(team1, selectionName)))
                            return new List<string> { "Corners - Over Under Team 1" + halfSuffix };
                        if (MatchesTeam(team2, clean) || clean.Contains("Away", StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(selectionName) && MatchesTeam(team2, selectionName)))
                            return new List<string> { "Corners - Over Under Team 2" + halfSuffix };

                        // Otherwise, generic corners (checking for 'Total Corners', 'Corners', or 'Half Corners')
                        if (clean.Contains("Total Corners", StringComparison.OrdinalIgnoreCase) || clean.Contains("Corners", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!string.IsNullOrEmpty(halfSuffix)) return new List<string> { "Corners - Over/Under" + halfSuffix };
                            return new List<string> { "Corners - Over/Under Full Time" };
                        }
                    }

                    // Goals Team 1/2
                    if (clean.Contains("Total Goals", StringComparison.OrdinalIgnoreCase) || clean.Contains("Goals", StringComparison.OrdinalIgnoreCase))
                    {
                        if (MatchesTeam(team1, clean) || clean.Contains("Home", StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(selectionName) && MatchesTeam(team1, selectionName)))
                            return new List<string> { "Over Under Team 1" + halfSuffix };
                        if (MatchesTeam(team2, clean) || clean.Contains("Away", StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(selectionName) && MatchesTeam(team2, selectionName)))
                            return new List<string> { "Over Under Team 2" + halfSuffix };
                            
                        // Otherwise, generic total goals for the match
                        if (clean.Contains("Total Goals", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!string.IsNullOrEmpty(halfSuffix)) return new List<string> { "Over Under" + halfSuffix };
                            if (clean.Contains("incl. overtime", StringComparison.OrdinalIgnoreCase) || clean.Contains("including overtime", StringComparison.OrdinalIgnoreCase))
                            {
                                return new List<string> { "Total (incl. overtime and penalties)", "Total", "Total Goals", "Over Under Full Time" };
                            }
                            return new List<string> { "Total", "Total Goals", "Over Under Full Time" };
                        }
                    }

                    // Cards Team 1/2
                    if (clean.Contains("Total Cards", StringComparison.OrdinalIgnoreCase) || clean.Contains("Cards", StringComparison.OrdinalIgnoreCase) || clean.Contains("Bookings", StringComparison.OrdinalIgnoreCase))
                    {
                        if (clean.Contains("Most Cards", StringComparison.OrdinalIgnoreCase) || clean.Contains("Cards 1X2", StringComparison.OrdinalIgnoreCase) || clean.Contains("More Cards", StringComparison.OrdinalIgnoreCase) || clean.Contains("Most Bookings", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!string.IsNullOrEmpty(halfSuffix)) return new List<string> { "Bookings - 1X2" + halfSuffix };
                            return new List<string> { "Bookings - 1X2" };
                        }

                        if (MatchesTeam(team1, clean) || clean.Contains("Home", StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(selectionName) && MatchesTeam(team1, selectionName)))
                            return new List<string> { $"Bookings - Over/Under [{team1}]" + halfSuffix, "Bookings - Over Under Team 1" + halfSuffix, $"Bookings - Over Under [{team1}]" + halfSuffix };
                        if (MatchesTeam(team2, clean) || clean.Contains("Away", StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(selectionName) && MatchesTeam(team2, selectionName)))
                            return new List<string> { $"Bookings - Over/Under [{team2}]" + halfSuffix, "Bookings - Over Under Team 2" + halfSuffix, $"Bookings - Over Under [{team2}]" + halfSuffix };
                    }

                    // To Win At Least One Half
                    if (clean.Contains("To Win At Least One Half", StringComparison.OrdinalIgnoreCase) || clean.Contains("To Win Either Half", StringComparison.OrdinalIgnoreCase))
                    {
                        if (MatchesTeam(team1, clean) || clean.Contains("Home", StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(selectionName) && MatchesTeam(team1, selectionName)))
                            return new List<string> { "Team 1 To Win Either Halves" };
                        if (MatchesTeam(team2, clean) || clean.Contains("Away", StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(selectionName) && MatchesTeam(team2, selectionName)))
                            return new List<string> { "Team 2 To Win Either Halves" };
                    }

                    // To Win Both Halves
                    if (clean.Contains("To Win Both Halves", StringComparison.OrdinalIgnoreCase))
                    {
                        if (MatchesTeam(team1, clean) || clean.Contains("Home", StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(selectionName) && MatchesTeam(team1, selectionName)))
                            return new List<string> { "Team 1 to win both halves" };
                        if (MatchesTeam(team2, clean) || clean.Contains("Away", StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(selectionName) && MatchesTeam(team2, selectionName)))
                            return new List<string> { "Team 2 to win both halves" };
                    }

                    // Goalkeeper Saves Team 1/2 or Match Total
                    if (clean.Contains("Goalkeeper Saves", StringComparison.OrdinalIgnoreCase) || clean.Contains("Goal Keeper Saves", StringComparison.OrdinalIgnoreCase) || clean.Contains("Saves", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!clean.Contains("Player", StringComparison.OrdinalIgnoreCase))
                        {
                            if (MatchesTeam(team1, clean) || clean.Contains("Home", StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(selectionName) && MatchesTeam(team1, selectionName)))
                                return new List<string> { $"Goalkeeper Saves - Over Under [{team1}]" + halfSuffix, "Goalkeeper Saves - Over Under Team 1" + halfSuffix, clean };
                            if (MatchesTeam(team2, clean) || clean.Contains("Away", StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(selectionName) && MatchesTeam(team2, selectionName)))
                                return new List<string> { $"Goalkeeper Saves - Over Under [{team2}]" + halfSuffix, "Goalkeeper Saves - Over Under Team 2" + halfSuffix, clean };

                            if (clean.StartsWith("Total ", StringComparison.OrdinalIgnoreCase) || clean.Contains("Total", StringComparison.OrdinalIgnoreCase))
                                return new List<string> { "Goalkeeper Saves - Over/Under Full Time", clean };
                        }
                    }

                    // Fouls Team 1/2 or Match Total
                    if (clean.Contains("Fouls", StringComparison.OrdinalIgnoreCase) || clean.Contains("Foul", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!clean.Contains("Player", StringComparison.OrdinalIgnoreCase))
                        {
                            if (MatchesTeam(team1, clean) || clean.Contains("Home", StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(selectionName) && MatchesTeam(team1, selectionName)))
                                return new List<string> { $"Fouls - Over Under [{team1}]" + halfSuffix, "Fouls - Over Under Team 1" + halfSuffix, clean };
                            if (MatchesTeam(team2, clean) || clean.Contains("Away", StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(selectionName) && MatchesTeam(team2, selectionName)))
                                return new List<string> { $"Fouls - Over Under [{team2}]" + halfSuffix, "Fouls - Over Under Team 2" + halfSuffix, clean };

                            if (clean.StartsWith("Total ", StringComparison.OrdinalIgnoreCase) || clean.Contains("Total", StringComparison.OrdinalIgnoreCase))
                                return new List<string> { "Fouls - Over/Under Full Time", clean };
                        }
                    }

                    // Shots / Shots on Target Team 1/2 or Match Total
                    if (clean.Contains("Shots", StringComparison.OrdinalIgnoreCase) || clean.Contains("Shot", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!clean.Contains("Player", StringComparison.OrdinalIgnoreCase))
                        {
                            bool isOnTarget = clean.Contains("Target", StringComparison.OrdinalIgnoreCase) || clean.Contains("Goal", StringComparison.OrdinalIgnoreCase);
                            string shotPrefix = isOnTarget ? "Shots On Goal" : "Shots";
                            if (MatchesTeam(team1, clean) || clean.Contains("Home", StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(selectionName) && MatchesTeam(team1, selectionName)))
                                return new List<string> { $"{shotPrefix} - Over Under [{team1}]" + halfSuffix, $"{shotPrefix} - Over Under Team 1" + halfSuffix, clean };
                            if (MatchesTeam(team2, clean) || clean.Contains("Away", StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(selectionName) && MatchesTeam(team2, selectionName)))
                                return new List<string> { $"{shotPrefix} - Over Under [{team2}]" + halfSuffix, $"{shotPrefix} - Over Under Team 2" + halfSuffix, clean };

                            if (clean.StartsWith("Total ", StringComparison.OrdinalIgnoreCase) || clean.Contains("Total", StringComparison.OrdinalIgnoreCase))
                                return new List<string> { $"{shotPrefix} - Over/Under Full Time", clean };
                        }
                    }

                    // Tackles Team 1/2 or Match Total
                    if (clean.Contains("Tackles", StringComparison.OrdinalIgnoreCase) || clean.Contains("Tackle", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!clean.Contains("Player", StringComparison.OrdinalIgnoreCase))
                        {
                            if (MatchesTeam(team1, clean) || clean.Contains("Home", StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(selectionName) && MatchesTeam(team1, selectionName)))
                                return new List<string> { $"Tackles - Over Under [{team1}]" + halfSuffix, "Tackles - Over Under Team 1" + halfSuffix, clean };
                            if (MatchesTeam(team2, clean) || clean.Contains("Away", StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(selectionName) && MatchesTeam(team2, selectionName)))
                                return new List<string> { $"Tackles - Over Under [{team2}]" + halfSuffix, "Tackles - Over Under Team 2" + halfSuffix, clean };

                            if (clean.StartsWith("Total ", StringComparison.OrdinalIgnoreCase) || clean.Contains("Total", StringComparison.OrdinalIgnoreCase))
                                return new List<string> { "Tackles - Over/Under Full Time", clean };
                        }
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

            if ((cleanBase.Equals("Total Points", StringComparison.OrdinalIgnoreCase) || clean.StartsWith("Total Points", StringComparison.OrdinalIgnoreCase)) && 
                !string.IsNullOrEmpty(selectionName) && HasPlayerName(selectionName))
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
                if (clean.Contains("Handicap", StringComparison.OrdinalIgnoreCase))
                {
                    return new List<string> { "Handicap", "Asian Handicap" };
                }
                if (clean.Contains("Total", StringComparison.OrdinalIgnoreCase) || clean.Contains("Goals", StringComparison.OrdinalIgnoreCase) || clean.Contains("Over", StringComparison.OrdinalIgnoreCase) || clean.Contains("Under", StringComparison.OrdinalIgnoreCase))
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
            
            if (clean.Contains("Moneyline", StringComparison.OrdinalIgnoreCase) || 
                clean.Contains("Money Line", StringComparison.OrdinalIgnoreCase) ||
                clean.Contains("Winner", StringComparison.OrdinalIgnoreCase))
            {
                if (clean.Contains("overtime", StringComparison.OrdinalIgnoreCase) || 
                    clean.Contains("shootout", StringComparison.OrdinalIgnoreCase) || 
                    clean.Contains("penalties", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("ot", StringComparison.OrdinalIgnoreCase))
                {
                    return new List<string> { "Winner (incl. overtime and penalties)", "Moneyline" };
                }
            }
            
            if (clean.Contains("Corners", StringComparison.OrdinalIgnoreCase) && clean.Contains("Handicap", StringComparison.OrdinalIgnoreCase))
            {
                return new List<string> { "Corners - Handicap" + halfSuffix };
            }

            if (clean.Contains("Most Corners", StringComparison.OrdinalIgnoreCase) || clean.Contains("Corners 1X2", StringComparison.OrdinalIgnoreCase) || clean.Contains("More Corners", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(halfSuffix)) return new List<string> { "Corners - 1X2" + halfSuffix };
                return new List<string> { "Corners - 1X2" };
            }

            if (clean.Contains("Most Cards", StringComparison.OrdinalIgnoreCase) || clean.Contains("Cards 1X2", StringComparison.OrdinalIgnoreCase) || clean.Contains("More Cards", StringComparison.OrdinalIgnoreCase) || clean.Contains("Most Bookings", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(halfSuffix)) return new List<string> { "Bookings - 1X2" + halfSuffix };
                return new List<string> { "Bookings - 1X2" };
            }

            if (clean.StartsWith("Total Cards", StringComparison.OrdinalIgnoreCase) || clean.StartsWith("Cards Over/Under", StringComparison.OrdinalIgnoreCase) || clean.Contains("Number of Cards", StringComparison.OrdinalIgnoreCase))
            {
                if (halfSuffix == " First Half") return new List<string> { "Bookings - Over Under First Half" };
                return new List<string> { "Bookings - Over Under Full Time" };
            }

            if (!IsCombinationMarket(clean, matchName) && 
                (clean.Contains("Both to Score", StringComparison.OrdinalIgnoreCase) || 
                 clean.Contains("Both Teams to Score", StringComparison.OrdinalIgnoreCase) || 
                 clean.Equals("BTTS", StringComparison.OrdinalIgnoreCase)))
            {
                if (halfSuffix == " First Half") return new List<string> { "Both Teams To Score - First Half", "Both Teams To Score First Half" };
                if (halfSuffix == " Second Half") return new List<string> { "Both Teams To Score - Second Half", "Both Teams To Score Second Half" };
                return new List<string> { "Both Teams To Score" };
            }

            if (clean.StartsWith("Total Goalkeeper Saves", StringComparison.OrdinalIgnoreCase) ||
                clean.StartsWith("Total Goal Keeper Saves", StringComparison.OrdinalIgnoreCase) ||
                clean.StartsWith("Total Fouls", StringComparison.OrdinalIgnoreCase) ||
                clean.StartsWith("Total Shots", StringComparison.OrdinalIgnoreCase) ||
                clean.StartsWith("Total Tackles", StringComparison.OrdinalIgnoreCase))
            {
                return new List<string> { clean };
            }

            // --- Dynamic Player Prop Catch-Alls (handles when AI appends player names to the market) ---
            bool isExplicitPlayerProp = clean.Contains("Player", StringComparison.OrdinalIgnoreCase) ||
                                        clean.Contains("Player's", StringComparison.OrdinalIgnoreCase);

            bool isTeamOrTotalStat = !isExplicitPlayerProp && (
                                     clean.Contains("Team 1", StringComparison.OrdinalIgnoreCase) ||
                                     clean.Contains("Team 2", StringComparison.OrdinalIgnoreCase) ||
                                     ((clean.Contains("Team", StringComparison.OrdinalIgnoreCase) ||
                                       clean.Contains("Home", StringComparison.OrdinalIgnoreCase) ||
                                       clean.Contains("Away", StringComparison.OrdinalIgnoreCase) ||
                                       clean.Contains("Total", StringComparison.OrdinalIgnoreCase)) && 
                                      !HasPlayerName(selectionName) && !HasPlayerName(clean)));

            // Combined Goal or Assist / Score or Assist (distinct from pure goals or pure assists)
            // Note: OddsPapi does not provide odds for combined Goal OR Assist props (it only provides pure Goals or pure Assists).
            // Do NOT fall back to pure Goals or pure Assists when the bet is for a combined Goal or Assist.
            bool isGoalOrAssist = ((clean.Contains("Score", StringComparison.OrdinalIgnoreCase) || clean.Contains("Goal", StringComparison.OrdinalIgnoreCase)) &&
                                  clean.Contains("Assist", StringComparison.OrdinalIgnoreCase)) ||
                                  (!string.IsNullOrEmpty(selectionName) &&
                                  (selectionName.Contains("Score", StringComparison.OrdinalIgnoreCase) || selectionName.Contains("Goal", StringComparison.OrdinalIgnoreCase)) &&
                                  selectionName.Contains("Assist", StringComparison.OrdinalIgnoreCase));

            if (isGoalOrAssist)
            {
                return new List<string> { "Over Under Player Goals or Assists (incl. overtime)", "Player Goal or Assist", "To Score or Assist", "Goal or Assist" };
            }

            if (!isTeamOrTotalStat && (clean.Contains("Points", StringComparison.OrdinalIgnoreCase) || clean.Contains("Point", StringComparison.OrdinalIgnoreCase)))
            {
                if (isExplicitPlayerProp || HasPlayerName(clean) || HasPlayerName(selectionName) || 
                    clean.Contains("to record", StringComparison.OrdinalIgnoreCase) || 
                    clean.Contains("to score", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("at least", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("over under", StringComparison.OrdinalIgnoreCase))
                {
                    if (clean.Contains("Power Play", StringComparison.OrdinalIgnoreCase) || clean.Contains("Powerplay", StringComparison.OrdinalIgnoreCase) || System.Text.RegularExpressions.Regex.IsMatch(clean, @"\bPPP\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    {
                        return new List<string> { "Over Under Player Power Play Points (incl. overtime)", "Player Power Play Points (incl. overtime)" };
                    }
                    return new List<string> { "Over Under Player Points (incl. overtime)", "Player Points (incl. overtime)", "Player Points" };
                }
            }

            if ((clean.Contains("Score", StringComparison.OrdinalIgnoreCase) || clean.Contains("Goal", StringComparison.OrdinalIgnoreCase)) &&
                (clean.Contains("Book", StringComparison.OrdinalIgnoreCase) || clean.Contains("Card", StringComparison.OrdinalIgnoreCase)))
            {
                return new List<string> { "Player to Score or be Booked", "Player to Score or Carded", "To Score or be Booked", "Score or Booked" };
            }

            if (!clean.Contains("Assist", StringComparison.OrdinalIgnoreCase) &&
                !clean.Contains("Point", StringComparison.OrdinalIgnoreCase) &&
                !clean.Contains("Book", StringComparison.OrdinalIgnoreCase) &&
                !clean.Contains("Card", StringComparison.OrdinalIgnoreCase) &&
               (clean.Contains("Goalscorer", StringComparison.OrdinalIgnoreCase) || clean.Contains("Goal Scorer", StringComparison.OrdinalIgnoreCase) || 
               (clean.Contains("To Score", StringComparison.OrdinalIgnoreCase) && !clean.Contains("Team", StringComparison.OrdinalIgnoreCase)) ||
               clean.Contains("Will/Will not Score", StringComparison.OrdinalIgnoreCase)))
            {
                return new List<string> { "Over Under Player Goals (incl. overtime)", "Anytime Goal Scorer", "Player Goals (incl. overtime)", "Player Goals", "Goals", "Goal" };
            }
            if (!isTeamOrTotalStat && (clean.Contains("Shots on Target", StringComparison.OrdinalIgnoreCase) || clean.Contains("Shots On Goal", StringComparison.OrdinalIgnoreCase)))
            {
                if (isExplicitPlayerProp || HasPlayerName(clean) || HasPlayerName(selectionName))
                {
                    return new List<string> { "Over Under Player Shots On Goal (incl. overtime)", "Player Shots On Goal (incl. overtime)", "Player's shot on target", "Player Shots on Target", "Player Shots on Target (incl. overtime)", "Shots on Target", "Shots On Goal" };
                }
                return new List<string> { clean };
            }
            if (!isTeamOrTotalStat && (clean.Contains("Goalkeeper Saves", StringComparison.OrdinalIgnoreCase) || clean.Contains("Goal Keeper Saves", StringComparison.OrdinalIgnoreCase)))
            {
                if (isExplicitPlayerProp || HasPlayerName(clean) || HasPlayerName(selectionName))
                {
                    return new List<string> { "Over Under Player Goalkeeper Saves (incl. overtime)", "Player Goalkeeper Saves (incl. overtime)", "Player Goalkeeper Saves", "Goalkeeper Saves", "Goal Keeper Saves", "Saves" };
                }
                return new List<string> { clean };
            }
            if (!isTeamOrTotalStat && clean.Contains("Shots", StringComparison.OrdinalIgnoreCase))
            {
                if (isExplicitPlayerProp || HasPlayerName(clean) || HasPlayerName(selectionName))
                {
                    return new List<string> { "Over Under Player Shots (incl. overtime)", "Player Shots (incl. overtime)", "Player Shots", "Shots", "Shot" };
                }
                return new List<string> { clean };
            }
            if (!isTeamOrTotalStat && clean.Contains("Fouls", StringComparison.OrdinalIgnoreCase))
            {
                if (isExplicitPlayerProp || HasPlayerName(clean) || HasPlayerName(selectionName))
                {
                    return new List<string> { "Over Under Player Fouls Committed (incl. overtime)", "Player Fouls Committed (incl. overtime)", "Player Fouls Committed", "Fouls Committed", "Fouls", "Foul" };
                }
                return new List<string> { clean };
            }
            if (!clean.Contains("Score", StringComparison.OrdinalIgnoreCase) && !clean.Contains("Goal", StringComparison.OrdinalIgnoreCase) &&
               (clean.Contains("Assists", StringComparison.OrdinalIgnoreCase) || clean.Contains("Assist", StringComparison.OrdinalIgnoreCase)))
            {
                return new List<string> { "Over Under Player Assists (incl. overtime)", "Player Assists (incl. overtime)", "Player Assists", "To Provide An Assist", "Anytime Assist", "Assists", "Assist" };
            }
            if (!isTeamOrTotalStat && (clean.Contains("Tackles", StringComparison.OrdinalIgnoreCase) || clean.Contains("Tackle", StringComparison.OrdinalIgnoreCase)))
            {
                if (isExplicitPlayerProp || HasPlayerName(clean) || HasPlayerName(selectionName))
                {
                    return new List<string> { "Over Under Player Tackles (incl. overtime)", "Player Tackles (incl. overtime)", "Player Tackles", "Tackles", "Tackle" };
                }
                return new List<string> { clean };
            }
            if (!clean.Contains("Score", StringComparison.OrdinalIgnoreCase) && !clean.Contains("Goal", StringComparison.OrdinalIgnoreCase) &&
               (clean.Contains("To Be Booked", StringComparison.OrdinalIgnoreCase) || (clean.Contains("Player", StringComparison.OrdinalIgnoreCase) && clean.Contains("Card", StringComparison.OrdinalIgnoreCase))))
            {
                return new List<string> { "Player To Be Carded (incl. overtime)", "Player To Be Carded", "Player Cards", "To Be Carded", "To Be Booked", "Cards", "Card" };
            }
            // -----------------------------------------------------------------------------------------

            if (clean.StartsWith("Early Win", StringComparison.OrdinalIgnoreCase) || 
                clean.StartsWith("Early Payout", StringComparison.OrdinalIgnoreCase) ||
                clean.StartsWith("2Up", StringComparison.OrdinalIgnoreCase))
            {
                return new List<string> { "2Up - Full Time Result" };
            }

            if (clean.StartsWith("1st Half Goals", StringComparison.OrdinalIgnoreCase) || 
                clean.StartsWith("First Half Goals", StringComparison.OrdinalIgnoreCase) ||
                clean.StartsWith("Half Time Goals", StringComparison.OrdinalIgnoreCase))
            {
                return new List<string> { "Over Under First Half" };
            }

            // Strip off any trailing scores (like "0 - 1") for 3-Way Handicaps
            if (clean.StartsWith("Handicap (3 Way)", StringComparison.OrdinalIgnoreCase) || 
                clean.StartsWith("3-Way Handicap", StringComparison.OrdinalIgnoreCase) ||
                clean.Contains("3 Way Handicap", StringComparison.OrdinalIgnoreCase) ||
                clean.Contains("Handicap 3 Way", StringComparison.OrdinalIgnoreCase))
            {
                return new List<string> { "3-Way Handicap" };
            }

            // Strip off any trailing scores (like "-1.0" or "(0-1)") for Asian Handicaps
            if (clean.StartsWith("Asian Handicap", StringComparison.OrdinalIgnoreCase) ||
                clean.StartsWith("Goals Handicap", StringComparison.OrdinalIgnoreCase) ||
                clean.StartsWith("Handicap", StringComparison.OrdinalIgnoreCase))
            {
                if (halfSuffix == " First Half") return new List<string> { "Asian Handicap First Half", "Asian Handicap - First Half" };
                return new List<string> { "Asian Handicap", "3-Way Handicap" };
            }

            // Route Asian Total variants to Over Under
            if (clean.StartsWith("Asian Total", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(halfSuffix)) return new List<string> { "Over Under" + halfSuffix };
                return new List<string> { "Over Under Full Time" };
            }

            // 2. Otherwise return what the AI gave us
            return new List<string> { clean };
        }

        private bool MatchesTeam(string teamName, string marketName)
        {
            if (string.IsNullOrWhiteSpace(teamName) || string.IsNullOrWhiteSpace(marketName)) return false;
            if (marketName.Contains(teamName, StringComparison.OrdinalIgnoreCase)) return true;
            if (BettingApp.Services.TeamAliasMappingService.IsTeamMatch(teamName, marketName)) return true;
            
            string candidate = System.Text.RegularExpressions.Regex.Replace(marketName, @"\([+-]?\d+(?:\.\d+)?\)", " ").Trim();
            candidate = System.Text.RegularExpressions.Regex.Replace(candidate, @"[+-]?\d+(?:\.\d+)?", " ").Trim();
            candidate = System.Text.RegularExpressions.Regex.Replace(candidate, @"\b(over|under|goals?|corners?|cards?|bookings?|points?|to win|starts?)\b", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            candidate = System.Text.RegularExpressions.Regex.Replace(candidate, @"\s+", " ").Trim();
            if (!string.IsNullOrWhiteSpace(candidate) && BettingApp.Services.TeamAliasMappingService.IsTeamMatch(teamName, candidate))
            {
                return true;
            }

            var words = teamName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
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

        private static bool HasPlayerName(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (text.Contains("Player", StringComparison.OrdinalIgnoreCase) || text.Contains("Player's", StringComparison.OrdinalIgnoreCase)) return true;
            string stripped = System.Text.RegularExpressions.Regex.Replace(text, @"\b(over|under|yes|no|total|team|home|away|goalkeeper|goal|keeper|saves|save|fouls|foul|shots|shot|target|tackles|tackle|points|point|cards|card|to|be|or|record|score|at|least)\b", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
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
                var split = matchName.Split(new[] { " vs ", " v ", " - " }, StringSplitOptions.None);
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
    }
}
