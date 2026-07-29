using System;
using System.Collections.Generic;

namespace BettingApp.Services
{
    public class TeamAliasMappingService
    {
        public static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            var normalizedString = text.Normalize(System.Text.NormalizationForm.FormD);
            var stringBuilder = new System.Text.StringBuilder(capacity: normalizedString.Length);
            foreach (var c in normalizedString)
            {
                var unicodeCategory = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != System.Globalization.UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }
            return stringBuilder.ToString().Normalize(System.Text.NormalizationForm.FormC);
        }

        // Maps alternative/bilingual team names to their standard API format.
        // We use string replacement so "Kuopion Palloseura U21" becomes "Kups U21".
        private readonly Dictionary<string, string> _teamAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            { "kuopion palloseura", "kups" },
            { "st georgen", "san giorgio" },
            { "asc st georgen", "san giorgio" },
            { "sudtirol", "fc sudtirol" },
            { "heart of midlothian", "hearts" }
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
