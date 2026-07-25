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
            { "1. Half: Goals Handicap", "Half Time Handicap" },
            { "Yellow Cards: Total", "Total Cards" },
            { "Total Goals", "Over Under Full Time" },
            { "Total Goals 2", "Over Under Full Time" },
            { "Total Goals 1", "Over Under Full Time" },
            { "Total Goals 3", "Over Under Full Time" },
            { "Total Goals 4", "Over Under Full Time" },
            { "Player Shots on Target", "Player's shot on target" },
            { "Full Time", "Full Time Result" },
            { "Match Odds", "Full Time Result" },
            { "1x2", "Full Time Result" },
            { "Match Result", "Full Time Result" },
            { "Match Result (1X2)", "Full Time Result" }
        };

        public string NormalizeMarketName(string rawMarketName, string? matchName = null)
        {
            if (string.IsNullOrWhiteSpace(rawMarketName)) return rawMarketName;

            var clean = rawMarketName.Trim();
            
            // Handle Team Specific Markets dynamically if matchName is provided
            if (!string.IsNullOrWhiteSpace(matchName))
            {
                var split = matchName.Split(new[] { " vs ", " v ", " - " }, StringSplitOptions.None);
                if (split.Length >= 2)
                {
                    string team1 = split[0].Trim();
                    string team2 = split[1].Trim();

                    // Corners Team 1/2
                    if (clean.Contains("Corners", StringComparison.OrdinalIgnoreCase))
                    {
                        if (clean.Contains(team1, StringComparison.OrdinalIgnoreCase) || clean.Contains("Home", StringComparison.OrdinalIgnoreCase))
                            return "Corners - Over Under Team 1";
                        if (clean.Contains(team2, StringComparison.OrdinalIgnoreCase) || clean.Contains("Away", StringComparison.OrdinalIgnoreCase))
                            return "Corners - Over Under Team 2";
                        
                        // Otherwise, generic corners
                        if (clean.StartsWith("Total Corners", StringComparison.OrdinalIgnoreCase) || clean.StartsWith("Corners", StringComparison.OrdinalIgnoreCase))
                            return "Corners - Over Under Full Time";
                    }

                    // Goals Team 1/2
                    if (clean.Contains("Total Goals", StringComparison.OrdinalIgnoreCase) || clean.Contains("Goals", StringComparison.OrdinalIgnoreCase))
                    {
                        if (clean.Contains(team1, StringComparison.OrdinalIgnoreCase) || clean.Contains("Home", StringComparison.OrdinalIgnoreCase))
                            return "Over Under Team 1";
                        if (clean.Contains(team2, StringComparison.OrdinalIgnoreCase) || clean.Contains("Away", StringComparison.OrdinalIgnoreCase))
                            return "Over Under Team 2";
                    }
                }
            }
            
            // 1. Check if we have an explicit override for this issue
            if (_marketAliases.TryGetValue(clean, out var mapped))
            {
                return mapped;
            }

            // Clean up common AI hallucinations where it appends numbers to Total Goals
            if (clean.StartsWith("Total Goals", StringComparison.OrdinalIgnoreCase))
            {
                return "Over Under Full Time";
            }

            // 2. Otherwise return what the AI gave us
            return clean;
        }
    }
}
