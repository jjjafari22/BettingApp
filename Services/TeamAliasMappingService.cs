using System;
using System.Collections.Generic;

namespace BettingApp.Services
{
    public class TeamAliasMappingService
    {
        // Maps alternative/bilingual team names to their standard API format.
        // We use string replacement so "Kuopion Palloseura U21" becomes "Kups U21".
        private readonly Dictionary<string, string> _teamAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            { "kuopion palloseura", "kups" },
            { "st georgen", "san giorgio" },
            { "asc st georgen", "san giorgio" },
            { "sudtirol", "fc sudtirol" }
        };

        public string ApplyTeamAliases(string normalizedTeamName)
        {
            if (string.IsNullOrWhiteSpace(normalizedTeamName)) return normalizedTeamName;

            string result = normalizedTeamName;
            
            foreach (var alias in _teamAliases)
            {
                result = result.Replace(alias.Key, alias.Value, StringComparison.OrdinalIgnoreCase);
            }
            
            return result;
        }
    }
}
