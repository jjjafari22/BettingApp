using System;
using System.Collections.Generic;

namespace BettingApp.Services
{
    public class TeamAliasMappingService
    {
        public static string RemoveDiacritics(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text ?? "";
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
            var result = stringBuilder.ToString().Normalize(System.Text.NormalizationForm.FormC);
            return result.Replace("ø", "o").Replace("Ø", "O")
                         .Replace("æ", "a").Replace("Æ", "A")
                         .Replace("å", "a").Replace("Å", "A");
        }

        // Maps alternative/bilingual team names to their standard API format.
        // We use string replacement so "Kuopion Palloseura U21" becomes "Kups U21".
        private static readonly Dictionary<string, string> _teamAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            { "kuopion palloseura", "kups" },
            { "st georgen", "san giorgio" },
            { "asc st georgen", "san giorgio" },
            { "sudtirol", "fc sudtirol" },
            { "heart of midlothian", "hearts" },
            { "os turn", "os" },
            { "red star belgrade", "crvena zvezda" },
            { "ois", "orgryte is" },
            { "paphos", "pafos" },
            { "grasshoppers", "grasshopper" },
            { "psg", "paris saint germain" },
            { "aalesunds", "aalesund" },
            { "nacional de montevideo", "nacional" },
            { "albion fc", "albion" },
            { "stade rennais", "rennes" }
        };

        public static string ApplyTeamAliases(string? normalizedTeamName)
        {
            if (string.IsNullOrWhiteSpace(normalizedTeamName)) return normalizedTeamName ?? "";

            string result = normalizedTeamName;
            
            foreach (var alias in _teamAliases)
            {
                result = result.Replace(alias.Key, alias.Value, StringComparison.OrdinalIgnoreCase);
            }
            
            return result;
        }

        public string NormalizeTeamName(string name, bool removeStopWords = true)
        {
            if (string.IsNullOrEmpty(name)) return "";
            
            string result = RemoveDiacritics(name).ToLowerInvariant();
                       
            result = ApplyTeamAliases(result);

            result = result.Replace("ø", "o")
                       .Replace("æ", "a")
                       .Replace("å", "a")
                       .Replace("oe", "o")
                       .Replace("ae", "a")
                       .Replace("aa", "a")
                       .Replace(" (w)", "")
                       .Replace("-", " ");
                       
            if (removeStopWords)
            {
                var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) 
                { 
                    "fc", "fk", "united", "city", "cf", "cd", "bk", "women", "sc", "ec" 
                };
                
                var words = System.Linq.Enumerable.Where(
                    result.Split(new[] { ' ', '.' }, StringSplitOptions.RemoveEmptyEntries),
                    w => !stopWords.Contains(w)
                );
                                  
                return string.Join(" ", words).Trim();
            }
            
            return result.Trim();
        }
    }
}
