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
            { "diriya", "diriyah" },
            { "al fateh al hasa", "al fateh" },
            { "al fateh al ahsa", "al fateh" },
            { "al fateh", "al fateh" },
            { "al-fateh", "al fateh" },
            { "al fath", "al fateh" },
            { "al nassr riyadh", "al nassr" },
            { "al nasr riyadh", "al nassr" },
            { "al nassr", "al nassr" },
            { "al-nassr", "al nassr" },
            { "al hilal riyadh", "al hilal" },
            { "al shabab riyadh", "al shabab" },
            { "al ahli jeddah", "al ahli" },
            { "al ahly jeddah", "al ahli" },
            { "al ahli saudi", "al ahli" },
            { "al ittihad jeddah", "al ittihad" },
            { "al ettihad jeddah", "al ittihad" },
            { "al ettihad", "al ittihad" },
            { "al-ettihad", "al ittihad" },
            { "ettihad", "ittihad" },
            { "al wahda mecca", "al wehda" },
            { "al-wahda mecca", "al wehda" },
            { "al wehda mecca", "al wehda" },
            { "al-wehda mecca", "al wehda" },
            { "al wahda makkah", "al wehda" },
            { "al-wahda makkah", "al wehda" },
            { "al wehda makkah", "al wehda" },
            { "al-wehda makkah", "al wehda" },
            { "wahda mecca", "wehda" },
            { "wehda mecca", "wehda" },
            { "wahda makkah", "wehda" },
            { "wehda makkah", "wehda" },
            { "al wahda", "al wehda" },
            { "al-wahda", "al wehda" },
            { "wahda", "wehda" },
            { "al taawoun", "al taawoun" },
            { "al tawoun", "al taawoun" },
            { "al taawon buraidah", "al taawoun" },
            { "al-taawon buraidah", "al taawoun" },
            { "al taawoun buraidah", "al taawoun" },
            { "al-taawoun buraidah", "al taawoun" },
            { "al tawoun buraidah", "al taawoun" },
            { "al taawon", "al taawoun" },
            { "al-taawon", "al taawoun" },
            { "taawon", "taawoun" },
            { "tawoun", "taawoun" },
            { "tawon", "taawoun" },
            { "al raed buraidah", "al raed" },
            { "al-raed buraidah", "al raed" },
            { "al adalah al hasa", "al adalah" },
            { "al adalah al ahsa", "al adalah" },
            { "al adalh", "al adalah" },
            { "al-adalh", "al adalah" },
            { "al adala", "al adalah" },
            { "al-adala", "al adalah" },
            { "adalh", "adalah" },
            { "adala", "adalah" },
            { "al okhdood najran", "al okhdood" },
            { "al akhdoud najran", "al okhdood" },
            { "al okhdood", "al okhdood" },
            { "al-okhdood", "al okhdood" },
            { "al akhdoud", "al okhdood" },
            { "al-akhdoud", "al okhdood" },
            { "al akhdood", "al okhdood" },
            { "al-akhdood", "al okhdood" },
            { "akhdoud", "okhdood" },
            { "akhdood", "okhdood" },
            { "ukhdood", "okhdood" },
            { "al ettifaq dammam", "al ittifaq" },
            { "al ittifaq dammam", "al ittifaq" },
            { "al ettifaq", "al ittifaq" },
            { "al-ettifaq", "al ittifaq" },
            { "ettifaq", "ittifaq" },
            { "al qadsiah khobar", "al qadsiah" },
            { "al qadisiyah khobar", "al qadsiah" },
            { "al qadisiyah", "al qadsiah" },
            { "al-qadisiyah", "al qadsiah" },
            { "al qadisiya", "al qadsiah" },
            { "al-qadisiya", "al qadsiah" },
            { "qadisiyah", "qadsiah" },
            { "qadisiya", "qadsiah" },
            { "qadsia", "qadsiah" },
            { "al ittihad", "al ittihad" },
            { "al-ittihad", "al ittihad" },
            { "al fayha al majmaah", "al fayha" },
            { "al feiha", "al fayha" },
            { "al-feiha", "al fayha" },
            { "al faiha", "al fayha" },
            { "al-faiha", "al fayha" },
            { "feiha", "fayha" },
            { "faiha", "fayha" },
            { "al khaleej saihat", "al khaleej" },
            { "al khalij", "al khaleej" },
            { "al-khalij", "al khaleej" },
            { "khalij", "khaleej" },
            { "al kholood ar rass", "al kholood" },
            { "al khulood", "al kholood" },
            { "al-khulood", "al kholood" },
            { "khulood", "kholood" },
            { "khlood", "kholood" },
            { "al hazem ar rass", "al hazm" },
            { "al hazm ar rass", "al hazm" },
            { "al hazem", "al hazm" },
            { "al-hazem", "al hazm" },
            { "hazem", "hazm" },
            { "al orobah sakaka", "al orobah" },
            { "al urubah", "al orobah" },
            { "al-urubah", "al orobah" },
            { "al orouba", "al orobah" },
            { "al-orouba", "al orobah" },
            { "al orubah", "al orobah" },
            { "al-orubah", "al orobah" },
            { "urubah", "orobah" },
            { "orouba", "orobah" },
            { "orubah", "orobah" },
            { "al bukayriyah", "al bukiryah" },
            { "al-bukayriyah", "al bukiryah" },
            { "bukayriyah", "bukiryah" },
            { "bakiryah", "bukiryah" },
            { "bukiriyah", "bukiryah" },
            { "al tai hail", "al tai" },
            { "al taee hail", "al tai" },
            { "al taee", "al tai" },
            { "al-taee", "al tai" },
            { "taee", "tai" },
            { "al jabalain hail", "al jabalain" },
            { "al jabalayn", "al jabalain" },
            { "al-jabalayn", "al jabalain" },
            { "jabalayn", "jabalain" },
            { "al najma unaizah", "al najma" },
            { "al najmah", "al najma" },
            { "al-najmah", "al najma" },
            { "najmah", "najma" },
            { "al arabi unaizah", "al arabi" },
            { "al faisaly harmah", "al faisaly" },
            { "al faisali", "al faisaly" },
            { "al-faisali", "al faisaly" },
            { "faisali", "faisaly" },
            { "al batin hafar al batin", "al batin" },
            { "al baten", "al batin" },
            { "al-baten", "al batin" },
            { "baten", "batin" },
            { "al shoulla", "al shoulla" },
            { "al shoalah", "al shoulla" },
            { "al-shoalah", "al shoulla" },
            { "shoalah", "shoulla" },
            { "shoullah", "shoulla" },
            { "al rawdah", "al rawdhah" },
            { "al-rawdah", "al rawdhah" },
            { "rawdah", "rawdhah" },
            { "al qaisoma", "al qaisumah" },
            { "al-qaisoma", "al qaisumah" },
            { "qaisoma", "qaisumah" },
            { "qaisoomah", "qaisumah" },
            { "al zulfy", "al zulfi" },
            { "al-zulfy", "al zulfi" },
            { "zulfy", "zulfi" },
            { "al nujoom", "al nojoom" },
            { "al-nujoom", "al nojoom" },
            { "nujoom", "nojoom" },
            { "al intisar", "al entesar" },
            { "al-intisar", "al entesar" },
            { "intisar", "entesar" },
            { "al hottain", "hetten" },
            { "al-hottain", "hetten" },
            { "hottain", "hetten" },
            { "ohod medina", "ohod" },
            { "al ohod", "ohod" },
            { "al-ohod", "ohod" },
            { "uhud", "ohod" },
            { "wej taif", "wej" },
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
            { "atletico paranaense", "athletico paranaense" },
            { "atletico pr", "athletico paranaense" },
            { "athletico pr", "athletico paranaense" },
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

        private static readonly System.Text.RegularExpressions.Regex _aliasesCombinedRegex = new(
            @"\b(" + string.Join("|", _teamAliases.Keys.OrderByDescending(k => k.Length).Select(System.Text.RegularExpressions.Regex.Escape)) + @")\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        public static string ApplyTeamAliases(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;

            string result = name;
            
            if (_teamAliases.TryGetValue(result, out var directAlias))
            {
                return directAlias;
            }

            return _aliasesCombinedRegex.Replace(result, m => _teamAliases.TryGetValue(m.Value, out var val) ? val : m.Value);
        }

        public static string NormalizeForComparison(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            string cleaned = RemoveDiacritics(name).ToLowerInvariant();
            // Strip parenthesized 2-3 letter country disambiguators used by OddsPapi, e.g. "(SA)", "(EGY)", "(JOR)", while preserving "(w)"
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\([a-z]{2,3}\)", " ");
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

        public static bool HasArabicArticle(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return System.Text.RegularExpressions.Regex.IsMatch(text, @"\b(al|el)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        public static string GetArabicTransliterationSkeleton(string? token)
        {
            if (string.IsNullOrWhiteSpace(token)) return "";
            string s = RemoveDiacritics(token).ToLowerInvariant().Trim();
            s = System.Text.RegularExpressions.Regex.Replace(s, @"[^a-z0-9]", "");
            if (s.Length <= 3) return s;

            s = s.Replace("kh", "k")
                 .Replace("gh", "g")
                 .Replace("dh", "d")
                 .Replace("th", "t")
                 .Replace("ph", "f");

            if (s.Length > 4 && s.EndsWith("h"))
            {
                s = s.Substring(0, s.Length - 1);
            }

            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (i == 0 || s[i] != s[i - 1])
                {
                    sb.Append(s[i]);
                }
            }
            s = sb.ToString();

            s = System.Text.RegularExpressions.Regex.Replace(s, @"[aeiouy]+", "a");
            return s;
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

            // Token overlap: Check significant tokens (length >= 3, excluding generic club words and pure founding-year numbers)
            var genericWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "fc", "fk", "cf", "cd", "sc", "if", "bk", "club", "ac", "sk", "as", "afc",
                "sfc", "cfc", "rfc", "bfc", "lfc", "ufc", "scu", "ssc", "csc", "hnk", "gnk", "krc", "bsc", "tsg", "fsv", "rcd", "sv", "tsv", "vfb", "vfl", "saudi"
            };
            var allWordsA = normA.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var allWordsB = normB.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            bool hasSingleLetterInitial = allWordsA.Any(t => t.Length == 1 && char.IsLetter(t[0])) ||
                                          allWordsB.Any(t => t.Length == 1 && char.IsLetter(t[0]));

            var tokensA = allWordsA.Where(t => t.Length >= 3 && !genericWords.Contains(t) && !t.All(char.IsDigit)).ToList();
            var tokensB = allWordsB.Where(t => t.Length >= 3 && !genericWords.Contains(t) && !t.All(char.IsDigit)).ToList();

            if (!hasSingleLetterInitial && tokensA.Any() && tokensB.Any())
            {
                var shorter = tokensA.Count <= tokensB.Count ? tokensA : tokensB;
                var longer = tokensA.Count <= tokensB.Count ? tokensB : tokensA;
                if (shorter.All(s => longer.Contains(s))) return true;

                // Arabic transliteration skeleton check when either team has "Al"/"El"
                if (HasArabicArticle(teamA) || HasArabicArticle(teamB))
                {
                    var skelA = tokensA.Where(t => t.Length >= 4).Select(GetArabicTransliterationSkeleton).Where(s => s.Length >= 3).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var skelB = tokensB.Where(t => t.Length >= 4).Select(GetArabicTransliterationSkeleton).Where(s => s.Length >= 3).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    if (skelA.Any() && skelB.Any() && skelA.Overlaps(skelB))
                    {
                        return true;
                    }
                }
            }

            // Person / Doubles / Initials / Short-surname token match (e.g. "Fils, Arthur" <-> "Arthur Fils", "Medvedev, D" <-> "Daniil Medvedev", "Ann Li" <-> "Li, A", "Stan Wawrinka" <-> "Wawrinka, Stanislas")
            if (ArePersonOrTeamTokensMatched(normA, normB, genericWords))
            {
                return true;
            }

            return false;
        }

        private static bool ArePersonOrTeamTokensMatched(string normA, string normB, HashSet<string>? genericWords = null)
        {
            var wordsA = normA.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                              .Where(t => (genericWords == null || !genericWords.Contains(t)) && !t.All(char.IsDigit))
                              .ToList();
            var wordsB = normB.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                              .Where(t => (genericWords == null || !genericWords.Contains(t)) && !t.All(char.IsDigit))
                              .ToList();

            if (wordsA.Count < 2 || wordsB.Count < 2) return false;

            var shorter = wordsA.Count <= wordsB.Count ? wordsA : wordsB;
            var longer = wordsA.Count <= wordsB.Count ? wordsB : wordsA;

            var remaining = longer.ToList();
            var unmatched = new List<string>();
            int exactMatchesLen2Plus = 0;

            // Pass 1: Exact token matches first
            foreach (var token in shorter)
            {
                int idx = remaining.FindIndex(r => r.Equals(token, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                {
                    if (token.Length >= 2) exactMatchesLen2Plus++;
                    remaining.RemoveAt(idx);
                }
                else
                {
                    unmatched.Add(token);
                }
            }

            if (exactMatchesLen2Plus == 0) return false;
            if (unmatched.Count == 0) return true;

            // Pass 2: Initials (e.g. "D" <-> "Daniil", "A" <-> "Ann") and shortened first names (e.g. "Stan" <-> "Stanislas")
            foreach (var token in unmatched.ToList())
            {
                if (token.Length == 1)
                {
                    int initIdx = remaining.FindIndex(r => r.StartsWith(token, StringComparison.OrdinalIgnoreCase));
                    if (initIdx >= 0)
                    {
                        unmatched.Remove(token);
                        remaining.RemoveAt(initIdx);
                    }
                }
                else if (token.Length >= 2)
                {
                    int initInRemIdx = remaining.FindIndex(r => r.Length == 1 && token.StartsWith(r, StringComparison.OrdinalIgnoreCase));
                    if (initInRemIdx >= 0)
                    {
                        unmatched.Remove(token);
                        remaining.RemoveAt(initInRemIdx);
                        continue;
                    }

                    if (token.Length >= 3)
                    {
                        int prefixIdx = remaining.FindIndex(r => r.Length >= 3 &&
                            (r.StartsWith(token, StringComparison.OrdinalIgnoreCase) || token.StartsWith(r, StringComparison.OrdinalIgnoreCase)));
                        if (prefixIdx >= 0)
                        {
                            unmatched.Remove(token);
                            remaining.RemoveAt(prefixIdx);
                        }
                    }
                }
            }

            return unmatched.Count == 0;
        }

        public static int ResolveTeamSelection(string? selectionText, string? team1Name, string? team2Name, string? opTeam1 = null, string? opTeam2 = null)
        {
            if (string.IsNullOrWhiteSpace(selectionText)) return 0;

            string sel = selectionText.Trim();
            string normSel = NormalizeForComparison(sel);

            // Direct outcome codes
            if (sel == "1" || normSel == "1" || normSel.Equals("home", StringComparison.OrdinalIgnoreCase) || normSel.Equals("hjemme", StringComparison.OrdinalIgnoreCase) || normSel.Equals("hemmalag", StringComparison.OrdinalIgnoreCase) || normSel.Equals("team 1", StringComparison.OrdinalIgnoreCase) || normSel.Equals("player 1", StringComparison.OrdinalIgnoreCase) || normSel.Equals("participant 1", StringComparison.OrdinalIgnoreCase)) return 1;
            if (sel == "2" || normSel == "2" || normSel.Equals("away", StringComparison.OrdinalIgnoreCase) || normSel.Equals("borte", StringComparison.OrdinalIgnoreCase) || normSel.Equals("bortalag", StringComparison.OrdinalIgnoreCase) || normSel.Equals("team 2", StringComparison.OrdinalIgnoreCase) || normSel.Equals("player 2", StringComparison.OrdinalIgnoreCase) || normSel.Equals("participant 2", StringComparison.OrdinalIgnoreCase)) return 2;
            if (sel.Equals("x", StringComparison.OrdinalIgnoreCase) || normSel.Equals("draw", StringComparison.OrdinalIgnoreCase) || normSel.Equals("tie", StringComparison.OrdinalIgnoreCase) || normSel.Equals("uavgjort", StringComparison.OrdinalIgnoreCase) || normSel.Equals("oavgjort", StringComparison.OrdinalIgnoreCase) || normSel.Equals("kryss", StringComparison.OrdinalIgnoreCase) || normSel.Equals("likt", StringComparison.OrdinalIgnoreCase)) return 0;

            if (System.Text.RegularExpressions.Regex.IsMatch(normSel, @"\b(?:team|player|participant)\s*1\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase) &&
                !System.Text.RegularExpressions.Regex.IsMatch(normSel, @"\b(?:team|player|participant)\s*2\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                return 1;
            }
            if (System.Text.RegularExpressions.Regex.IsMatch(normSel, @"\b(?:team|player|participant)\s*2\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase) &&
                !System.Text.RegularExpressions.Regex.IsMatch(normSel, @"\b(?:team|player|participant)\s*1\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                return 2;
            }

            // Strip line numbers, handicaps, prop conditions to isolate team candidate
            // e.g. "PSG (+1.5)" -> "PSG", "PSG Over 1.5 Goals" -> "PSG", "Manchester City -1" -> "Manchester City"
            string candidate = System.Text.RegularExpressions.Regex.Replace(sel, @"\([+-]?\d+(?:\.\d+)?\)", " ").Trim();
            candidate = System.Text.RegularExpressions.Regex.Replace(candidate, @"[+-]?\d+(?:\.\d+)?", " ").Trim();
            candidate = System.Text.RegularExpressions.Regex.Replace(candidate, @"\b(over|under|o\/u|total|goals?|corners?|cards?|bookings?|points?|shots?|on|target|saves?|goalkeeper|goal\s*keeper|fouls?|committed|tackles?|offsides?|aces?|double\s+faults?|faults?|games?|sets?|breaks?|service|tie\s*breaks?|tiebreaks?|won|number|of|1st|2nd|3rd|4th|5th|first|second|third|fourth|fifth|half|halves|either|both|at|least|one|team|player|participant|most|more|to|win|starts?|including|incl|overtime|ot|penalties|penalty|shootouts?|so|and|&|or|eller|draw|tie|ties|oavgjort|uavgjort|kryss|likt|full|time|regular)\b", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            candidate = System.Text.RegularExpressions.Regex.Replace(candidate, @"[^\w\s]", " ").Trim();
            candidate = System.Text.RegularExpressions.Regex.Replace(candidate, @"\s+", " ").Trim();
            if (string.IsNullOrWhiteSpace(candidate)) return 0;

            if (candidate.Equals("home", StringComparison.OrdinalIgnoreCase) || candidate.Equals("hjemme", StringComparison.OrdinalIgnoreCase) || candidate.Equals("hemmalag", StringComparison.OrdinalIgnoreCase)) return 1;
            if (candidate.Equals("away", StringComparison.OrdinalIgnoreCase) || candidate.Equals("borte", StringComparison.OrdinalIgnoreCase) || candidate.Equals("bortalag", StringComparison.OrdinalIgnoreCase)) return 2;

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

                bool strict1 = (!string.IsNullOrEmpty(team1Name) && ArePersonOrTeamTokensMatched(normCand, NormalizeForComparison(team1Name))) ||
                               (!string.IsNullOrEmpty(opTeam1) && ArePersonOrTeamTokensMatched(normCand, NormalizeForComparison(opTeam1)));
                bool strict2 = (!string.IsNullOrEmpty(team2Name) && ArePersonOrTeamTokensMatched(normCand, NormalizeForComparison(team2Name))) ||
                               (!string.IsNullOrEmpty(opTeam2) && ArePersonOrTeamTokensMatched(normCand, NormalizeForComparison(opTeam2)));
                if (strict1 && !strict2) return 1;
                if (strict2 && !strict1) return 2;
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
                "fc", "fk", "united", "city", "cf", "cd", "bk", "sc", "ec", "if", "club",
                "sfc", "ssc", "csc", "afc", "saudi"
            };

            public static bool IsStopWord(string word) => _stopWords.Contains(word);

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
                // Strip parenthesized 2-3 letter country codes like "(SA)", "(EGY)", "(JOR)" while preserving "(w)"
                result = System.Text.RegularExpressions.Regex.Replace(result, @"\([a-z]{2,3}\)", " ");
                result = result.Replace("-", " ").Replace(",", " ");
                result = System.Text.RegularExpressions.Regex.Replace(result, @"\s+", " ").Trim();
                           
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
