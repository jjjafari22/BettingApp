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
            { "Total Goals 2", "Total Goals" },
            { "Total Goals 1", "Total Goals" },
            { "Total Goals 3", "Total Goals" },
            { "Total Goals 4", "Total Goals" },
            { "Player Shots on Target", "Player's shot on target" }
        };

        public string NormalizeMarketName(string rawMarketName)
        {
            if (string.IsNullOrWhiteSpace(rawMarketName)) return rawMarketName;

            var clean = rawMarketName.Trim();
            
            // 1. Check if we have an explicit override for this issue
            if (_marketAliases.TryGetValue(clean, out var mapped))
            {
                return mapped;
            }

            // Clean up common AI hallucinations where it appends numbers to Total Goals
            if (clean.StartsWith("Total Goals", StringComparison.OrdinalIgnoreCase))
            {
                return "Total Goals";
            }

            // 2. Otherwise return what the AI gave us
            return clean;
        }
    }
}
