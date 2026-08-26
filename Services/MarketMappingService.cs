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
            { "Asian Over/Under", "Over Under Full Time" },
            { "1. Half: Goals Handicap", "Half Time Handicap" },
            { "Yellow Cards: Total", "Total Cards" },
            { "Total Goals", "Over Under Full Time" },
            { "Total Goals 2", "Over Under Full Time" },
            { "Total Goals 1", "Over Under Full Time" },
            { "Total Goals 3", "Over Under Full Time" },
            { "Total Goals 4", "Over Under Full Time" },
            { "Player Shots on Target", "Over Under Player Shots On Goal (incl. overtime)|Player Shots On Goal (incl. overtime)|Player's shot on target" },
            { "Full Time", "Full Time Result" },
            { "Match Odds", "Full Time Result" },
            { "1x2", "Full Time Result" },
            { "Match Result", "Full Time Result" },
            { "Match Result (1X2)", "Full Time Result" },
            { "1st Half Result", "First Half Result" },
            { "1. Half Result", "First Half Result" },
            { "Half Time Result", "First Half Result" },
            { "1st Half 1x2", "First Half Result" },
            { "2nd Half Result", "Second Half Result" },
            { "2. Half Result", "Second Half Result" },
            { "2nd Half 1x2", "Second Half Result" },
            { "Correct Score", "Correct Score Full Time" },
            { "Early Win", "2Up - Full Time Result" },
            { "Early Win (Anytime 2 Goal Lead)", "2Up - Full Time Result" },
            { "Early Payout", "2Up - Full Time Result" },
            { "Player to be Booked", "Player To Be Carded (incl. overtime)" },
            { "Player To Receive A Card", "Player To Be Carded (incl. overtime)" },
            { "Player Cards", "Player To Be Carded (incl. overtime)" },
            { "Will/Will not get Booked", "Player To Be Carded (incl. overtime)" }
        };

        public List<string> NormalizeMarketName(string rawMarketName, string? matchName = null)
        {
            if (string.IsNullOrWhiteSpace(rawMarketName)) return new List<string> { rawMarketName };

            var clean = rawMarketName.Trim();
            
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
                        if (MatchesTeam(team1, clean) || clean.Contains("Home", StringComparison.OrdinalIgnoreCase))
                            return new List<string> { "Corners - Over Under Team 1" + halfSuffix };
                        if (MatchesTeam(team2, clean) || clean.Contains("Away", StringComparison.OrdinalIgnoreCase))
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
                        if (MatchesTeam(team1, clean) || clean.Contains("Home", StringComparison.OrdinalIgnoreCase))
                            return new List<string> { "Over Under Team 1" + halfSuffix };
                        if (MatchesTeam(team2, clean) || clean.Contains("Away", StringComparison.OrdinalIgnoreCase))
                            return new List<string> { "Over Under Team 2" + halfSuffix };
                            
                        // Otherwise, generic total goals for the match
                        if (!string.IsNullOrEmpty(halfSuffix)) return new List<string> { "Over Under" + halfSuffix };
                    }

                    // To Win At Least One Half
                    if (clean.Contains("To Win At Least One Half", StringComparison.OrdinalIgnoreCase) || clean.Contains("To Win Either Half", StringComparison.OrdinalIgnoreCase))
                    {
                        if (MatchesTeam(team1, clean) || clean.Contains("Home", StringComparison.OrdinalIgnoreCase))
                            return new List<string> { "Team 1 To Win Either Halves" };
                        if (MatchesTeam(team2, clean) || clean.Contains("Away", StringComparison.OrdinalIgnoreCase))
                            return new List<string> { "Team 2 To Win Either Halves" };
                    }

                    // To Win Both Halves
                    if (clean.Contains("To Win Both Halves", StringComparison.OrdinalIgnoreCase))
                    {
                        if (MatchesTeam(team1, clean) || clean.Contains("Home", StringComparison.OrdinalIgnoreCase))
                            return new List<string> { "Team 1 to win both halves" };
                        if (MatchesTeam(team2, clean) || clean.Contains("Away", StringComparison.OrdinalIgnoreCase))
                            return new List<string> { "Team 2 to win both halves" };
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

            // Clean up common AI hallucinations where it appends numbers to Total Goals
            if (clean.StartsWith("Total Goals", StringComparison.OrdinalIgnoreCase))
            {
                return new List<string> { "Over Under Full Time" };
            }

            // Strip off any trailing scores (like "0 - 1") for 3-Way Handicaps
            if (clean.StartsWith("Handicap (3 Way)", StringComparison.OrdinalIgnoreCase) || 
                clean.StartsWith("3-Way Handicap", StringComparison.OrdinalIgnoreCase))
            {
                return new List<string> { "3-Way Handicap" };
            }

            // Strip off any trailing scores (like "-1.0" or "(0-1)") for Asian Handicaps
            if (clean.StartsWith("Asian Handicap", StringComparison.OrdinalIgnoreCase))
            {
                return new List<string> { "Asian Handicap" };
            }

            // 2. Otherwise return what the AI gave us
            return new List<string> { clean };
        }

        private bool MatchesTeam(string teamName, string marketName)
        {
            if (string.IsNullOrWhiteSpace(teamName) || string.IsNullOrWhiteSpace(marketName)) return false;
            if (marketName.Contains(teamName, StringComparison.OrdinalIgnoreCase)) return true;
            
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
    }
}
