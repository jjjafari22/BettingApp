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
                         .Replace("å", "a").Replace("Å", "A")
                         .Replace("ı", "i").Replace("İ", "I")
                         .Replace("ł", "l").Replace("Ł", "L")
                         .Replace("đ", "d").Replace("Đ", "D")
                         .Replace("ß", "ss");
        }

        // Maps alternative/bilingual team names to their standard API format.
        // We use string replacement so "Kuopion Palloseura U21" becomes "Kups U21".
        private static readonly Dictionary<string, string> _teamAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            { "borussia monchengladbach", "borussia m'gladbach" },
            { "monchengladbach", "m'gladbach" },
            { "mönchengladbach", "m'gladbach" },
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
            { "stade rennais", "rennes" },
            { "al draih", "al diriyah" },
            { "al-draih", "al diriyah" },
            { "al fateh", "al fateh fc" },
            { "al-fateh", "al fateh fc" },
            { "al nassr", "al nassr fc" },
            { "al-nassr", "al nassr fc" },
            { "al taawoun", "al-taawoun" },
            { "al tawoun", "al-taawoun" },
            { "al ettifaq", "al-ettifaq" },
            { "al-ettifaq", "al-ettifaq" },
            { "al qadisiyah", "al qadsiah" },
            { "al-qadisiyah", "al qadsiah" },
            { "al qadisiya", "al qadsiah" },
            { "qadisiyah", "qadsiah" },
            { "qadisiya", "qadsiah" },
            { "al ittihad", "al ittihad" },
            { "al-ittihad", "al ittihad" },
            { "eidsvold turn", "eidsvold tf" },
            { "ullensaker/kisa", "ull/kisa" },
            { "ullensaker kisa", "ull/kisa" },
            { "d.c. united", "dc united" },
            { "ca vinotinto", "vinotinto del ecuador" },
            { "athletic bilbao", "athletic club" },
            { "inter milan", "internazionale" },
            { "sporting lisbon", "sporting cp" },
            { "boca juniors", "boca" },
            { "fc copenhagen", "fc kobenhavn" },
            { "copenhagen", "kobenhavn" },
            { "munich", "munchen" },
            { "man utd", "manchester united" },
            { "odense bk", "ob" },
            { "aarhus", "agf" },
            { "cologne", "koln" },
            { "fc cologne", "1. fc koln" },
            { "hebar pazardzhik", "hebar" },
            { "cska sofia reserves", "cska sofia ii" },
            { "partizan belgrade", "partizan" },
            { "ucv moquegua", "ucv" },
            { "frederiksberg alliancen 2000", "fa 2000" },
            { "atletico mineiro", "atletico mg" },
            { "manchester utd", "manchester united" },
            { "man city", "manchester city" },
            { "spurs", "tottenham hotspur" },
            { "wolves", "wolverhampton wanderers" },
            { "qpr", "queens park rangers" },
            { "van buyuksehir belediyespor", "van spor kulubu" },
            { "deportes union la calera", "union la calera" }
        };

        public static string ApplyTeamAliases(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;

            string result = name;
            
            if (_teamAliases.TryGetValue(result, out var directAlias))
            {
                return directAlias;
            }

            foreach (var alias in _teamAliases)
            {
                result = result.Replace(alias.Key, alias.Value, StringComparison.OrdinalIgnoreCase);
            }
            
            return result;
        }

            private static readonly HashSet<string> _stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) 
            { 
                "fc", "fk", "united", "city", "cf", "cd", "bk", "sc", "ec", "if" 
            };

            private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _normalizationCache = new();
            
            public string NormalizeTeamName(string name, bool removeStopWords = true)
            {
                if (string.IsNullOrEmpty(name)) return "";
                
                string cacheKey = removeStopWords ? name : name + "_noStop";
                if (_normalizationCache.TryGetValue(cacheKey, out var cachedValue))
                {
                    return cachedValue;
                }
                
                string result = RemoveDiacritics(name).ToLowerInvariant();
                           
                result = ApplyTeamAliases(result);
    
                result = result.Replace("ø", "o")
                           .Replace("æ", "a")
                           .Replace("å", "a")
                           .Replace("oe", "o")
                           .Replace("ae", "a")
                           .Replace("aa", "a")
                           .Replace("-", " ")
                           .Replace(" women", " (w)")
                           .Replace("women", "(w)");
                           
                if (removeStopWords)
                {
                    var stopWords = _stopWords;
                
                    var words = System.Linq.Enumerable.Where(
                        result.Split(new[] { ' ', '.' }, StringSplitOptions.RemoveEmptyEntries),
                        w => !stopWords.Contains(w)
                    );
                                  
                    var finalRes = string.Join(" ", words).Trim();
                    _normalizationCache[cacheKey] = finalRes;
                    return finalRes;
                }
            
                var finalResNoStop = result.Trim();
                _normalizationCache[cacheKey] = finalResNoStop;
                return finalResNoStop;
            }
    }
}
