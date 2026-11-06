using System;
using System.Collections.Generic;
using System.Linq;
using BettingApp.Models;

namespace BettingApp.Services
{
    // Market selection and mapping against OddsPapi market lists.
    public static partial class OddsMatchingLogic
    {
        public static OddsPapiMarket? FindBestMarket(
            List<OddsPapiMarket> markets,
            string marketCategory,
            string? matchName,
            string? selectionName,
            MarketMappingService marketMapper,
            string? opMatchName = null)
        {
            if (markets == null || !markets.Any() || string.IsNullOrEmpty(marketCategory)) return null;

            // Reject Bet Builder / composite combination markets that OddsPapi does not offer as single markets
            if (MarketMappingService.IsCombinationMarket(marketCategory, matchName) ||
                (!string.IsNullOrEmpty(selectionName) && MarketMappingService.IsCombinationMarket(selectionName, matchName)))
            {
                return null;
            }

            var normalizedTargets = marketMapper.NormalizeMarketName(marketCategory, matchName, selectionName, opMatchName);

            static string Simplify(string s)
            {
                var clean = TeamAliasMappingService.RemoveDiacritics(s).ToLowerInvariant()
                             .Replace("players", "player")
                             .Replace("player's", "player")
                             .Replace("participants", "participant")
                             .Replace("participant's", "participant")
                             .Replace("shots", "shot")
                             .Replace("cards", "card")
                             .Replace("goals", "goal")
                             .Replace("corners", "corner")
                             .Replace("halves", "half")
                             .Replace("quarters", "quarter")
                             .Replace("sets", "set")
                             .Replace("games", "game")
                             .Replace("aces", "ace")
                             .Replace("faults", "fault")
                             .Replace("breaks", "break")
                             .Replace("rebounds", "rebound")
                             .Replace("assists", "assist")
                             .Replace("steals", "steal")
                             .Replace("blocks", "block")
                             .Replace("turnovers", "turnover")
                             .Replace("points", "point")
                             .Replace("tie-breaks", "tiebreak")
                             .Replace("tie-break", "tiebreak")
                             .Replace("tiebreaks", "tiebreak")
                             .Replace("tie breaks", "tiebreak")
                             .Replace("tie break", "tiebreak");
                return new string(clean.Where(char.IsLetterOrDigit).ToArray());
            }

            var simplifiedTargets = normalizedTargets.Select(Simplify).ToList();

            OddsPapiMarket? bestMatch = null;
            foreach (var target in simplifiedTargets)
            {
                bestMatch = markets.FirstOrDefault(m => Simplify(m.MarketName) == target);
                if (bestMatch != null) break;
            }

            if (bestMatch == null)
            {
                var statKeywords = new[] { "corner", "booking", "card", "foul", "save", "goalkeeper", "shot", "tackle", "offside", "ace", "fault", "tiebreak", "rebound", "assist", "steal", "block", "turnover", "3point" };
                var subPeriodTokens = new[] { "firstquarter", "secondquarter", "thirdquarter", "fourthquarter", "firsthalf", "secondhalf", "firstperiod", "secondperiod", "thirdperiod", "firstset", "secondset", "thirdset", "fourthset", "fifthset" };
                var playerComboStats = new[] { "rebound", "assist", "steal", "block", "turnover", "3point" };

                foreach (var target in simplifiedTargets)
                {
                    if (target == "winner" || target == "winnerinclovertime" || target == "correctscore") continue;

                    var targetStats = statKeywords.Where(kw => target.Contains(kw)).ToList();

                    var potentialMatches = markets.Where(m =>
                    {
                        string simM = Simplify(m.MarketName);

                        // Never cross-match sub-period qualifiers (e.g., First Quarter vs Full Game)
                        foreach (var sp in subPeriodTokens)
                        {
                            if (target.Contains(sp) != simM.Contains(sp)) return false;
                        }

                        // For player markets, ensure exact stat combination alignment so single-stat never matches combo-stat (e.g. Points vs Points + Rebounds)
                        if (target.Contains("player") || simM.Contains("player"))
                        {
                            string targetNo3Pt = target.Replace("3point", "").Replace("breakpoint", "").Replace("powerplaypoint", "");
                            string simNo3Pt = simM.Replace("3point", "").Replace("breakpoint", "").Replace("powerplaypoint", "");
                            if (targetNo3Pt.Contains("point") != simNo3Pt.Contains("point")) return false;
                            foreach (var pStat in playerComboStats)
                            {
                                if (target.Contains(pStat) != simM.Contains(pStat)) return false;
                            }
                        }

                        if (targetStats.Any())
                        {
                            bool hasMatchingStat = targetStats.Any(kw =>
                                simM.Contains(kw) ||
                                (kw == "booking" && simM.Contains("card")) ||
                                (kw == "card" && simM.Contains("booking")) ||
                                (kw == "goalkeeper" && simM.Contains("save")) ||
                                (kw == "save" && simM.Contains("goalkeeper")));
                            if (!hasMatchingStat) return false;
                        }
                        else
                        {
                            if (statKeywords.Any(kw => simM.Contains(kw))) return false;
                        }

                        if (target.Contains("foul") || simM.Contains("foul"))
                        {
                            bool targetIsFoulsWon = target.Contains("foul") && (target.Contains("won") || target.Contains("drawn") || target.Contains("suffered") || target.Contains("fouled"));
                            bool simIsFoulsWon = simM.Contains("foul") && (simM.Contains("won") || simM.Contains("drawn") || simM.Contains("suffered") || simM.Contains("fouled"));
                            if (targetIsFoulsWon != simIsFoulsWon) return false;
                        }

                        return simM.Contains(target) || (target.Contains(simM) && Math.Abs(target.Length - simM.Length) <= 4);
                    }).ToList();

                    if (potentialMatches.Count == 1)
                    {
                        bestMatch = potentialMatches.First();
                        break;
                    }
                    else if (potentialMatches.Count > 1 && !string.IsNullOrEmpty(selectionName))
                    {
                        string normSel = Simplify(selectionName).Replace("yes", "").Replace("no", "").Replace("over05", "").Replace("under05", "");
                        if (normSel.Length > 3)
                        {
                            bestMatch = potentialMatches.FirstOrDefault(m =>
                                IsFuzzyPlayerMatch(selectionName, m.MarketName) ||
                                Simplify(m.MarketName).Contains(normSel) ||
                                normSel.Contains(Simplify(m.MarketName)));
                        }
                        bestMatch ??= potentialMatches.First();
                        break;
                    }
                    else if (potentialMatches.Count > 1)
                    {
                        bestMatch = potentialMatches.First();
                        break;
                    }
                }
            }

            if (bestMatch == null && simplifiedTargets.Any(t => t.Contains("teamtotalgoal")))
            {
                if (!string.IsNullOrEmpty(matchName) && !string.IsNullOrEmpty(selectionName))
                {
                    var hcSplit = TeamAliasMappingService.SplitMatchTeams(matchName);
                    if (hcSplit.Length >= 2)
                    {
                        int matchedTeam = TeamAliasMappingService.ResolveTeamSelection(selectionName, hcSplit[0], hcSplit[1]);
                        string expectedTeamMarket = matchedTeam switch
                        {
                            1 => Simplify("Over Under Team 1"),
                            2 => Simplify("Over Under Team 2"),
                            _ => ""
                        };

                        if (!string.IsNullOrEmpty(expectedTeamMarket))
                        {
                            bestMatch = markets.FirstOrDefault(m => Simplify(m.MarketName) == expectedTeamMarket) ??
                                        markets.FirstOrDefault(m => Simplify(m.MarketName).Contains(expectedTeamMarket));
                        }
                    }
                }
            }

            return bestMatch;
        }
    }
}
