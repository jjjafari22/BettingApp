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
                             .Replace("sets", "set")
                             .Replace("games", "game")
                             .Replace("aces", "ace")
                             .Replace("faults", "fault")
                             .Replace("breaks", "break")
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
                var statKeywords = new[] { "corner", "booking", "card", "foul", "save", "goalkeeper", "shot", "tackle", "offside", "ace", "fault", "tiebreak" };

                foreach (var target in simplifiedTargets)
                {
                    if (target == "winner" || target == "correctscore") continue;

                    var targetStats = statKeywords.Where(kw => target.Contains(kw)).ToList();

                    var potentialMatches = markets.Where(m =>
                    {
                        string simM = Simplify(m.MarketName);
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
                    var hcSplit = matchName.Split(new[] { " vs ", " v ", " - " }, StringSplitOptions.None);
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
