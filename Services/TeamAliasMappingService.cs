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
            { "paris sg", "paris saint germain" },
            { "paris s-g", "paris saint germain" },
            { "paris saint-germain", "paris saint germain" },
            { "aalesunds", "aalesund" },
            { "nacional de montevideo", "nacional" },
            { "albion fc", "albion" },
            { "stade rennais", "rennes" },
            { "al draih", "al diriyah" },
            { "al-draih", "al diriyah" },
            { "draih", "diriyah" },
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
            { "inter milano", "internazionale" },
            { "internazionale milano", "internazionale" },
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
            { "manchester c", "manchester city" },
            { "manchester u", "manchester united" },
            { "spurs", "tottenham hotspur" },
            { "wolves", "wolverhampton wanderers" },
            { "qpr", "queens park rangers" },
            { "van buyuksehir belediyespor", "van spor kulubu" },
            { "deportes union la calera", "union la calera" },
            { "praha", "prague" },
            { "sparta praha", "sparta prague" },
            { "slavia praha", "slavia prague" },
            { "bohemians praha", "bohemians prague" },
            { "dukla praha", "dukla prague" },
            { "bucuresti", "bucharest" },
            { "warszawa", "warsaw" },
            { "lisboa", "lisbon" },
            { "wien", "vienna" },
            { "moskva", "moscow" },
            { "kyiv", "kiev" },
            { "plzen", "pilsen" }
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
                result = System.Text.RegularExpressions.Regex.Replace(result, $@"\b{System.Text.RegularExpressions.Regex.Escape(alias.Key)}\b", alias.Value, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
            
            return result;
        }

        public static string NormalizeForComparison(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            string cleaned = RemoveDiacritics(name).ToLowerInvariant();
            // Replace hyphens, dashes, slashes, underscores, colons with spaces
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"[\-_/\\:]+", " ");
            cleaned = ApplyTeamAliases(cleaned);
            cleaned = cleaned.Replace("oe", "o")
                             .Replace("ae", "a")
                             .Replace("aa", "a");
            // Remove punctuation, collapse spaces
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"[^\w\s]", "");
            return System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", " ").Trim();
        }

        public static string StripToAlphaNumeric(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            string cleaned = NormalizeForComparison(name);
            return cleaned.Replace(" ", "");
        }

        private static bool IsPureLineOutcome(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            string lettersOnly = new string(s.Where(char.IsLetter).ToArray()).ToLowerInvariant();
            return lettersOnly == "over" || lettersOnly == "under";
        }

        public static bool IsTeamMatch(string? teamA, string? teamB)
        {
            if (string.IsNullOrWhiteSpace(teamA) || string.IsNullOrWhiteSpace(teamB)) return false;
            if (string.Equals(teamA, teamB, StringComparison.OrdinalIgnoreCase)) return true;

            string normA = NormalizeForComparison(teamA);
            string normB = NormalizeForComparison(teamB);

            if (string.IsNullOrEmpty(normA) || string.IsNullOrEmpty(normB)) return false;
            if (normA == normB) return true;

            if (IsPureLineOutcome(normA) || IsPureLineOutcome(normB)) return false;

            string compactA = normA.Replace(" ", "");
            string compactB = normB.Replace(" ", "");
            if (compactA == compactB) return true;

            // Substring / word containment (require at least 3 chars)
            if (compactA.Length >= 3 && compactB.Length >= 3)
            {
                if (normA.Contains(normB) || normB.Contains(normA)) return true;
                if (compactA.Contains(compactB) || compactB.Contains(compactA)) return true;
            }

            // Token overlap: Check significant tokens (length >= 3, excluding generic words)
            var genericWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "fc", "fk", "cf", "cd", "sc", "if", "bk", "club", "ac", "sk", "as", "afc" };
            var tokensA = normA.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => t.Length >= 3 && !genericWords.Contains(t)).ToList();
            var tokensB = normB.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => t.Length >= 3 && !genericWords.Contains(t)).ToList();

            if (tokensA.Any() && tokensB.Any())
            {
                var shorter = tokensA.Count <= tokensB.Count ? tokensA : tokensB;
                var longer = tokensA.Count <= tokensB.Count ? tokensB : tokensA;
                if (shorter.All(s => longer.Contains(s))) return true;
            }

            return false;
        }

        public static int ResolveTeamSelection(string? selectionText, string? team1Name, string? team2Name, string? opTeam1 = null, string? opTeam2 = null)
        {
            if (string.IsNullOrWhiteSpace(selectionText)) return 0;

            string sel = selectionText.Trim();
            string normSel = NormalizeForComparison(sel);

            // Direct outcome codes
            if (sel == "1" || normSel == "1" || normSel.Equals("home", StringComparison.OrdinalIgnoreCase)) return 1;
            if (sel == "2" || normSel == "2" || normSel.Equals("away", StringComparison.OrdinalIgnoreCase)) return 2;
            if (sel.Equals("x", StringComparison.OrdinalIgnoreCase) || normSel.Equals("draw", StringComparison.OrdinalIgnoreCase) || normSel.Equals("tie", StringComparison.OrdinalIgnoreCase)) return 0;

            // Strip line numbers, handicaps, prop conditions to isolate team candidate
            // e.g. "PSG (+1.5)" -> "PSG", "PSG Over 1.5 Goals" -> "PSG", "Manchester City -1" -> "Manchester City"
            string candidate = System.Text.RegularExpressions.Regex.Replace(sel, @"\([+-]?\d+(?:\.\d+)?\)", " ").Trim();
            candidate = System.Text.RegularExpressions.Regex.Replace(candidate, @"[+-]?\d+(?:\.\d+)?", " ").Trim();
            candidate = System.Text.RegularExpressions.Regex.Replace(candidate, @"\b(over|under|goals?|corners?|cards?|bookings?|points?|to win|starts?|including|incl|overtime|ot|penalties|penalty|shootouts?|so|and|&|or|draw|tie|ties|oavgjort|uavgjort|kryss)\b", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            candidate = System.Text.RegularExpressions.Regex.Replace(candidate, @"\s+", " ").Trim();
            if (string.IsNullOrWhiteSpace(candidate)) candidate = sel;

            bool match1 = (!string.IsNullOrEmpty(team1Name) && IsTeamMatch(candidate, team1Name)) ||
                          (!string.IsNullOrEmpty(opTeam1) && IsTeamMatch(candidate, opTeam1));

            bool match2 = (!string.IsNullOrEmpty(team2Name) && IsTeamMatch(candidate, team2Name)) ||
                          (!string.IsNullOrEmpty(opTeam2) && IsTeamMatch(candidate, opTeam2));

            if (match1 && !match2) return 1;
            if (match2 && !match1) return 2;

            if (match1 && match2)
            {
                string normCand = NormalizeForComparison(candidate);
                string n1 = NormalizeForComparison(team1Name ?? opTeam1);
                string n2 = NormalizeForComparison(team2Name ?? opTeam2);
                if (normCand == n1) return 1;
                if (normCand == n2) return 2;
                if (n1.Contains(normCand) && !n2.Contains(normCand)) return 1;
                if (n2.Contains(normCand) && !n1.Contains(normCand)) return 2;
            }

            if (!match1 && !match2)
            {
                var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) 
                { 
                    "fc", "fk", "united", "city", "cf", "cd", "bk", "sc", "ec", "if", "club",
                    "ac", "sk", "as", "afc", "sv", "tsv", "vfb", "vfl", "ff", "ik", "town", "real", "athletic", "sporting"
                };

                string normCand = NormalizeForComparison(candidate);
                var candTokens = normCand.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(t => t.Length >= 3 && !stopWords.Contains(t))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (candTokens.Any())
                {
                    string normT1 = NormalizeForComparison(team1Name) + " " + NormalizeForComparison(opTeam1);
                    string normT2 = NormalizeForComparison(team2Name) + " " + NormalizeForComparison(opTeam2);

                    var t1Tokens = normT1.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Where(t => t.Length >= 3 && !stopWords.Contains(t))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    var t2Tokens = normT2.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Where(t => t.Length >= 3 && !stopWords.Contains(t))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    bool overlapsT1 = candTokens.Any(t => t1Tokens.Contains(t));
                    bool overlapsT2 = candTokens.Any(t => t2Tokens.Contains(t));

                    if (overlapsT1 && !overlapsT2) return 1;
                    if (overlapsT2 && !overlapsT1) return 2;
                }
            }

            return 0;
        }

            private static readonly HashSet<string> _stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) 
            { 
                "fc", "fk", "united", "city", "cf", "cd", "bk", "sc", "ec", "if", "club"
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
                    if (string.IsNullOrEmpty(finalRes))
                    {
                        finalRes = result.Trim();
                    }
                    _normalizationCache[cacheKey] = finalRes;
                    return finalRes;
                }
            
                var finalResNoStop = result.Trim();
                _normalizationCache[cacheKey] = finalResNoStop;
                return finalResNoStop;
            }
    }
}
