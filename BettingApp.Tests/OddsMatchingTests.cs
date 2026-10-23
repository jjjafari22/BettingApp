using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using BettingApp.Services;
using BettingApp.Models;

namespace BettingApp.Tests
{
    public class OddsMatchingTests
    {
        private readonly MarketMappingService _marketMapper = new MarketMappingService();

        [Theory]
        [InlineData("Erling Haaland - Over 3.5", "Erling Braut Haaland - Over 3.5", true)]
        [InlineData("Erling Haaland - Over 3.5", "Erling Braut Haaland - Under 3.5", false)]
        [InlineData("Erling Haaland - Over 2.5", "Erling Braut Haaland - Over 3.5", false)]
        [InlineData("Janover Underhagen - Yes", "Janover Underhagen - Yes", true)]
        [InlineData("Janover Underhagen - Yes", "Janover Underhagen - No", false)]
        [InlineData("Janover Underhagen - Over 2.5", "Janover Underhagen - Under 2.5", false)]
        [InlineData("Bruno Fernandes", "Bruno Miguel Borges Fernandes", true)]
        [InlineData("Bruno Fernandes", "Bruno Silva", false)]
        [InlineData("Martin Ødegaard - To Provide An Assist", "Martin Ødegaard - Over 0.5", true)]
        [InlineData("Martin Ødegaard - To Provide An Assist", "Martin Ødegaard - Under 0.5", false)]
        [InlineData("Pio Esposito - Over 1.5 Shots On Target (Power Sub)", "Over 1.5 (Esposito, Francesco Pio)", true)]
        [InlineData("Pio Esposito - Over 1.5 Shots On Target (Power Sub)", "Over 1.5 (Francesco Pio Esposito)", true)]
        [InlineData("Pio Esposito 2+ (Power Sub)", "Over 1.5 (Esposito, Francesco Pio)", true)]
        [InlineData("F. Esposito - Over 1.5 Shots", "Over 1.5 (Esposito, Francesco Pio)", true)]
        [InlineData("Max Power - Over 1.5 Shots On Target", "Over 1.5 (Power, Max)", true)]
        [InlineData("Max Power - Over 1.5 Shots On Target (Power Sub)", "Over 1.5 (Power, Max)", true)]
        [InlineData("Niklas Castro - Over 1.5 Shots On Target (Power Sub)", "Over 1.5 (Castro, Niklas)", true)]
        [InlineData("Sao Paulo / Sao Paulo", "Cruzeiro / Sao Paulo", false)]
        [InlineData("Sao Paulo / Sao Paulo", "Sao Paulo / Sao Paulo", true)]
        [InlineData("Cruzeiro / Sao Paulo", "Sao Paulo / Cruzeiro", true)]
        [InlineData("John Smith", "John Paul Smith", true)]
        [InlineData("John Smith", "John", true)]
        public void Test_FuzzyPlayerMatch(string aiSelection, string opOutcome, bool expected)
        {
            bool result = OddsMatchingLogic.IsFuzzyPlayerMatch(aiSelection, opOutcome);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void Test_LoisOpenda_ParenthesesHandling()
        {
            // Verifies the fix where player names with "ois" and parentheses are properly handled
            string aiSelection = "Loïs Openda - Over 1.5";
            string opOutcome = "2+ (Openda, Lois)"; // OddsPapi format for Over 1.5 Player Shots
            
            var leg = new AiVisionLeg { Selection = aiSelection, Market = "Player Shots" };
            bool isMatch = OddsMatchingLogic.IsOutcomeMatch(opOutcome, opOutcome, leg, null, null);
            
            Assert.True(isMatch, "Loïs Openda Over 1.5 should match 2+ (Openda, Lois)");
        }

        [Fact]
        public void Test_TeamCorners_Norway()
        {
            // Verifies that a generic "Team Corners" market checks the selection name to find the team
            var targets = _marketMapper.NormalizeMarketName("Team Corners", "Portugal vs Norway", "Norway - Over 3.5");
            Assert.Contains("Corners - Over Under Team 2", targets);
            Assert.DoesNotContain("Corners - Over/Under Full Time", targets);
        }

        [Fact]
        public void Test_NumberOfCardsInMatch()
        {
            // Verifies that generic cards market falls back properly
            var targets = _marketMapper.NormalizeMarketName("Number of Cards in Match", "Portugal vs Norway", "Over 4.5");
            Assert.Contains("Bookings - Over Under Full Time", targets);
        }

        [Fact]
        public void Test_FindBestMarket_EndToEnd()
        {
            // Mock OddsPapi Result
            var mockResult = new OddsPapiSearchResult
            {
                MatchName = "France vs Belgium"
            };

            var mockMarket = new OddsPapiMarket
            {
                MarketId = "10743",
                MarketName = "Player Shots (incl. overtime)", // OddsPapi name
            };
            mockResult.Markets.Add(mockMarket);

            var bestMarket = OddsMatchingLogic.FindBestMarket(
                mockResult.Markets, 
                "Player Shots", 
                "France vs Belgium", 
                "Loïs Openda - Over 1.5", 
                _marketMapper);

            Assert.NotNull(bestMarket);
            Assert.Equal("10743", bestMarket.MarketId);
        }

        [Fact]
        public void Test_ToScoreOrAssist_DoesNotMatchPlayerGoalsOrAssists()
        {
            var mockMarkets = new List<OddsPapiMarket>
            {
                new OddsPapiMarket { MarketId = "100", MarketName = "Over Under Player Goals (incl. overtime)" },
                new OddsPapiMarket { MarketId = "101", MarketName = "Over Under Player Assists (incl. overtime)" }
            };

            var bestMarket = OddsMatchingLogic.FindBestMarket(
                mockMarkets,
                "To Score or Assist",
                "Liverpool vs Manchester City",
                "Erling Haaland - Yes (Power Sub)",
                _marketMapper);

            Assert.Null(bestMarket);
        }

        [Theory]
        [InlineData("To Score or Assist")]
        [InlineData("Score or Assist")]
        [InlineData("Goal or Assist")]
        [InlineData("To Score or Provide an Assist")]
        [InlineData("Score or Provide an Assist")]
        [InlineData("Anytime Goal or Assist")]
        [InlineData("Player Goal or Assist")]
        [InlineData("To Score / Assist")]
        public void Test_NormalizeMarketName_GoalOrAssistVariants_DoNotIncludeGoalsOrAssists(string marketName)
        {
            var targets = _marketMapper.NormalizeMarketName(marketName, "Liverpool vs Manchester City", "Erling Haaland - Yes");
            Assert.DoesNotContain("Over Under Player Goals (incl. overtime)", targets);
            Assert.DoesNotContain("Over Under Player Assists (incl. overtime)", targets);
            Assert.Contains(targets, t => t.Contains("Goal or Assist", StringComparison.OrdinalIgnoreCase) || t.Contains("Goals or Assists", StringComparison.OrdinalIgnoreCase) || t.Contains("Score or Assist", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Test_PureGoalAndPureAssist_StillMatchCorrectly()
        {
            var mockMarkets = new List<OddsPapiMarket>
            {
                new OddsPapiMarket { MarketId = "100", MarketName = "Over Under Player Goals (incl. overtime)" },
                new OddsPapiMarket { MarketId = "101", MarketName = "Over Under Player Assists (incl. overtime)" }
            };

            var goalMarket = OddsMatchingLogic.FindBestMarket(
                mockMarkets,
                "Anytime Goalscorer",
                "Liverpool vs Manchester City",
                "Erling Haaland",
                _marketMapper);
            Assert.NotNull(goalMarket);
            Assert.Equal("100", goalMarket.MarketId);

            var assistMarket = OddsMatchingLogic.FindBestMarket(
                mockMarkets,
                "Player Assists",
                "Liverpool vs Manchester City",
                "Kevin De Bruyne",
                _marketMapper);
            Assert.NotNull(assistMarket);
            Assert.Equal("101", assistMarket.MarketId);
        }

        [Fact]
        public void Test_Psg_Vs_ParisSaintGermain_Resolution()
        {
            var opResult = new OddsPapiSearchResult { MatchName = "Manchester City vs Paris Saint-Germain" };

            // PROD scenario: AI extracted "Manchester City vs Paris Saint-Germain", selection "PSG"
            var prodLeg = new AiVisionLeg { Match = "Manchester City vs Paris Saint-Germain", Selection = "PSG", Market = "Full Time Result" };
            string displayOName = OddsMatchingLogic.GetTranslatedOutcomeName("2", opResult.MatchName);
            bool prodMatch = OddsMatchingLogic.IsOutcomeMatch("2", displayOName, prodLeg, opResult, "Full Time Result");
            Assert.True(prodMatch, "PSG selection must match Outcome 2 (Paris Saint-Germain) when Match contains Paris Saint-Germain");

            // DEV scenario: AI extracted "Manchester City vs PSG", selection "PSG"
            var devLeg = new AiVisionLeg { Match = "Manchester City vs PSG", Selection = "PSG", Market = "Full Time Result" };
            bool devMatch = OddsMatchingLogic.IsOutcomeMatch("2", displayOName, devLeg, opResult, "Full Time Result");
            Assert.True(devMatch, "PSG selection must match Outcome 2 when Match contains PSG");

            // Double Chance: "PSG or Draw" -> X2
            var x2Leg = new AiVisionLeg { Match = "Manchester City vs Paris Saint-Germain", Selection = "PSG or Draw", Market = "Double Chance" };
            string x2Translated = OddsMatchingLogic.GetTranslatedOutcomeName("X2", opResult.MatchName);
            bool x2Match = OddsMatchingLogic.IsOutcomeMatch("X2", x2Translated, x2Leg, opResult, "Double Chance");
            Assert.True(x2Match, "PSG or Draw must match Outcome X2 (Paris Saint-Germain or Draw)");

            // Asian Handicap: "PSG (+1.5)" -> 2 (+1.5)
            var hcLeg = new AiVisionLeg { Match = "Manchester City vs Paris Saint-Germain", Selection = "PSG (+1.5)", Market = "Asian Handicap" };
            string hcTranslated = OddsMatchingLogic.GetTranslatedOutcomeName("2 (+1.5)", opResult.MatchName);
            bool hcMatch = OddsMatchingLogic.IsOutcomeMatch("2 (+1.5)", hcTranslated, hcLeg, opResult, "Asian Handicap");
            Assert.True(hcMatch, "PSG (+1.5) must match Outcome 2 (+1.5)");
        }

        [Fact]
        public void Test_Spurs_Vs_TottenhamHotspur_Resolution()
        {
            var opResult = new OddsPapiSearchResult { MatchName = "Tottenham Hotspur vs Arsenal" };
            var leg = new AiVisionLeg { Match = "Tottenham Hotspur vs Arsenal", Selection = "Spurs", Market = "Full Time Result" };
            string displayOName = OddsMatchingLogic.GetTranslatedOutcomeName("1", opResult.MatchName);
            bool isMatch = OddsMatchingLogic.IsOutcomeMatch("1", displayOName, leg, opResult, "Full Time Result");
            Assert.True(isMatch, "Spurs selection must match Outcome 1 (Tottenham Hotspur)");
        }

        [Fact]
        public void Test_TeamTotalGoals_FindBestMarket_Psg()
        {
            var mockMarkets = new List<OddsPapiMarket>
            {
                new OddsPapiMarket { MarketId = "200", MarketName = "Over Under Team 1" },
                new OddsPapiMarket { MarketId = "201", MarketName = "Over Under Team 2" }
            };

            var bestMarket = OddsMatchingLogic.FindBestMarket(
                mockMarkets,
                "Team Total Goals",
                "Manchester City vs Paris Saint-Germain",
                "PSG Over 1.5 Goals",
                _marketMapper);

            Assert.NotNull(bestMarket);
            Assert.Equal("201", bestMarket.MarketId);
        }

        [Fact]
        public void Test_Bet940_TotalGoalkeeperSaves_DoesNotMatchPlayerProps()
        {
            var opMarkets = new List<OddsPapiMarket>
            {
                new OddsPapiMarket { MarketId = "100", MarketName = "Over Under Full Time" },
                new OddsPapiMarket { MarketId = "101", MarketName = "Over Under Team 1" },
                new OddsPapiMarket { MarketId = "102", MarketName = "Over Under Team 2" },
                new OddsPapiMarket { MarketId = "103", MarketName = "Over Under Player Goalkeeper Saves (incl. overtime)" }
            };

            // Leg 1: Total Goalkeeper Saves - Roma
            var m1 = OddsMatchingLogic.FindBestMarket(opMarkets, "Total Goalkeeper Saves - Roma", "Roma vs Real Madrid", "Over 5.5", _marketMapper);
            Assert.Null(m1);

            // Leg 5: Total Goalkeeper Saves
            var m5 = OddsMatchingLogic.FindBestMarket(opMarkets, "Total Goalkeeper Saves", "Roma vs Real Madrid", "Over 7.5", _marketMapper);
            Assert.Null(m5);

            // Leg 6: Total Goalkeeper Saves - Real Madrid
            var m6 = OddsMatchingLogic.FindBestMarket(opMarkets, "Total Goalkeeper Saves - Real Madrid", "Roma vs Real Madrid", "Over 2.5", _marketMapper);
            Assert.Null(m6);
        }

        [Fact]
        public void Test_Bet940_TotalFouls_DoesNotMatchPlayerProps()
        {
            var opMarkets = new List<OddsPapiMarket>
            {
                new OddsPapiMarket { MarketId = "100", MarketName = "Over Under Full Time" },
                new OddsPapiMarket { MarketId = "101", MarketName = "Over Under Team 1" },
                new OddsPapiMarket { MarketId = "102", MarketName = "Over Under Team 2" },
                new OddsPapiMarket { MarketId = "104", MarketName = "Over Under Player Fouls Committed (incl. overtime)" }
            };

            // Leg 2: Total Fouls
            var m2 = OddsMatchingLogic.FindBestMarket(opMarkets, "Total Fouls", "Roma vs Real Madrid", "Over 29.5", _marketMapper);
            Assert.Null(m2);

            // Leg 3: Total Fouls - Roma
            var m3 = OddsMatchingLogic.FindBestMarket(opMarkets, "Total Fouls - Roma", "Roma vs Real Madrid", "Over 14.5", _marketMapper);
            Assert.Null(m3);

            // Leg 4: Total Fouls - Real Madrid
            var m4 = OddsMatchingLogic.FindBestMarket(opMarkets, "Total Fouls - Real Madrid", "Roma vs Real Madrid", "Over 13.5", _marketMapper);
            Assert.Null(m4);
        }

        [Fact]
        public void Test_PlayerProps_SavesAndFouls_StillMatch()
        {
            var opMarkets = new List<OddsPapiMarket>
            {
                new OddsPapiMarket { MarketId = "103", MarketName = "Over Under Player Goalkeeper Saves (incl. overtime)" },
                new OddsPapiMarket { MarketId = "104", MarketName = "Over Under Player Fouls Committed (incl. overtime)" }
            };

            // Player Saves
            var mp1 = OddsMatchingLogic.FindBestMarket(opMarkets, "Player Goalkeeper Saves", "Roma vs Real Madrid", "Mile Svilar Over 2.5", _marketMapper);
            Assert.NotNull(mp1);
            Assert.Equal("103", mp1.MarketId);

            var mp2 = OddsMatchingLogic.FindBestMarket(opMarkets, "Mile Svilar - Goalkeeper Saves", "Roma vs Real Madrid", "Over 2.5", _marketMapper);
            Assert.NotNull(mp2);
            Assert.Equal("103", mp2.MarketId);

            // Player Fouls
            var mp3 = OddsMatchingLogic.FindBestMarket(opMarkets, "Player Fouls Committed", "Roma vs Real Madrid", "Over 1.5", _marketMapper);
            Assert.NotNull(mp3);
            Assert.Equal("104", mp3.MarketId);
        }

        [Fact]
        public void Test_IsOutcomeMatch_DoesNotFalselyMatchOverUnderToNamedPlayer()
        {
            var leg = new AiVisionLeg
            {
                Match = "Roma vs Real Madrid",
                Market = "Total Goalkeeper Saves - Roma",
                Selection = "Over 5.5"
            };

            var searchResult = new OddsPapiSearchResult
            {
                MatchName = "Roma vs Real Madrid"
            };

            // Svilar has line Over 5.5 in OP outcome, but leg selection is just "Over 5.5" (no player specified)
            bool isMatch = OddsMatchingLogic.IsOutcomeMatch(
                "Over 5.5 (Svilar, Mile)",
                "Over 5.5 (Svilar, Mile)",
                leg,
                searchResult,
                "Over Under Player Goalkeeper Saves (incl. overtime)");

            Assert.False(isMatch);
        }

        [Fact]
        public void Test_BothTeamsToScoreAndOverGoals_DoesNotMatchBttsOrOverUnder()
        {
            // Scenario reported by user:
            // Feyenoord vs Como - Both Teams To Score & Over 2.5 Goals (Selection: Yes)
            // Must NOT match "Both Teams To Score" or "Over Under Full Time"
            var mockMarkets = new List<OddsPapiMarket>
            {
                new OddsPapiMarket { MarketId = "1", MarketName = "Full Time Result" },
                new OddsPapiMarket { MarketId = "2", MarketName = "Both Teams To Score" },
                new OddsPapiMarket { MarketId = "3", MarketName = "Over Under Full Time" }
            };

            var normalizedTargets = _marketMapper.NormalizeMarketName(
                "Both Teams To Score & Over 2.5 Goals", 
                "Feyenoord vs Como", 
                "Yes");

            Assert.DoesNotContain("Both Teams To Score", normalizedTargets);

            var bestMarket = OddsMatchingLogic.FindBestMarket(
                mockMarkets,
                "Both Teams To Score & Over 2.5 Goals",
                "Feyenoord vs Como",
                "Yes",
                _marketMapper);

            Assert.Null(bestMarket);
        }

        [Fact]
        public void Test_PureBothTeamsToScore_MatchesCorrectly()
        {
            var mockMarkets = new List<OddsPapiMarket>
            {
                new OddsPapiMarket { MarketId = "1", MarketName = "Full Time Result" },
                new OddsPapiMarket { MarketId = "2", MarketName = "Both Teams To Score" },
                new OddsPapiMarket { MarketId = "3", MarketName = "Both Teams To Score - First Half" }
            };

            // Pure FT BTTS
            var mFt = OddsMatchingLogic.FindBestMarket(
                mockMarkets,
                "Both Teams To Score",
                "Feyenoord vs Como",
                "Yes",
                _marketMapper);
            Assert.NotNull(mFt);
            Assert.Equal("2", mFt.MarketId);

            // BTTS alias
            var mBtts = OddsMatchingLogic.FindBestMarket(
                mockMarkets,
                "BTTS",
                "Feyenoord vs Como",
                "Yes",
                _marketMapper);
            Assert.NotNull(mBtts);
            Assert.Equal("2", mBtts.MarketId);

            // First Half BTTS
            var m1H = OddsMatchingLogic.FindBestMarket(
                mockMarkets,
                "1st Half Both Teams To Score",
                "Feyenoord vs Como",
                "Yes",
                _marketMapper);
            Assert.NotNull(m1H);
            Assert.Equal("3", m1H.MarketId);
        }

        [Theory]
        [InlineData("Both Teams To Score & Over 2.5 Goals", true)]
        [InlineData("Both Teams To Score & Over 3.5 Goals", true)]
        [InlineData("Both Teams to Score and Over/Under 2.5 Goals", true)]
        [InlineData("Both to Score and Total Goals", true)]
        [InlineData("Match Result and Both Teams to Score", true)]
        [InlineData("1x2 and Both to Score", true)]
        [InlineData("Double Chance and Both Teams to Score", true)]
        [InlineData("Lyn to win and both teams to score", true)]
        [InlineData("Over 2.5 Match Total Goals And Both Teams To Score", true)]
        [InlineData("Bet Builder (Match Result (1X2) + Player 1+ Goals or Assists)", true)]
        [InlineData("Bet Builder (Total Goals 2.5 & Double Chance)", true)]
        [InlineData("Match Result & Thun Total Corners", true)]
        [InlineData("Match Result & Total Goals 2.5", true)]
        [InlineData("Handicap (3 Way) -1 & Total Corners 5.5", true)]
        [InlineData("Result / Both Teams to Score", true)]
        [InlineData("Both Teams To Score", false)]
        [InlineData("Both to Score", false)]
        [InlineData("BTTS", false)]
        [InlineData("Both Teams To Score - First Half", false)]
        [InlineData("1st Half Both Teams To Score", false)]
        [InlineData("Both Teams To Score - Second Half", false)]
        [InlineData("Both Teams To Score - Full Time", false)]
        [InlineData("Both Teams To Score (BTTS)", false)]
        [InlineData("Both Teams to Score: Yes/No", false)]
        [InlineData("Brighton & Hove Albion Total Goals", false)]
        [InlineData("Player 1+ Shots", false)]
        [InlineData("Player 2+ Fouls", false)]
        [InlineData("Handicap (+0.5)", false)]
        [InlineData("Total Goals", false)]
        public void Test_IsCombinationMarket(string marketName, bool expectedIsCombo)
        {
            string matchName = marketName.Contains("Brighton") ? "Brighton & Hove Albion vs Chelsea" : "Feyenoord vs Como";
            bool isCombo = MarketMappingService.IsCombinationMarket(marketName, matchName);
            Assert.Equal(expectedIsCombo, isCombo);
        }

        [Theory]
        [InlineData("Both Teams To Score & Over 2.5 Goals")]
        [InlineData("Both Teams To Score & Over 3.5 Goals")]
        [InlineData("Both Teams to Score and Over/Under 2.5 Goals")]
        [InlineData("Both to Score and Total Goals")]
        [InlineData("Match Result and Both Teams to Score")]
        [InlineData("1x2 and Both to Score")]
        [InlineData("Double Chance and Both Teams to Score")]
        [InlineData("Lyn to win and both teams to score")]
        [InlineData("Over 2.5 Match Total Goals And Both Teams To Score")]
        [InlineData("Match Result & Thun Total Corners")]
        [InlineData("Match Result & Total Goals 2.5")]
        [InlineData("Bet Builder (Total Goals 2.5 & Double Chance)")]
        [InlineData("Bet Builder (Match Result (1X2) + Player 1+ Goals or Assists)")]
        [InlineData("Handicap (3 Way) -1 & Total Corners 5.5")]
        public void Test_CombinationMarkets_DoNotMatchSingleMarkets(string marketName)
        {
            var mockMarkets = new List<OddsPapiMarket>
            {
                new OddsPapiMarket { MarketId = "1", MarketName = "Full Time Result" },
                new OddsPapiMarket { MarketId = "2", MarketName = "Both Teams To Score" },
                new OddsPapiMarket { MarketId = "3", MarketName = "Over Under Full Time" },
                new OddsPapiMarket { MarketId = "4", MarketName = "Double Chance Full Time" },
                new OddsPapiMarket { MarketId = "5", MarketName = "Corners - Over/Under Full Time" },
                new OddsPapiMarket { MarketId = "6", MarketName = "3-Way Handicap" }
            };

            var bestMarket = OddsMatchingLogic.FindBestMarket(
                mockMarkets,
                marketName,
                "Feyenoord vs Como",
                "Yes",
                _marketMapper);

            Assert.Null(bestMarket);
        }

        [Fact]
        public void Test_MostCorners_MatchesCorners1X2_NotOverUnderTeam()
        {
            // Scenario reported by user:
            // Lyon vs Crystal Palace - Most Corners (Selection: Lyon)
            // Must match "Corners - 1X2", NOT "Corners - Over Under Team 1"
            var mockMarkets = new List<OddsPapiMarket>
            {
                new OddsPapiMarket { MarketId = "10", MarketName = "Corners - Over/Under Full Time" },
                new OddsPapiMarket { MarketId = "11", MarketName = "Corners - Over Under Team 1" },
                new OddsPapiMarket { MarketId = "12", MarketName = "Corners - Over Under Team 2" },
                new OddsPapiMarket { MarketId = "13", MarketName = "Corners - 1X2" }
            };

            var normalizedTargets = _marketMapper.NormalizeMarketName("Most Corners", "Lyon vs Crystal Palace", "Lyon");
            Assert.Contains("Corners - 1X2", normalizedTargets);
            Assert.DoesNotContain("Corners - Over Under Team 1", normalizedTargets);

            var bestMarket = OddsMatchingLogic.FindBestMarket(
                mockMarkets,
                "Most Corners",
                "Lyon vs Crystal Palace",
                "Lyon",
                _marketMapper);

            Assert.NotNull(bestMarket);
            Assert.Equal("13", bestMarket.MarketId);
            Assert.Equal("Corners - 1X2", bestMarket.MarketName);

            // Verify outcome matching: Selection "Lyon" matches Outcome "1"
            var leg = new AiVisionLeg
            {
                Match = "Lyon vs Crystal Palace",
                Market = "Most Corners",
                Selection = "Lyon"
            };
            var searchResult = new OddsPapiSearchResult
            {
                MatchName = "Lyon vs Crystal Palace"
            };
            string displayOName = OddsMatchingLogic.GetTranslatedOutcomeName("1", searchResult.MatchName);
            bool isMatch = OddsMatchingLogic.IsOutcomeMatch("1", displayOName, leg, searchResult, "Corners - 1X2");
            Assert.True(isMatch, "Outcome 1 should match Lyon for Most Corners");
        }

        [Theory]
        [InlineData("Moneyline (incl. overtime and penalties)", "Toronto Maple Leafs", false)]
        [InlineData("Moneyline", "Toronto Maple Leafs to win including overtime and penalties", false)]
        [InlineData("Moneyline", "Sabres to win including overtime and penalties", false)]
        [InlineData("Moneyline", "Buffalo Sabres (incl. overtime and shootout)", false)]
        [InlineData("Moneyline", "Toronto Maple Leafs to win incl. OT & SO", false)]
        [InlineData("Match Result and Total Goals", "Toronto Maple Leafs and Over 5.5", true)]
        [InlineData("Winner and Total Goals", "Sabres to win including overtime and penalties and Over 5.5", true)]
        public void Test_Hockey_OvertimeAndCombinationMarkets(string market, string selection, bool expectedIsCombination)
        {
            string match = "Toronto Maple Leafs vs Buffalo Sabres";
            bool isCombo = MarketMappingService.IsCombinationMarket(market, match) || MarketMappingService.IsCombinationMarket(selection, match);
            Assert.Equal(expectedIsCombination, isCombo);
        }

        [Fact]
        public void Test_Hockey_MoneylineOutcomeMatching()
        {
            var searchResult = new OddsPapiSearchResult
            {
                MatchName = "Toronto Maple Leafs vs Buffalo Sabres"
            };

            // Buffalo Sabres with "Sabres to win including overtime and penalties"
            var sabresLeg = new AiVisionLeg
            {
                Match = "Toronto Maple Leafs vs Buffalo Sabres",
                Market = "Moneyline",
                Selection = "Sabres to win including overtime and penalties"
            };
            string sabresDisplay = OddsMatchingLogic.GetTranslatedOutcomeName("2", searchResult.MatchName);
            bool sabresMatch = OddsMatchingLogic.IsOutcomeMatch("2", sabresDisplay, sabresLeg, searchResult, "Winner (incl. overtime and penalties)");
            Assert.True(sabresMatch, "Sabres selection should match outcome 2");

            // Toronto Maple Leafs with "Toronto Maple Leafs to win incl. OT & SO"
            var leafsLeg = new AiVisionLeg
            {
                Match = "Toronto Maple Leafs vs Buffalo Sabres",
                Market = "Moneyline",
                Selection = "Toronto Maple Leafs to win incl. OT & SO"
            };
            string leafsDisplay = OddsMatchingLogic.GetTranslatedOutcomeName("1", searchResult.MatchName);
            bool leafsMatch = OddsMatchingLogic.IsOutcomeMatch("1", leafsDisplay, leafsLeg, searchResult, "Winner (incl. overtime and penalties)");
            Assert.True(leafsMatch, "Leafs selection should match outcome 1");
        }

        [Fact]
        public void Test_Hockey_PlayerProp_MultiGoalDistinction()
        {
            var searchResult = new OddsPapiSearchResult
            {
                MatchName = "Toronto Maple Leafs vs Buffalo Sabres"
            };

            var leg2OrMore = new AiVisionLeg
            {
                Match = "Toronto Maple Leafs vs Buffalo Sabres",
                Market = "To Score 2 or More Goals - Including Overtime",
                Selection = "Tage Thompson - Yes"
            };

            bool matchOver05 = OddsMatchingLogic.IsOutcomeMatch("Over 0.5 (Thompson, Tage)", "Over 0.5 (Thompson, Tage)", leg2OrMore, searchResult, "Over Under Player Goals (incl. overtime)");
            bool matchOver15 = OddsMatchingLogic.IsOutcomeMatch("Over 1.5 (Thompson, Tage)", "Over 1.5 (Thompson, Tage)", leg2OrMore, searchResult, "Over Under Player Goals (incl. overtime)");

            Assert.False(matchOver05, "To Score 2 or More Goals must NOT match Over 0.5");
            Assert.True(matchOver15, "To Score 2 or More Goals MUST match Over 1.5");
        }

        [Theory]
        [InlineData("Match Odds - Regular Time")]
        [InlineData("Match Odds (Regular Time)")]
        [InlineData("Match Result - Regular Time")]
        [InlineData("1X2 - Regular Time")]
        [InlineData("3-Way - Regular Time")]
        [InlineData("Regular Time Result")]
        public void Test_Hockey_RegularTimeResult_MarketAndOutcomeMatching(string marketName)
        {
            var targets = _marketMapper.NormalizeMarketName(marketName, "San Diego Gulls vs Ontario Reign", "Draw");
            Assert.Contains("Regular Time Result", targets);

            var markets = new List<OddsPapiMarket>
            {
                new OddsPapiMarket
                {
                    MarketId = "Regular Time Result",
                    MarketName = "Regular Time Result",
                    OutcomeNames = new Dictionary<string, string>
                    {
                        { "153", "1" },
                        { "154", "X" },
                        { "155", "2" }
                    }
                }
            };

            var best = OddsMatchingLogic.FindBestMarket(markets, marketName, "San Diego Gulls vs Ontario Reign", "Draw", _marketMapper);
            Assert.NotNull(best);
            Assert.Equal("Regular Time Result", best.MarketName);

            var searchResult = new OddsPapiSearchResult
            {
                MatchName = "San Diego Gulls vs Ontario Reign"
            };

            var leg = new AiVisionLeg
            {
                Match = "San Diego Gulls vs Ontario Reign",
                Market = marketName,
                Selection = "Draw"
            };

            string displayOName = OddsMatchingLogic.GetTranslatedOutcomeName("X", searchResult.MatchName);
            bool isMatch = OddsMatchingLogic.IsOutcomeMatch("X", displayOName, leg, searchResult, "Regular Time Result");
            Assert.True(isMatch, "Draw selection should match Outcome X");
        }

        [Fact]
        public void Test_AcSpartaPraha_Matches_SpartaPrague()
        {
            var searchResult = new OddsPapiSearchResult
            {
                MatchName = "Sparta Prague vs Lillestroem SK"
            };

            var leg = new AiVisionLeg
            {
                Match = "Sparta Prague vs Lillestrom",
                Market = "Match Result (1X2)",
                Selection = "AC Sparta Praha"
            };

            string displayOName1 = OddsMatchingLogic.GetTranslatedOutcomeName("1", searchResult.MatchName);
            bool isMatch1 = OddsMatchingLogic.IsOutcomeMatch("1", displayOName1, leg, searchResult, "Full Time Result");
            Assert.True(isMatch1, "AC Sparta Praha should match 1 (Sparta Prague)");

            string displayOName2 = OddsMatchingLogic.GetTranslatedOutcomeName("2", searchResult.MatchName);
            bool isMatch2 = OddsMatchingLogic.IsOutcomeMatch("2", displayOName2, leg, searchResult, "Full Time Result");
            Assert.False(isMatch2, "AC Sparta Praha should not match 2 (Lillestroem SK)");

            string displayONameX = OddsMatchingLogic.GetTranslatedOutcomeName("X", searchResult.MatchName);
            bool isMatchX = OddsMatchingLogic.IsOutcomeMatch("X", displayONameX, leg, searchResult, "Full Time Result");
            Assert.False(isMatchX, "AC Sparta Praha should not match X (Draw)");
        }

        [Fact]
        public void Test_LillestroemSK_Matches_Lillestrom()
        {
            var searchResult = new OddsPapiSearchResult
            {
                MatchName = "Sparta Prague vs Lillestroem SK"
            };

            var leg = new AiVisionLeg
            {
                Match = "Sparta Prague vs Lillestrom",
                Market = "Match Result (1X2)",
                Selection = "Lillestrom"
            };

            string displayOName2 = OddsMatchingLogic.GetTranslatedOutcomeName("2", searchResult.MatchName);
            bool isMatch2 = OddsMatchingLogic.IsOutcomeMatch("2", displayOName2, leg, searchResult, "Full Time Result");
            Assert.True(isMatch2, "Lillestrom should match 2 (Lillestroem SK)");
        }

        [Fact]
        public void Test_CeltaVigo_Juventus_DoubleChance()
        {
            var searchResult = new OddsPapiSearchResult
            {
                MatchName = "RC Celta de Vigo vs Juventus Turin"
            };

            var leg = new AiVisionLeg
            {
                Match = "Celta Vigo vs Juventus",
                Market = "Double Chance",
                Selection = "Juventus or Draw"
            };

            string display12 = OddsMatchingLogic.GetTranslatedOutcomeName("12", searchResult.MatchName);
            bool match12 = OddsMatchingLogic.IsOutcomeMatch("12", display12, leg, searchResult, "Double Chance Full Time");
            Assert.False(match12);

            string display1X = OddsMatchingLogic.GetTranslatedOutcomeName("1X", searchResult.MatchName);
            bool match1X = OddsMatchingLogic.IsOutcomeMatch("1X", display1X, leg, searchResult, "Double Chance Full Time");
            Assert.False(match1X);

            string display2X = OddsMatchingLogic.GetTranslatedOutcomeName("2X", searchResult.MatchName);
            bool match2X = OddsMatchingLogic.IsOutcomeMatch("2X", display2X, leg, searchResult, "Double Chance Full Time");
            Assert.True(match2X, "Juventus or Draw should match Outcome 2X (Juventus Turin or Draw)");
        }

        [Fact]
        public void Test_HajdukSplit_Vs_Ajax_CornersHandicap()
        {
            var searchResult = new OddsPapiSearchResult
            {
                MatchName = "HNK Hajduk Split vs Ajax Amsterdam"
            };

            var leg = new AiVisionLeg
            {
                Match = "Hajduk Split vs AFC Ajax",
                Market = "Corners Handicap (2 Way) 0 - 0.5",
                Selection = "AFC Ajax +0.5"
            };

            // In OddsPapi 2-way handicap, 2 (-0.5) is Away Team +0.5
            string display2Minus05 = OddsMatchingLogic.GetTranslatedOutcomeName("2 (-0.5)", searchResult.MatchName);
            Assert.Equal("Ajax Amsterdam +0.5", display2Minus05);
            bool matchCorrect = OddsMatchingLogic.IsOutcomeMatch("2 (-0.5)", display2Minus05, leg, searchResult, "Corners - Handicap");
            Assert.True(matchCorrect, "AFC Ajax +0.5 must match 2 (-0.5) (Ajax Amsterdam +0.5)");

            // 2 (0.5) is Away Team -0.5 and should NOT match
            string display2Plus05 = OddsMatchingLogic.GetTranslatedOutcomeName("2 (0.5)", searchResult.MatchName);
            Assert.Equal("Ajax Amsterdam -0.5", display2Plus05);
            bool matchWrong = OddsMatchingLogic.IsOutcomeMatch("2 (0.5)", display2Plus05, leg, searchResult, "Corners - Handicap");
            Assert.False(matchWrong, "AFC Ajax +0.5 must not match 2 (0.5) (Ajax Amsterdam -0.5)");

            // Home outcomes should not match
            string display1Minus05 = OddsMatchingLogic.GetTranslatedOutcomeName("1 (-0.5)", searchResult.MatchName);
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("1 (-0.5)", display1Minus05, leg, searchResult, "Corners - Handicap"));

            string display1Plus05 = OddsMatchingLogic.GetTranslatedOutcomeName("1 (0.5)", searchResult.MatchName);
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("1 (0.5)", display1Plus05, leg, searchResult, "Corners - Handicap"));
        }

        [Fact]
        public void Test_Torreense_Vs_Sunderland_CornersHandicap()
        {
            var searchResult = new OddsPapiSearchResult
            {
                MatchName = "SCU Torreense vs Sunderland AFC"
            };

            var leg = new AiVisionLeg
            {
                Match = "SC União Torreense vs Sunderland",
                Market = "Corners Handicap (2 Way) 4.5 - 0",
                Selection = "Sunderland -4.5"
            };

            // In OddsPapi 2-way handicap with baseline 4.5, 2 (4.5) is Away Team -4.5
            string display2 = OddsMatchingLogic.GetTranslatedOutcomeName("2 (4.5)", searchResult.MatchName);
            Assert.Equal("Sunderland AFC -4.5", display2);
            bool match2 = OddsMatchingLogic.IsOutcomeMatch("2 (4.5)", display2, leg, searchResult, "Corners - Handicap");
            Assert.True(match2, "Sunderland -4.5 must match 2 (4.5) (Sunderland AFC -4.5)");

            // Home 1 (4.5) is SCU Torreense +4.5
            string display1 = OddsMatchingLogic.GetTranslatedOutcomeName("1 (4.5)", searchResult.MatchName);
            Assert.Equal("SCU Torreense +4.5", display1);
            bool match1 = OddsMatchingLogic.IsOutcomeMatch("1 (4.5)", display1, leg, searchResult, "Corners - Handicap");
            Assert.False(match1, "Sunderland -4.5 must not match 1 (4.5) (SCU Torreense +4.5)");
        }

        [Fact]
        public void Test_HandicapSignConflict_OppositeSignsDoNotMatch()
        {
            var searchResult = new OddsPapiSearchResult
            {
                MatchName = "Hajduk Split vs AFC Ajax"
            };

            var legMinus = new AiVisionLeg
            {
                Match = "Hajduk Split vs AFC Ajax",
                Market = "Corners Handicap",
                Selection = "Hajduk Split -0.5"
            };

            string displayPlus = OddsMatchingLogic.GetTranslatedOutcomeName("1 (0.5)", searchResult.MatchName);
            Assert.Equal("Hajduk Split +0.5", displayPlus);
            bool matchConflict = OddsMatchingLogic.IsOutcomeMatch("1 (0.5)", displayPlus, legMinus, searchResult, "Corners - Handicap");
            Assert.False(matchConflict, "Hajduk Split -0.5 must not match Hajduk Split +0.5");

            var legPlus = new AiVisionLeg
            {
                Match = "Hajduk Split vs AFC Ajax",
                Market = "Corners Handicap",
                Selection = "Hajduk Split +0.5"
            };
            bool matchSame = OddsMatchingLogic.IsOutcomeMatch("1 (0.5)", displayPlus, legPlus, searchResult, "Corners - Handicap");
            Assert.True(matchSame, "Hajduk Split +0.5 must match Hajduk Split +0.5");
        }

        [Fact]
        public void Test_Hockey_MoneylineInclOvertimeAndShootout()
        {
            var targets = _marketMapper.NormalizeMarketName("Moneyline (Incl. Overtime and Shootout)", "Ottawa Senators vs New York Islanders", "Ottawa Senators");
            Assert.Contains("Winner (incl. overtime and penalties)", targets);

            var markets = new List<OddsPapiMarket>
            {
                new OddsPapiMarket
                {
                    MarketId = "Winner (incl. overtime and penalties)",
                    MarketName = "Winner (incl. overtime and penalties)",
                    OutcomeNames = new Dictionary<string, string> { { "151", "1" }, { "152", "2" } }
                }
            };

            var best = OddsMatchingLogic.FindBestMarket(markets, "Moneyline (Incl. Overtime and Shootout)", "Ottawa Senators vs New York Islanders", "Ottawa Senators", _marketMapper);
            Assert.NotNull(best);
            Assert.Equal("Winner (incl. overtime and penalties)", best.MarketName);

            var searchResult = new OddsPapiSearchResult
            {
                MatchName = "Ottawa Senators vs New York Islanders"
            };

            var leg = new AiVisionLeg
            {
                Match = "Ottawa Senators vs New York Islanders",
                Market = "Moneyline (Incl. Overtime and Shootout)",
                Selection = "Ottawa Senators"
            };

            string display1 = OddsMatchingLogic.GetTranslatedOutcomeName("1", searchResult.MatchName);
            Assert.Equal("Ottawa Senators", display1);
            Assert.True(OddsMatchingLogic.IsOutcomeMatch("1", display1, leg, searchResult, best.MarketName));
        }

        [Fact]
        public void Test_Hockey_PlayerToRecordAtLeast1Point()
        {
            var targets = _marketMapper.NormalizeMarketName("Player to Record at Least 1 Point (Including Overtime)", "Toronto Maple Leafs vs Buffalo Sabres", "Auston Matthews - Yes");
            Assert.Contains("Over Under Player Points (incl. overtime)", targets);

            var markets = new List<OddsPapiMarket>
            {
                new OddsPapiMarket
                {
                    MarketId = "Over Under Player Points (incl. overtime)",
                    MarketName = "Over Under Player Points (incl. overtime)",
                    OutcomeNames = new Dictionary<string, string>
                    {
                        { "1", "Over 0.5 (Matthews, Auston)" },
                        { "2", "Over 1.5 (Matthews, Auston)" }
                    }
                }
            };

            var best = OddsMatchingLogic.FindBestMarket(markets, "Player to Record at Least 1 Point (Including Overtime)", "Toronto Maple Leafs vs Buffalo Sabres", "Auston Matthews - Yes", _marketMapper);
            Assert.NotNull(best);
            Assert.Equal("Over Under Player Points (incl. overtime)", best.MarketName);

            var searchResult = new OddsPapiSearchResult
            {
                MatchName = "Toronto Maple Leafs vs Buffalo Sabres"
            };

            var leg = new AiVisionLeg
            {
                Match = "Toronto Maple Leafs vs Buffalo Sabres",
                Market = "Player to Record at Least 1 Point (Including Overtime)",
                Selection = "Auston Matthews - Yes"
            };

            Assert.True(OddsMatchingLogic.IsOutcomeMatch("Over 0.5 (Matthews, Auston)", "Over 0.5 (Matthews, Auston)", leg, searchResult, best.MarketName));
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("Over 1.5 (Matthews, Auston)", "Over 1.5 (Matthews, Auston)", leg, searchResult, best.MarketName));
        }

        [Fact]
        public void Test_Hockey_PlayerToScoreAtLeast2Points_MatchesOver15NotOver05()
        {
            var targets = _marketMapper.NormalizeMarketName("Player to Score at Least 2 Points - Including Overtime", "Detroit Red Wings vs Philadelphia Flyers", "Alex DeBrincat - Yes");
            Assert.Contains("Over Under Player Points (incl. overtime)", targets);
            Assert.DoesNotContain("Over Under Player Goals (incl. overtime)", targets);

            var markets = new List<OddsPapiMarket>
            {
                new OddsPapiMarket
                {
                    MarketId = "Over Under Player Points (incl. overtime)",
                    MarketName = "Over Under Player Points (incl. overtime)",
                    OutcomeNames = new Dictionary<string, string>
                    {
                        { "1", "Over 0.5 (DeBrincat, Alex)" },
                        { "2", "Over 1.5 (DeBrincat, Alex)" },
                        { "3", "Over 2.5 (DeBrincat, Alex)" }
                    }
                }
            };

            var best = OddsMatchingLogic.FindBestMarket(markets, "Player to Score at Least 2 Points - Including Overtime", "Detroit Red Wings vs Philadelphia Flyers", "Alex DeBrincat - Yes", _marketMapper);
            Assert.NotNull(best);
            Assert.Equal("Over Under Player Points (incl. overtime)", best.MarketName);

            var searchResult = new OddsPapiSearchResult
            {
                MatchName = "Detroit Red Wings vs Philadelphia Flyers"
            };

            var leg = new AiVisionLeg
            {
                Match = "Detroit Red Wings vs Philadelphia Flyers",
                Market = "Player to Score at Least 2 Points - Including Overtime",
                Selection = "Alex DeBrincat - Yes"
            };

            Assert.False(OddsMatchingLogic.IsOutcomeMatch("Over 0.5 (DeBrincat, Alex)", "Over 0.5 (DeBrincat, Alex)", leg, searchResult, best.MarketName));
            Assert.True(OddsMatchingLogic.IsOutcomeMatch("Over 1.5 (DeBrincat, Alex)", "Over 1.5 (DeBrincat, Alex)", leg, searchResult, best.MarketName));
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("Over 2.5 (DeBrincat, Alex)", "Over 2.5 (DeBrincat, Alex)", leg, searchResult, best.MarketName));
        }

        [Fact]
        public void Test_Hockey_MarkStone_2PlusPoints_Matches_Over1_5()
        {
            var searchResult = new OddsPapiSearchResult { MatchName = "Vegas Golden Knights vs Calgary Flames" };
            var leg = new AiVisionLeg
            {
                Match = "Vegas Golden Knights vs Calgary Flames",
                Market = "Player to Record 2+ Points (Incl. Overtime)",
                Selection = "Mark Stone - Yes"
            };

            string marketName = "Over Under Player Points (incl. overtime)";

            Assert.False(OddsMatchingLogic.IsOutcomeMatch("Over 0.5 (Stone, Mark)", "Over 0.5 (Stone, Mark)", leg, searchResult, marketName));
            Assert.True(OddsMatchingLogic.IsOutcomeMatch("Over 1.5 (Stone, Mark)", "Over 1.5 (Stone, Mark)", leg, searchResult, marketName));
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("Over 2.5 (Stone, Mark)", "Over 2.5 (Stone, Mark)", leg, searchResult, marketName));
        }

        [Fact]
        public void Test_Hockey_TomasHertl_2PlusGoals_Matches_Over1_5()
        {
            var searchResult = new OddsPapiSearchResult { MatchName = "Vegas Golden Knights vs Calgary Flames" };
            var leg = new AiVisionLeg
            {
                Match = "Vegas Golden Knights vs Calgary Flames",
                Market = "To Score 2+ Goals (Incl. Overtime)",
                Selection = "Tomas Hertl - Yes"
            };

            string marketName = "Over Under Player Goals (incl. overtime)";

            Assert.False(OddsMatchingLogic.IsOutcomeMatch("Over 0.5 (Hertl, Tomas)", "Over 0.5 (Hertl, Tomas)", leg, searchResult, marketName));
            Assert.True(OddsMatchingLogic.IsOutcomeMatch("Over 1.5 (Hertl, Tomas)", "Over 1.5 (Hertl, Tomas)", leg, searchResult, marketName));
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("Over 2.5 (Hertl, Tomas)", "Over 2.5 (Hertl, Tomas)", leg, searchResult, marketName));
        }

        [Fact]
        public void Test_Hockey_TomasHertl_3PlusGoals_Matches_Over2_5()
        {
            var searchResult = new OddsPapiSearchResult { MatchName = "Vegas Golden Knights vs Calgary Flames" };
            var leg = new AiVisionLeg
            {
                Match = "Vegas Golden Knights vs Calgary Flames",
                Market = "To Score 3+ Goals (Incl. Overtime)",
                Selection = "Tomas Hertl - Yes"
            };

            string marketName = "Over Under Player Goals (incl. overtime)";

            Assert.False(OddsMatchingLogic.IsOutcomeMatch("Over 0.5 (Hertl, Tomas)", "Over 0.5 (Hertl, Tomas)", leg, searchResult, marketName));
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("Over 1.5 (Hertl, Tomas)", "Over 1.5 (Hertl, Tomas)", leg, searchResult, marketName));
            Assert.True(OddsMatchingLogic.IsOutcomeMatch("Over 2.5 (Hertl, Tomas)", "Over 2.5 (Hertl, Tomas)", leg, searchResult, marketName));
        }

        [Fact]
        public void Test_Hockey_MitchMarner_2PlusAssists_Matches_Over1_5()
        {
            var searchResult = new OddsPapiSearchResult { MatchName = "Vegas Golden Knights vs Calgary Flames" };
            var leg = new AiVisionLeg
            {
                Match = "Vegas Golden Knights vs Calgary Flames",
                Market = "Player to Record 2+ Assists (Incl. Overtime)",
                Selection = "Mitch Marner - Yes"
            };

            string marketName = "Over Under Player Assists (incl. overtime)";

            Assert.False(OddsMatchingLogic.IsOutcomeMatch("Over 0.5 (Marner, Mitchell)", "Over 0.5 (Marner, Mitchell)", leg, searchResult, marketName));
            Assert.True(OddsMatchingLogic.IsOutcomeMatch("Over 1.5 (Marner, Mitchell)", "Over 1.5 (Marner, Mitchell)", leg, searchResult, marketName));
        }

        [Fact]
        public void Test_Soccer_FindBestMarket_RealisticFullMarketList_PicksCorrectMarkets()
        {
            // Create a realistic list of 100+ OddsPapi soccer markets matching real API responses
            var realisticMarkets = new List<OddsPapiMarket>
            {
                new OddsPapiMarket { MarketId = "2Up - Full Time Result", MarketName = "2Up - Full Time Result" },
                new OddsPapiMarket { MarketId = "3-Way Handicap", MarketName = "3-Way Handicap" },
                new OddsPapiMarket { MarketId = "Asian Handicap", MarketName = "Asian Handicap" },
                new OddsPapiMarket { MarketId = "Bookings - 1X2", MarketName = "Bookings - 1X2" },
                new OddsPapiMarket { MarketId = "Bookings - Odd Even", MarketName = "Bookings - Odd Even" },
                new OddsPapiMarket { MarketId = "Bookings - Odd Even Team 1", MarketName = "Bookings - Odd Even Team 1" },
                new OddsPapiMarket { MarketId = "Bookings - Odd Even Team 2", MarketName = "Bookings - Odd Even Team 2" },
                new OddsPapiMarket { MarketId = "Bookings - Over Under Full Time", MarketName = "Bookings - Over Under Full Time" },
                new OddsPapiMarket { MarketId = "Both Teams To Score", MarketName = "Both Teams To Score" },
                new OddsPapiMarket { MarketId = "Corners - Over Under Full Time", MarketName = "Corners - Over Under Full Time" },
                new OddsPapiMarket { MarketId = "Full Time Result", MarketName = "Full Time Result" },
                new OddsPapiMarket { MarketId = "Over Under Player Offsides (incl. overtime)", MarketName = "Over Under Player Offsides (incl. overtime)" },
                new OddsPapiMarket { MarketId = "Over Under Player Shots (incl. overtime)", MarketName = "Over Under Player Shots (incl. overtime)" },
                new OddsPapiMarket { MarketId = "Over Under Player Shots On Goal (incl. overtime)", MarketName = "Over Under Player Shots On Goal (incl. overtime)" },
                new OddsPapiMarket { MarketId = "Over Under Team 1", MarketName = "Over Under Team 1" },
                new OddsPapiMarket { MarketId = "Over Under Team 2", MarketName = "Over Under Team 2" },
                new OddsPapiMarket { MarketId = "Over Under Team 2 First Half", MarketName = "Over Under Team 2 First Half" },
                new OddsPapiMarket { MarketId = "Over Under Team 2 Second Half", MarketName = "Over Under Team 2 Second Half" }
            };

            // 1. Both Teams To Score must NEVER match Bookings - Odd Even Team 2
            var btts = OddsMatchingLogic.FindBestMarket(realisticMarkets, "Both Teams To Score", "Le Mans FC vs Toulouse FC", "Yes", _marketMapper);
            Assert.NotNull(btts);
            Assert.Equal("Both Teams To Score", btts.MarketId);
            Assert.NotEqual("Bookings - Odd Even Team 2", btts.MarketId);

            // 2. Match Result must NEVER match Over Under Team 2 Second Half
            var matchRes = OddsMatchingLogic.FindBestMarket(realisticMarkets, "Match Result", "HamKam vs Bodø/Glimt", "Bodø/Glimt", _marketMapper);
            Assert.NotNull(matchRes);
            Assert.Equal("Full Time Result", matchRes.MarketId);
            Assert.NotEqual("Over Under Team 2 Second Half", matchRes.MarketId);

            // 3. Player Shots must NEVER match Over Under Player Offsides
            var shots = OddsMatchingLogic.FindBestMarket(realisticMarkets, "Player Shots", "Eintracht Frankfurt vs 1. FC Köln", "Robin Koch - Over 0.5", _marketMapper);
            Assert.NotNull(shots);
            Assert.Equal("Over Under Player Shots (incl. overtime)", shots.MarketId);
            Assert.NotEqual("Over Under Player Offsides (incl. overtime)", shots.MarketId);
        }

        [Fact]
        public void Test_Hockey_Bet1025_TotalPointsOverUnder()
        {
            // Bet #1025: Total Points Over/Under 1.5, Nathan MacKinnon - Over 1.5
            var markets = new List<OddsPapiMarket>
            {
                new OddsPapiMarket { MarketId = "Total", MarketName = "Total" },
                new OddsPapiMarket { MarketId = "Over Under Player Points (incl. overtime)", MarketName = "Over Under Player Points (incl. overtime)" }
            };

            var best = OddsMatchingLogic.FindBestMarket(markets, "Total Points Over/Under 1.5", "Colorado Avalanche vs Pittsburgh Penguins", "Nathan MacKinnon - Over 1.5", _marketMapper);
            Assert.NotNull(best);
            Assert.Equal("Over Under Player Points (incl. overtime)", best.MarketId);

            var leg = new AiVisionLeg { Match = "Colorado Avalanche vs Pittsburgh Penguins", Sport = "Ice Hockey", Market = "Total Points Over/Under 1.5", Selection = "Nathan MacKinnon - Over 1.5" };
            var ctx = new OddsMatchingLogic.OutcomeMatchingContext(leg, null, best.MarketName);
            Assert.False(ctx.WantsUnder);
            Assert.Equal(1.5, ctx.TargetLine);

            bool matchOver = OddsMatchingLogic.IsOutcomeMatch("Over 1.5 (MacKinnon, Nathan)", "Over 1.5 (MacKinnon, Nathan)", ctx);
            bool matchUnder = OddsMatchingLogic.IsOutcomeMatch("Under 1.5 (MacKinnon, Nathan)", "Under 1.5 (MacKinnon, Nathan)", ctx);
            Assert.True(matchOver);
            Assert.False(matchUnder);
        }

        [Fact]
        public void Test_Hockey_Bet1024_PlayerTotalAssists_DirectionIntegrity()
        {
            // Bet #1024: Selection is Over 1.5, must NEVER match Under 1.5 despite market having "Over/Under"
            var markets = new List<OddsPapiMarket>
            {
                new OddsPapiMarket { MarketId = "Over Under Player Assists (incl. overtime)", MarketName = "Over Under Player Assists (incl. overtime)" }
            };

            var best = OddsMatchingLogic.FindBestMarket(markets, "Player Total Assists Over/Under 1.5", "Colorado Avalanche vs Pittsburgh Penguins", "Cale Makar - Over 1.5", _marketMapper);
            Assert.NotNull(best);
            Assert.Equal("Over Under Player Assists (incl. overtime)", best.MarketId);

            var leg = new AiVisionLeg { Match = "Colorado Avalanche vs Pittsburgh Penguins", Sport = "Ice Hockey", Market = "Player Total Assists Over/Under 1.5", Selection = "Cale Makar - Over 1.5" };
            var ctx = new OddsMatchingLogic.OutcomeMatchingContext(leg, null, best.MarketName);
            Assert.False(ctx.WantsUnder, "WantsUnder must be FALSE because user selected Over 1.5!");
            Assert.Equal(1.5, ctx.TargetLine);

            bool matchOver = OddsMatchingLogic.IsOutcomeMatch("Over 1.5 (Makar, Cale)", "Over 1.5 (Makar, Cale)", ctx);
            bool matchUnder = OddsMatchingLogic.IsOutcomeMatch("Under 1.5 (Makar, Cale)", "Under 1.5 (Makar, Cale)", ctx);
            Assert.True(matchOver, "Must match Over 1.5 (Makar, Cale)");
            Assert.False(matchUnder, "Must NOT match Under 1.5 (Makar, Cale)");
        }

        [Fact]
        public void Test_Hockey_Bet1023_PlayerTotalPoints()
        {
            // Bet #1023: Player Total Points Over/Under 1.5, Cale Makar - Over 1.5
            var markets = new List<OddsPapiMarket>
            {
                new OddsPapiMarket { MarketId = "Total", MarketName = "Total" },
                new OddsPapiMarket { MarketId = "Over Under Player Points (incl. overtime)", MarketName = "Over Under Player Points (incl. overtime)" }
            };

            var best = OddsMatchingLogic.FindBestMarket(markets, "Player Total Points Over/Under 1.5", "Colorado Avalanche vs Pittsburgh Penguins", "Cale Makar - Over 1.5", _marketMapper);
            Assert.NotNull(best);
            Assert.Equal("Over Under Player Points (incl. overtime)", best.MarketId);

            var leg = new AiVisionLeg { Match = "Colorado Avalanche vs Pittsburgh Penguins", Sport = "Ice Hockey", Market = "Player Total Points Over/Under 1.5", Selection = "Cale Makar - Over 1.5" };
            var ctx = new OddsMatchingLogic.OutcomeMatchingContext(leg, null, best.MarketName);
            Assert.False(ctx.WantsUnder);
            Assert.Equal(1.5, ctx.TargetLine);

            bool matchOver = OddsMatchingLogic.IsOutcomeMatch("Over 1.5 (Makar, Cale)", "Over 1.5 (Makar, Cale)", ctx);
            Assert.True(matchOver);
        }

        [Fact]
        public void Test_Hockey_Bet1021_PeterkaJJ_InitialsMatch()
        {
            // Bet #1021: John-Jason Peterka - Yes vs Peterka, JJ
            var markets = new List<OddsPapiMarket>
            {
                new OddsPapiMarket { MarketId = "Over Under Player Points (incl. overtime)", MarketName = "Over Under Player Points (incl. overtime)" }
            };

            var best = OddsMatchingLogic.FindBestMarket(markets, "Player to Record at Least 1 Point - Including Overtime", "Anaheim Ducks vs Boston Bruins", "John-Jason Peterka - Yes", _marketMapper);
            Assert.NotNull(best);
            Assert.Equal("Over Under Player Points (incl. overtime)", best.MarketId);

            var leg = new AiVisionLeg { Match = "Anaheim Ducks vs Boston Bruins", Sport = "Ice Hockey", Market = "Player to Record at Least 1 Point - Including Overtime", Selection = "John-Jason Peterka - Yes" };
            var ctx = new OddsMatchingLogic.OutcomeMatchingContext(leg, null, best.MarketName);
            Assert.False(ctx.WantsUnder);
            Assert.Equal(0.5, ctx.TargetLine);

            bool matchOver = OddsMatchingLogic.IsOutcomeMatch("Over 0.5 (Peterka, JJ)", "Over 0.5 (Peterka, JJ)", ctx);
            Assert.True(matchOver, "'Over 0.5 (Peterka, JJ)' must match 'John-Jason Peterka - Yes' via initials");
        }

        [Fact]
        public void Test_Bet1031_Leg2_TotalGoals_Over25Goals_MatchesOver25()
        {
            // Bet #1031 Leg 2: Total Goals, Over 2.5 Goals
            var markets = new List<OddsPapiMarket>
            {
                new OddsPapiMarket
                {
                    MarketId = "Over Under Full Time",
                    MarketName = "Over Under Full Time",
                    OutcomeNames = new Dictionary<string, string>
                    {
                        ["o1"] = "Over 0.5",
                        ["o2"] = "Under 0.5",
                        ["o3"] = "Over 1.5",
                        ["o4"] = "Under 1.5",
                        ["o5"] = "Over 2.5",
                        ["o6"] = "Under 2.5",
                        ["o7"] = "Over 3.5",
                        ["o8"] = "Under 3.5"
                    }
                }
            };

            var best = OddsMatchingLogic.FindBestMarket(markets, "Total Goals", "Brentford vs Liverpool", "Over 2.5 Goals", _marketMapper);
            Assert.NotNull(best);
            Assert.Equal("Over Under Full Time", best.MarketId);

            var leg = new AiVisionLeg { Match = "Brentford vs Liverpool", Sport = "Soccer", Market = "Total Goals", Selection = "Over 2.5 Goals" };
            var apiResult = new OddsPapiSearchResult { MatchName = "Brentford vs Liverpool" };
            var ctx = new OddsMatchingLogic.OutcomeMatchingContext(leg, apiResult, best.MarketName);

            Assert.False(ctx.WantsUnder);
            Assert.Equal(2.5, ctx.TargetLine);

            Assert.True(OddsMatchingLogic.IsOutcomeMatch("Over 2.5", "Over 2.5", ctx), "Over 2.5 Goals must match Over 2.5");
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("Under 2.5", "Under 2.5", ctx), "Over 2.5 Goals must not match Under 2.5");
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("Over 1.5", "Over 1.5", ctx), "Over 2.5 Goals must not match Over 1.5");
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("Over 3.5", "Over 3.5", ctx), "Over 2.5 Goals must not match Over 3.5");
        }

        [Theory]
        [InlineData("Over 2.5 Goals", "Over Under Full Time", "Over 2.5", true)]
        [InlineData("Over 2.5", "Over Under Full Time", "Over 2.5", true)]
        [InlineData("Over 2.5 Mål", "Over Under Full Time", "Over 2.5", true)]
        [InlineData("3+ Goals", "Over Under Full Time", "Over 2.5", true)]
        [InlineData("At least 3 goals", "Over Under Full Time", "Over 2.5", true)]
        [InlineData("Under 2.5 Goals", "Over Under Full Time", "Under 2.5", true)]
        [InlineData("Under 2.5 Goals", "Over Under Full Time", "Over 2.5", false)]
        [InlineData("Over 0.5 Goals", "Over Under First Half", "Over 0.5", true)]
        [InlineData("Over 9.5 Corners", "Total Corners", "Over 9.5", true)]
        [InlineData("Over 3.5 Cards", "Total Cards", "Over 3.5", true)]
        [InlineData("Over 2.5", "Asian Over Under", "Over 2.5 (0:0)", true)]
        public void Test_PureOverUnderOutcomeMatching_VariousFormats(string selection, string opMarket, string opOutcome, bool expected)
        {
            var leg = new AiVisionLeg { Match = "Brentford vs Liverpool", Sport = "Soccer", Market = "Total Goals", Selection = selection };
            var apiResult = new OddsPapiSearchResult { MatchName = "Brentford vs Liverpool" };
            var ctx = new OddsMatchingLogic.OutcomeMatchingContext(leg, apiResult, opMarket);

            bool isMatch = OddsMatchingLogic.IsOutcomeMatch(opOutcome, opOutcome, ctx);
            Assert.Equal(expected, isMatch);
        }

        [Fact]
        public void Test_PlayerProp_DoesNotAccidentallyMatch_MatchTotalGoals()
        {
            // Kiernan Dewsbury-Hall - Over 1.5 (Power Sub) must not match 'Over 1.5' in 'Over Under Full Time'
            var leg = new AiVisionLeg { Match = "Brentford vs Liverpool", Sport = "Soccer", Market = "", Selection = "Kiernan Dewsbury-Hall - Over 1.5 (Power Sub)" };
            var apiResult = new OddsPapiSearchResult { MatchName = "Brentford vs Liverpool" };
            var ctx = new OddsMatchingLogic.OutcomeMatchingContext(leg, apiResult, "Over Under Full Time");

            bool isMatch = OddsMatchingLogic.IsOutcomeMatch("Over 1.5", "Over 1.5", ctx);
            Assert.False(isMatch, "Player prop selection must never match a generic match total outcome");
        }

        [Fact]
        public void Test_TeamTotalGoals_TeamAlignment()
        {
            var brentfordLeg = new AiVisionLeg { Match = "Brentford vs Liverpool", Sport = "Soccer", Market = "Brentford Total Goals", Selection = "Over 1.5 Goals" };
            var apiResult = new OddsPapiSearchResult { MatchName = "Brentford vs Liverpool" };
            
            var ctxTeam1 = new OddsMatchingLogic.OutcomeMatchingContext(brentfordLeg, apiResult, "Over Under Team 1");
            Assert.True(OddsMatchingLogic.IsOutcomeMatch("Over 1.5", "Over 1.5", ctxTeam1), "Brentford total must match Over Under Team 1");

            var ctxTeam2 = new OddsMatchingLogic.OutcomeMatchingContext(brentfordLeg, apiResult, "Over Under Team 2");
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("Over 1.5", "Over 1.5", ctxTeam2), "Brentford total must NOT match Over Under Team 2");
        }

        [Theory]
        [InlineData("Tottenham/Tottenham", "1/1", true)]
        [InlineData("Tottenham / Tottenham", "1/1", true)]
        [InlineData("Tottenham - Tottenham", "1/1", true)]
        [InlineData("Tottenham/Coventry City", "1/2", true)]
        [InlineData("Tottenham / Coventry City", "1/2", true)]
        [InlineData("Tottenham/Draw", "1/X", true)]
        [InlineData("Tottenham / Draw", "1/X", true)]
        [InlineData("Tottenham / Uavgjort", "1/X", true)]
        [InlineData("Draw/Tottenham", "X/1", true)]
        [InlineData("Draw / Tottenham", "X/1", true)]
        [InlineData("Draw/Draw", "X/X", true)]
        [InlineData("Coventry City/Tottenham", "2/1", true)]
        [InlineData("Coventry City/Coventry City", "2/2", true)]
        [InlineData("Coventry City/Draw", "2/X", true)]
        [InlineData("1/1", "1/1", true)]
        [InlineData("1 / 1", "1/1", true)]
        [InlineData("1/X", "1/X", true)]
        [InlineData("X/1", "X/1", true)]
        [InlineData("2/2", "2/2", true)]
        [InlineData("Tottenham/Tottenham", "1/2", false)]
        [InlineData("Tottenham/Tottenham", "2/1", false)]
        [InlineData("Tottenham/Tottenham", "2/2", false)]
        [InlineData("Tottenham/Tottenham", "1/X", false)]
        [InlineData("Coventry City/Tottenham", "1/2", false)]
        public void Test_HalfTimeFullTime_OutcomeMatching_VariousFormats(string selection, string opOutcome, bool expected)
        {
            var leg = new AiVisionLeg
            {
                Match = "Tottenham vs Coventry City",
                Sport = "Soccer",
                Market = "Half Time / Full Time",
                Selection = selection
            };
            var apiResult = new OddsPapiSearchResult
            {
                MatchName = "Tottenham vs Coventry City"
            };
            var ctx = new OddsMatchingLogic.OutcomeMatchingContext(leg, apiResult, "Half Time / Full Time");
            string displayOutcome = OddsMatchingLogic.GetTranslatedOutcomeName(opOutcome, apiResult.MatchName);

            bool isMatch = OddsMatchingLogic.IsOutcomeMatch(opOutcome, displayOutcome, ctx);
            Assert.Equal(expected, isMatch);
        }

        [Fact]
        public void Test_HalfTimeFullTime_SwappedTeams()
        {
            // Leg match has Coventry City vs Tottenham (AiT1 = Coventry, AiT2 = Tottenham)
            // OddsPapi match has Tottenham vs Coventry City (OpT1 = Tottenham, OpT2 = Coventry)
            var leg = new AiVisionLeg
            {
                Match = "Coventry City vs Tottenham",
                Sport = "Soccer",
                Market = "Half Time / Full Time",
                Selection = "Tottenham/Tottenham"
            };
            var apiResult = new OddsPapiSearchResult
            {
                MatchName = "Tottenham vs Coventry City"
            };
            var ctx = new OddsMatchingLogic.OutcomeMatchingContext(leg, apiResult, "Half Time / Full Time");
            
            // Tottenham in OddsPapi is Team 1, so Tottenham/Tottenham must match "1/1"
            string display11 = OddsMatchingLogic.GetTranslatedOutcomeName("1/1", apiResult.MatchName);
            Assert.True(OddsMatchingLogic.IsOutcomeMatch("1/1", display11, ctx), "Tottenham/Tottenham must match 1/1 when teams are swapped");
            
            string display22 = OddsMatchingLogic.GetTranslatedOutcomeName("2/2", apiResult.MatchName);
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("2/2", display22, ctx), "Tottenham/Tottenham must NOT match 2/2 when teams are swapped");
        }

        [Fact]
        public void Test_AsianHandicap_MatchingAndSorting_Bet1048()
        {
            var leg = new AiVisionLeg
            {
                Match = "Getafe vs Rayo Vallecano",
                Sport = "Soccer",
                Market = "Asian Handicap",
                Selection = "Rayo Vallecano +0.5"
            };
            var apiResult = new OddsPapiSearchResult
            {
                MatchName = "Getafe CF vs Rayo Vallecano"
            };

            var ctx = new OddsMatchingLogic.OutcomeMatchingContext(leg, apiResult, "Asian Handicap");

            // Must match "2 (-0.5)" (which translates to Rayo Vallecano +0.5)
            string displayMatch = OddsMatchingLogic.GetTranslatedOutcomeName("2 (-0.5)", apiResult.MatchName);
            Assert.Equal("Rayo Vallecano +0.5", displayMatch);
            Assert.True(OddsMatchingLogic.IsOutcomeMatch("2 (-0.5)", displayMatch, ctx), "Rayo Vallecano +0.5 must match 2 (-0.5)");

            // Must NOT match "2" (plain Rayo Vallecano with no handicap / 0 handicap)
            string displayZero = OddsMatchingLogic.GetTranslatedOutcomeName("2", apiResult.MatchName);
            Assert.Equal("Rayo Vallecano", displayZero);
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("2", displayZero, ctx), "Rayo Vallecano +0.5 must NOT match 2 without handicap");

            // Must NOT match "1 (-0.5)" (Getafe CF -0.5)
            string displayOpposite = OddsMatchingLogic.GetTranslatedOutcomeName("1 (-0.5)", apiResult.MatchName);
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("1 (-0.5)", displayOpposite, ctx), "Rayo Vallecano +0.5 must NOT match 1 (-0.5)");

            // Test sorting: Team 1 outcomes should all sort before Team 2, and handicaps should be in ascending order
            var rawOutcomes = new List<string> { "2", "1 (-1.5)", "2 (-0.5)", "1 (0.5)", "1", "2 (0.5)", "1 (-0.5)", "2 (-1.5)" };
            var sorted = rawOutcomes.OrderBy(o => OddsMatchingLogic.GetSortKey(o)).ToList();

            // Expected order:
            // Team 1: 1 (-1.5), 1 (-0.5), 1, 1 (0.5)
            // Team 2: 2 (0.5) [-0.5], 2 [0], 2 (-0.5) [+0.5], 2 (-1.5) [+1.5]
            var expected = new List<string> { "1 (-1.5)", "1 (-0.5)", "1", "1 (0.5)", "2 (0.5)", "2", "2 (-0.5)", "2 (-1.5)" };
            Assert.Equal(expected, sorted);
        }

        [Fact]
        public void Test_PlayerToScoreOrBeBooked_Bet1047()
        {
            var leg = new AiVisionLeg
            {
                Match = "Tottenham vs Coventry City",
                Sport = "Soccer",
                Market = "Player to Score or be Booked",
                Selection = "Bobby Thomas - To Score or be Booked (Power Sub)"
            };

            // 1. Must be recognized as combination market
            Assert.True(MarketMappingService.IsCombinationMarket(leg.Market, leg.Match), "Player to Score or be Booked must be recognized as combination market");
            Assert.True(MarketMappingService.IsCombinationMarket(leg.Selection, leg.Match), "Bobby Thomas - To Score or be Booked must be recognized as combination market");

            // 2. FindBestMarket must return null when only Goals and Carded markets exist
            var mockMarkets = new List<OddsPapiMarket>
            {
                new OddsPapiMarket { MarketId = "1", MarketName = "Over Under Player Goals (incl. overtime)" },
                new OddsPapiMarket { MarketId = "2", MarketName = "Player To Be Carded (incl. overtime)" },
                new OddsPapiMarket { MarketId = "3", MarketName = "Full Time Result" }
            };

            var best = OddsMatchingLogic.FindBestMarket(mockMarkets, leg.Market, leg.Match, leg.Selection, _marketMapper);
            Assert.Null(best);

            // 3. Even if evaluated directly against Over Under Player Goals, IsOutcomeMatch must reject it
            var apiResult = new OddsPapiSearchResult { MatchName = "Tottenham Hotspur vs Coventry City" };
            var ctxGoals = new OddsMatchingLogic.OutcomeMatchingContext(leg, apiResult, "Over Under Player Goals (incl. overtime)");
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("Over 0.5 (Thomas, Bobby)", "Over 0.5 (Thomas, Bobby)", ctxGoals),
                "Bobby Thomas - To Score or be Booked must NOT match Over 0.5 in Over Under Player Goals");

            var ctxCards = new OddsMatchingLogic.OutcomeMatchingContext(leg, apiResult, "Player To Be Carded (incl. overtime)");
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("Thomas, Bobby", "Thomas, Bobby", ctxCards),
                "Bobby Thomas - To Score or be Booked must NOT match in Player To Be Carded");
        }

        [Fact]
        public void Test_ThreeWayHandicap_Matching_Bet1049()
        {
            var leg = new AiVisionLeg
            {
                Match = "Al Hilal vs Al Hazem",
                Sport = "Soccer",
                Market = "3-Way Handicap (0-2)",
                Selection = "Al Hilal"
            };
            var apiResult = new OddsPapiSearchResult
            {
                MatchName = "Al Hilal SFC vs Al-Hazm"
            };

            var ctx = new OddsMatchingLogic.OutcomeMatchingContext(leg, apiResult, "3-Way Handicap");

            // Must match "1 (0-2.0)" (Al Hilal SFC -2)
            string display2 = OddsMatchingLogic.GetTranslatedOutcomeName("1 (0-2.0)", apiResult.MatchName);
            Assert.Equal("Al Hilal SFC -2", display2);
            Assert.True(OddsMatchingLogic.IsOutcomeMatch("1 (0-2.0)", display2, ctx), "Al Hilal with (0-2) market handicap must match 1 (0-2.0)");

            // Must NOT match "1 (0-1.0)" (Al Hilal SFC -1)
            string display1 = OddsMatchingLogic.GetTranslatedOutcomeName("1 (0-1.0)", apiResult.MatchName);
            Assert.Equal("Al Hilal SFC -1", display1);
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("1 (0-1.0)", display1, ctx), "Al Hilal with (0-2) market handicap must NOT match 1 (0-1.0)");

            // Must NOT match "1 (0-3.0)" (Al Hilal SFC -3)
            string display3 = OddsMatchingLogic.GetTranslatedOutcomeName("1 (0-3.0)", apiResult.MatchName);
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("1 (0-3.0)", display3, ctx), "Al Hilal with (0-2) market handicap must NOT match 1 (0-3.0)");

            // Must NOT match Draw (-2) or Al-Hazm (+2)
            string displayDraw = OddsMatchingLogic.GetTranslatedOutcomeName("X (0-2.0)", apiResult.MatchName);
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("X (0-2.0)", displayDraw, ctx), "Al Hilal must NOT match Draw");

            string displayAway = OddsMatchingLogic.GetTranslatedOutcomeName("2 (0-2.0)", apiResult.MatchName);
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("2 (0-2.0)", displayAway, ctx), "Al Hilal must NOT match Al-Hazm");
        }

        [Fact]
        public void Test_TeamTotalGoals_BareOverSelection_DoesNotFallThroughOnWrongTeam()
        {
            // Regression test: when Selection is bare "Over 1.5" (without "Goals"),
            // evaluating against "Over Under Team 2" for a Team 1 leg must return false
            // and MUST NOT fall through to normOName == ctx.NormSel ("over15" == "over15").
            var brentfordLeg = new AiVisionLeg
            {
                Match = "Brentford vs Liverpool",
                Sport = "Soccer",
                Market = "Brentford Total Goals",
                Selection = "Over 1.5"
            };
            var apiResult = new OddsPapiSearchResult { MatchName = "Brentford vs Liverpool" };

            var ctxTeam1 = new OddsMatchingLogic.OutcomeMatchingContext(brentfordLeg, apiResult, "Over Under Team 1");
            Assert.True(OddsMatchingLogic.IsOutcomeMatch("Over 1.5", "Over 1.5", ctxTeam1));

            var ctxTeam2 = new OddsMatchingLogic.OutcomeMatchingContext(brentfordLeg, apiResult, "Over Under Team 2");
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("Over 1.5", "Over 1.5", ctxTeam2),
                "Bare 'Over 1.5' for Team 1 must never match 'Over 1.5' in 'Over Under Team 2'");
        }

        [Fact]
        public void Test_PlayerSharingTeamNameToken_DoesNotMatchTeamTotalBet()
        {
            // If a player's name happens to share a token with a team in the match (e.g. "Roma, Marco"),
            // a team total bet ("Total Goalkeeper Saves - Roma", "Over 5.5") must NEVER match that player prop.
            var leg = new AiVisionLeg
            {
                Match = "Roma vs Real Madrid",
                Market = "Total Goalkeeper Saves - Roma",
                Selection = "Over 5.5"
            };
            var searchResult = new OddsPapiSearchResult { MatchName = "Roma vs Real Madrid" };

            bool isMatch = OddsMatchingLogic.IsOutcomeMatch(
                "Over 5.5 (Roma, Marco)",
                "Over 5.5 (Roma, Marco)",
                leg,
                searchResult,
                "Over Under Player Goalkeeper Saves (incl. overtime)");

            Assert.False(isMatch);
        }

        [Fact]
        public void Test_FullDictionarySimulation_ThreeWayHandicap_SelectsExactLineOnly()
        {
            // Simulates Admin.OddsLookup.cs iterating through the full OddsPapi outcome dictionary in order
            var leg = new AiVisionLeg
            {
                Match = "Al Hilal vs Al Hazem",
                Sport = "Soccer",
                Market = "3-Way Handicap (0-2)",
                Selection = "Al Hilal"
            };
            var apiResult = new OddsPapiSearchResult { MatchName = "Al Hilal SFC vs Al-Hazm" };
            var ctx = new OddsMatchingLogic.OutcomeMatchingContext(leg, apiResult, "3-Way Handicap");

            var outcomesInOrder = new[]
            {
                "1 (0-1.0)", "X (0-1.0)", "2 (0-1.0)",
                "1 (0-2.0)", "X (0-2.0)", "2 (0-2.0)",
                "1 (0-3.0)", "X (0-3.0)", "2 (0-3.0)"
            };

            var matchedOutcomes = outcomesInOrder
                .Where(o => OddsMatchingLogic.IsOutcomeMatch(o, OddsMatchingLogic.GetTranslatedOutcomeName(o, apiResult.MatchName), ctx))
                .ToList();

            Assert.Single(matchedOutcomes);
            Assert.Equal("1 (0-2.0)", matchedOutcomes[0]);
        }

        [Fact]
        public void Test_FullDictionarySimulation_AsianHandicap_SelectsExactLineOnly()
        {
            // Simulates Admin.OddsLookup.cs iterating through the full OddsPapi Asian Handicap dictionary
            // where "2" (0 handicap) and "2 (0.5)" (-0.5) appear before "2 (-0.5)" (+0.5)
            var leg = new AiVisionLeg
            {
                Match = "Getafe vs Rayo Vallecano",
                Sport = "Soccer",
                Market = "Asian Handicap",
                Selection = "Rayo Vallecano +0.5"
            };
            var apiResult = new OddsPapiSearchResult { MatchName = "Getafe CF vs Rayo Vallecano" };
            var ctx = new OddsMatchingLogic.OutcomeMatchingContext(leg, apiResult, "Asian Handicap");

            var outcomesInOrder = new[]
            {
                "1", "2",
                "1 (0.5)", "2 (0.5)",
                "1 (-0.5)", "2 (-0.5)",
                "1 (-1.5)", "2 (-1.5)"
            };

            var matchedOutcomes = outcomesInOrder
                .Where(o => OddsMatchingLogic.IsOutcomeMatch(o, OddsMatchingLogic.GetTranslatedOutcomeName(o, apiResult.MatchName), ctx))
                .ToList();

            Assert.Single(matchedOutcomes);
            Assert.Equal("2 (-0.5)", matchedOutcomes[0]);
        }

        [Theory]
        [InlineData("New York Rangers vs Anaheim Ducks", "Player to Record at Least 1 Assist (Inc. Overtime)", "Adam Fox - No", "Over Under Player Assists (incl. overtime)", "Fox, Adam", "Under 0.5 (Fox, Adam)")]
        [InlineData("Philadelphia Flyers vs Colorado Avalanche", "Player to Record at Least 1 Assist (Inc. Overtime)", "Cale Makar - No", "Over Under Player Assists (incl. overtime)", "Makar, Cale", "Under 0.5 (Makar, Cale)")]
        [InlineData("Toronto Maple Leafs vs San Jose Sharks", "Player to Record at Least 1 Assist (Inc. Overtime)", "Macklin Celebrini - No", "Over Under Player Assists (incl. overtime)", "Celebrini, Macklin", "Under 0.5 (Celebrini, Macklin)")]
        [InlineData("Utah Mammoth vs Pittsburgh Penguins", "Player to Record at Least 2 Points (Inc. Overtime)", "Clayton Keller - Yes", "Over Under Player Points (incl. overtime)", "Keller, Clayton", "Over 1.5 (Keller, Clayton)")]
        public void Test_Bet1050_AllFourLegs_FullDictionarySimulation_SelectsExactLineAndDirection(
            string matchName,
            string marketName,
            string selection,
            string opMarketName,
            string opPlayerName,
            string expectedOutcome)
        {
            var leg = new AiVisionLeg
            {
                Match = matchName,
                Sport = "Ice Hockey",
                Market = marketName,
                Selection = selection
            };
            var apiResult = new OddsPapiSearchResult { MatchName = matchName };
            var ctx = new OddsMatchingLogic.OutcomeMatchingContext(leg, apiResult, opMarketName);

            var outcomesInOrder = new[]
            {
                $"Over 0.5 ({opPlayerName})",
                $"Over 1.5 ({opPlayerName})",
                $"Over 2.5 ({opPlayerName})",
                $"Under 0.5 ({opPlayerName})",
                $"Under 1.5 ({opPlayerName})",
                $"Under 2.5 ({opPlayerName})"
            };

            var matchedOutcomes = outcomesInOrder
                .Where(o => OddsMatchingLogic.IsOutcomeMatch(o, o, ctx))
                .ToList();

            Assert.Single(matchedOutcomes);
            Assert.Equal(expectedOutcome, matchedOutcomes[0]);
        }

        [Fact]
        public void Test_NegativePlayerProp_FuzzyAndUnparenthesizedFallback_SelectsUnderAndRejectsOver()
        {
            // Verify IsFuzzyPlayerMatch directly when market says "at least 1" and selection is "- No"
            Assert.False(OddsMatchingLogic.IsFuzzyPlayerMatch(
                "Player to Record at Least 1 Assist (Inc. Overtime) Adam Fox - No",
                "Over 0.5 (Fox, Adam)"));
            Assert.True(OddsMatchingLogic.IsFuzzyPlayerMatch(
                "Player to Record at Least 1 Assist (Inc. Overtime) Adam Fox - No",
                "Under 0.5 (Fox, Adam)"));

            // Verify unparenthesized outcomes ("Adam Fox Over 0.5", "Adam Fox Under 0.5", bare "Adam Fox")
            var leg = new AiVisionLeg
            {
                Match = "New York Rangers vs Anaheim Ducks",
                Sport = "Ice Hockey",
                Market = "Player to Record at Least 1 Assist (Inc. Overtime)",
                Selection = "Adam Fox - No"
            };
            var apiResult = new OddsPapiSearchResult { MatchName = "New York Rangers vs Anaheim Ducks" };
            var ctx = new OddsMatchingLogic.OutcomeMatchingContext(leg, apiResult, "Over Under Player Assists (incl. overtime)");

            Assert.False(OddsMatchingLogic.IsOutcomeMatch("Adam Fox Over 0.5", "Adam Fox Over 0.5", ctx));
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("Adam Fox", "Adam Fox", ctx));
            Assert.True(OddsMatchingLogic.IsOutcomeMatch("Adam Fox Under 0.5", "Adam Fox Under 0.5", ctx));
        }

        [Fact]
        public void Test_Bet1060_Dev_AllLegs_EndToEndMarketAndOutcomeMatching()
        {
            var mockMarkets = new List<OddsPapiMarket>
            {
                new OddsPapiMarket { MarketId = "goals", MarketName = "Over Under Player Goals (incl. overtime)" },
                new OddsPapiMarket { MarketId = "assists", MarketName = "Over Under Player Assists (incl. overtime)" },
                new OddsPapiMarket { MarketId = "shots", MarketName = "Over Under Player Shots (incl. overtime)" },
                new OddsPapiMarket { MarketId = "cards", MarketName = "Player To Be Carded (incl. overtime)" }
            };

            // Leg 1: AS Roma vs Slovan Bratislava | Will/Will not Score | Donyell Malen - Will Score
            var leg1 = new AiVisionLeg { Match = "AS Roma vs Slovan Bratislava", Sport = "Soccer", Market = "Will/Will not Score", Selection = "Donyell Malen - Will Score" };
            var mkt1 = OddsMatchingLogic.FindBestMarket(mockMarkets, leg1.Market, leg1.Match, leg1.Selection, _marketMapper);
            Assert.NotNull(mkt1);
            Assert.Equal("Over Under Player Goals (incl. overtime)", mkt1.MarketName);

            var ctx1 = new OddsMatchingLogic.OutcomeMatchingContext(leg1, new OddsPapiSearchResult { MatchName = leg1.Match }, mkt1.MarketName);
            Assert.False(ctx1.WantsUnder, "Will/Will not Score with 'Will Score' must have WantsUnder = false");

            var outcomes1 = new[]
            {
                "Over 0.5 (Malen, Donyell)", "Under 0.5 (Malen, Donyell)",
                "Over 1.5 (Malen, Donyell)", "Under 1.5 (Malen, Donyell)",
                "Over 0.5 (Dybala, Paulo)", "Under 0.5 (Dybala, Paulo)"
            };
            var matched1 = outcomes1.Where(o => OddsMatchingLogic.IsOutcomeMatch(o, o, ctx1)).ToList();
            Assert.Single(matched1);
            Assert.Equal("Over 0.5 (Malen, Donyell)", matched1[0]);

            // Leg 2: Paris Saint-Germain vs FC Barcelona | Will/Will not Score | Ousmane Dembele - Will Score
            var leg2 = new AiVisionLeg { Match = "Paris Saint-Germain vs FC Barcelona", Sport = "Soccer", Market = "Will/Will not Score", Selection = "Ousmane Dembele - Will Score" };
            var mkt2 = OddsMatchingLogic.FindBestMarket(mockMarkets, leg2.Market, leg2.Match, leg2.Selection, _marketMapper);
            Assert.NotNull(mkt2);
            Assert.Equal("Over Under Player Goals (incl. overtime)", mkt2.MarketName);

            var ctx2 = new OddsMatchingLogic.OutcomeMatchingContext(leg2, new OddsPapiSearchResult { MatchName = leg2.Match }, mkt2.MarketName);
            Assert.False(ctx2.WantsUnder);

            var outcomes2 = new[]
            {
                "Over 0.5 (Dembele, Ousmane)", "Under 0.5 (Dembele, Ousmane)",
                "Over 1.5 (Dembele, Ousmane)", "Under 1.5 (Dembele, Ousmane)"
            };
            var matched2 = outcomes2.Where(o => OddsMatchingLogic.IsOutcomeMatch(o, o, ctx2)).ToList();
            Assert.Single(matched2);
            Assert.Equal("Over 0.5 (Dembele, Ousmane)", matched2[0]);

            // Leg 3: VfB Stuttgart vs Atletico Madrid | To Score or give an Assist | Alex Baena - Yes
            // OddsPapi does not offer combined Goal+Assist market; must return null rather than matching pure Goals or pure Assists
            var leg3 = new AiVisionLeg { Match = "VfB Stuttgart vs Atletico Madrid", Sport = "Soccer", Market = "To Score or give an Assist", Selection = "Alex Baena - Yes" };
            var mkt3 = OddsMatchingLogic.FindBestMarket(mockMarkets, leg3.Market, leg3.Match, leg3.Selection, _marketMapper);
            Assert.Null(mkt3);

            // Leg 4: Liverpool vs Villarreal | To Give an Assist | Bradley Barcola - Yes
            var leg4 = new AiVisionLeg { Match = "Liverpool vs Villarreal", Sport = "Soccer", Market = "To Give an Assist", Selection = "Bradley Barcola - Yes" };
            var mkt4 = OddsMatchingLogic.FindBestMarket(mockMarkets, leg4.Market, leg4.Match, leg4.Selection, _marketMapper);
            Assert.NotNull(mkt4);
            Assert.Equal("Over Under Player Assists (incl. overtime)", mkt4.MarketName);

            var ctx4 = new OddsMatchingLogic.OutcomeMatchingContext(leg4, new OddsPapiSearchResult { MatchName = leg4.Match }, mkt4.MarketName);
            var outcomes4 = new[]
            {
                "Over 0.5 (Barcola, Bradley)", "Under 0.5 (Barcola, Bradley)",
                "Over 1.5 (Barcola, Bradley)", "Under 1.5 (Barcola, Bradley)"
            };
            var matched4 = outcomes4.Where(o => OddsMatchingLogic.IsOutcomeMatch(o, o, ctx4)).ToList();
            Assert.Single(matched4);
            Assert.Equal("Over 0.5 (Barcola, Bradley)", matched4[0]);
        }

        [Theory]
        // Coolbet "Will/Will not" markets (positive & negative)
        [InlineData("Will/Will not Score", "Donyell Malen - Will Score", "Over Under Player Goals (incl. overtime)", "Malen, Donyell", "Over 0.5 (Malen, Donyell)")]
        [InlineData("Will/Will not Score", "Donyell Malen - Will Not Score", "Over Under Player Goals (incl. overtime)", "Malen, Donyell", "Under 0.5 (Malen, Donyell)")]
        [InlineData("Will/Will not get Booked", "Bryan Cristante - Will get Booked", "Player To Be Carded (incl. overtime)", "Cristante, Bryan", "Over 0.5 (Cristante, Bryan)")]
        [InlineData("Will/Will not get Booked", "Bryan Cristante - Will Not get Booked", "Player To Be Carded (incl. overtime)", "Cristante, Bryan", "Under 0.5 (Cristante, Bryan)")]
        [InlineData("Will/Will not Score From Header", "Cristian Gabriel Romero - Will Score", "Over Under Player Goals (incl. overtime)", "Romero, Cristian Gabriel", "Over 0.5 (Romero, Cristian Gabriel)")]
        [InlineData("Will/Will not Score: Lautaro Martínez", "Lautaro Martínez - Will Score", "Over Under Player Goals (incl. overtime)", "Martínez, Lautaro", "Over 0.5 (Martínez, Lautaro)")]
        [InlineData("Will Score", "Bilal Njie - Will Score", "Over Under Player Goals (incl. overtime)", "Njie, Bilal", "Over 0.5 (Njie, Bilal)")]
        [InlineData("To Score During 1st Half", "Erling Haaland - Will Score", "Over Under Player Goals (incl. overtime)", "Haaland, Erling", "Over 0.5 (Haaland, Erling)")]
        // Bet365 / Unibet / Kambi binary player prop phrasing variations
        [InlineData("Anytime Goalscorer", "Erling Haaland", "Over Under Player Goals (incl. overtime)", "Haaland, Erling", "Over 0.5 (Haaland, Erling)")]
        [InlineData("Anytime Goalscorer", "Erling Haaland - Anytime Goalscorer", "Over Under Player Goals (incl. overtime)", "Haaland, Erling", "Over 0.5 (Haaland, Erling)")]
        [InlineData("Anytime Goalscorer", "Erling Haaland - Yes", "Over Under Player Goals (incl. overtime)", "Haaland, Erling", "Over 0.5 (Haaland, Erling)")]
        [InlineData("Anytime Goalscorer", "Erling Haaland - No", "Over Under Player Goals (incl. overtime)", "Haaland, Erling", "Under 0.5 (Haaland, Erling)")]
        [InlineData("To Score", "Mikel Oyarzabal - To Score", "Over Under Player Goals (incl. overtime)", "Oyarzabal, Mikel", "Over 0.5 (Oyarzabal, Mikel)")]
        [InlineData("To Score", "Raphinha - To Score (Power Sub)", "Over Under Player Goals (incl. overtime)", "Raphinha", "Over 0.5 (Raphinha)")]
        [InlineData("To Score at Any Time", "Cole Palmer to Score (Power Sub)", "Over Under Player Goals (incl. overtime)", "Palmer, Cole", "Over 0.5 (Palmer, Cole)")]
        [InlineData("To Score at Any Time", "Kylian Mbappe - Anytime (Power Sub)", "Over Under Player Goals (incl. overtime)", "Mbappe, Kylian", "Over 0.5 (Mbappe, Kylian)")]
        [InlineData("Player Assists", "Bukayo Saka - Assist", "Over Under Player Assists (incl. overtime)", "Saka, Bukayo", "Over 0.5 (Saka, Bukayo)")]
        [InlineData("Player Assists", "Martin Ødegaard - To Provide An Assist", "Over Under Player Assists (incl. overtime)", "Ødegaard, Martin", "Over 0.5 (Ødegaard, Martin)")]
        [InlineData("To Give An Assist", "Lionel Messi - To give an assist (Power Sub)", "Over Under Player Assists (incl. overtime)", "Messi, Lionel", "Over 0.5 (Messi, Lionel)")]
        [InlineData("To Be Carded", "Ozan Kabak - To Be Carded", "Player To Be Carded (incl. overtime)", "Kabak, Ozan", "Over 0.5 (Kabak, Ozan)")]
        [InlineData("To Receive a Card", "Philip Brittijn - To receive a card", "Player To Be Carded (incl. overtime)", "Brittijn, Philip", "Over 0.5 (Brittijn, Philip)")]
        // Word numbers, "and more", braces, hat-tricks, multi-line props
        [InlineData("Player To Score Two Or More Goals", "Orkun Kokcu - To Score Two Or More Goals", "Over Under Player Goals (incl. overtime)", "Kokcu, Orkun", "Over 1.5 (Kokcu, Orkun)")]
        [InlineData("To Score Two or More Goals", "Lamine Yamal - To Score Two or More Goals (Power Sub)", "Over Under Player Goals (incl. overtime)", "Yamal, Lamine", "Over 1.5 (Yamal, Lamine)")]
        [InlineData("Player Goals", "Kylian Mbappe - 2 and more", "Over Under Player Goals (incl. overtime)", "Mbappe, Kylian", "Over 1.5 (Mbappe, Kylian)")]
        [InlineData("Player To Score A Hat Trick", "Erling Haaland - Hat Trick", "Over Under Player Goals (incl. overtime)", "Haaland, Erling", "Over 2.5 (Haaland, Erling)")]
        // Players with "no", "under", or "will" substrings in their names
        [InlineData("Anytime Goalscorer", "Connor McDavid - Yes", "Over Under Player Goals (incl. overtime)", "McDavid, Connor", "Over 0.5 (McDavid, Connor)")]
        [InlineData("Anytime Goalscorer", "Connor McDavid - No", "Over Under Player Goals (incl. overtime)", "McDavid, Connor", "Under 0.5 (McDavid, Connor)")]
        [InlineData("Anytime Goalscorer", "Cristiano Ronaldo - Yes", "Over Under Player Goals (incl. overtime)", "Ronaldo, Cristiano", "Over 0.5 (Ronaldo, Cristiano)")]
        [InlineData("Anytime Goalscorer", "Bruno Fernandes - Yes", "Over Under Player Goals (incl. overtime)", "Fernandes, Bruno", "Over 0.5 (Fernandes, Bruno)")]
        [InlineData("Anytime Goalscorer", "Noni Madueke - Yes", "Over Under Player Goals (incl. overtime)", "Madueke, Noni", "Over 0.5 (Madueke, Noni)")]
        [InlineData("Anytime Goalscorer", "Antonio Nusa - Yes", "Over Under Player Goals (incl. overtime)", "Nusa, Antonio", "Over 0.5 (Nusa, Antonio)")]
        [InlineData("Anytime Goalscorer", "Romano Schmid - Yes (Power Sub)", "Over Under Player Goals (incl. overtime)", "Schmid, Romano", "Over 0.5 (Schmid, Romano)")]
        [InlineData("Will/Will not Score", "Will Smith - Will Score", "Over Under Player Goals (incl. overtime)", "Smith, Will", "Over 0.5 (Smith, Will)")]
        public void Test_FullDictionarySimulation_PlayerProps_AcrossBookmakerFormats(
            string marketName,
            string selection,
            string expectedOpMarket,
            string opPlayerName,
            string expectedOutcome)
        {
            var mockMarkets = new List<OddsPapiMarket>
            {
                new OddsPapiMarket { MarketId = "goals", MarketName = "Over Under Player Goals (incl. overtime)" },
                new OddsPapiMarket { MarketId = "assists", MarketName = "Over Under Player Assists (incl. overtime)" },
                new OddsPapiMarket { MarketId = "cards", MarketName = "Player To Be Carded (incl. overtime)" },
                new OddsPapiMarket { MarketId = "shots", MarketName = "Over Under Player Shots (incl. overtime)" },
                new OddsPapiMarket { MarketId = "sot", MarketName = "Over Under Player Shots On Goal (incl. overtime)" }
            };

            string matchName = "Home FC vs Away United";
            var bestMarket = OddsMatchingLogic.FindBestMarket(mockMarkets, marketName, matchName, selection, _marketMapper);
            Assert.NotNull(bestMarket);
            Assert.Equal(expectedOpMarket, bestMarket.MarketName);

            var leg = new AiVisionLeg
            {
                Match = matchName,
                Sport = "Soccer",
                Market = marketName,
                Selection = selection
            };
            var apiResult = new OddsPapiSearchResult { MatchName = matchName };
            var ctx = new OddsMatchingLogic.OutcomeMatchingContext(leg, apiResult, bestMarket.MarketName);

            var outcomesInOrder = new[]
            {
                $"Over 0.5 ({opPlayerName})",
                $"Under 0.5 ({opPlayerName})",
                $"Over 1.5 ({opPlayerName})",
                $"Under 1.5 ({opPlayerName})",
                $"Over 2.5 ({opPlayerName})",
                $"Under 2.5 ({opPlayerName})",
                $"Over 3.5 ({opPlayerName})",
                $"Under 3.5 ({opPlayerName})",
                "Over 0.5 (Other, Player)",
                "Under 0.5 (Other, Player)"
            };

            var matchedOutcomes = outcomesInOrder
                .Where(o => OddsMatchingLogic.IsOutcomeMatch(o, o, ctx))
                .ToList();

            Assert.Single(matchedOutcomes);
            Assert.Equal(expectedOutcome, matchedOutcomes[0]);
        }

        [Fact]
        public void Test_PlayerProp_PartialNameCollision_DoesNotMatchDifferentPlayerSharingOneName()
        {
            var apiResult = new OddsPapiSearchResult { MatchName = "Real Sociedad vs Manchester United" };

            // "Bruno Silva" must NOT match "Fernandes, Bruno"
            var legBruno = new AiVisionLeg { Match = apiResult.MatchName, Market = "Anytime Goalscorer", Selection = "Bruno Silva - Yes" };
            var ctxBruno = new OddsMatchingLogic.OutcomeMatchingContext(legBruno, apiResult, "Over Under Player Goals (incl. overtime)");
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("Over 0.5 (Fernandes, Bruno)", "Over 0.5 (Fernandes, Bruno)", ctxBruno));

            // "Alex Baena" must NOT match "Remiro, Alex"
            var legAlex = new AiVisionLeg { Match = apiResult.MatchName, Market = "Anytime Goalscorer", Selection = "Alex Baena - Yes" };
            var ctxAlex = new OddsMatchingLogic.OutcomeMatchingContext(legAlex, apiResult, "Over Under Player Goals (incl. overtime)");
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("Over 0.5 (Remiro, Alex)", "Over 0.5 (Remiro, Alex)", ctxAlex));

            // "Adam Fox" must NOT match "Fox, De'Aaron"
            var legFox = new AiVisionLeg { Match = apiResult.MatchName, Market = "Player Points", Selection = "Adam Fox - Over 0.5" };
            var ctxFox = new OddsMatchingLogic.OutcomeMatchingContext(legFox, apiResult, "Over Under Player Points (incl. overtime)");
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("Over 0.5 (Fox, De'Aaron)", "Over 0.5 (Fox, De'Aaron)", ctxFox));
        }

        [Fact]
        public void Test_MarketMapping_FoulsSufferedAndFirstGoalscorer_DoNotMapToWrongMarket()
        {
            var mockMarkets = new List<OddsPapiMarket>
            {
                new OddsPapiMarket { MarketId = "goals", MarketName = "Over Under Player Goals (incl. overtime)" },
                new OddsPapiMarket { MarketId = "fouls", MarketName = "Over Under Player Fouls Committed (incl. overtime)" }
            };

            // "Player Fouls Suffered" / "Total Fouls Won" must NOT map to "Over Under Player Fouls Committed"
            Assert.Null(OddsMatchingLogic.FindBestMarket(mockMarkets, "Player Fouls Suffered", "Roma vs Lazio", "Paulo Dybala - Over 1.5", _marketMapper));
            Assert.Null(OddsMatchingLogic.FindBestMarket(mockMarkets, "Kiernan Dewsbury-Hall Total Fouls Won", "Chelsea vs Arsenal", "Over 1.5", _marketMapper));

            // "First Goalscorer" / "Next Goalscorer (Goal 4)" must NOT map to "Over Under Player Goals (incl. overtime)" (Anytime Goalscorer)
            Assert.Null(OddsMatchingLogic.FindBestMarket(mockMarkets, "First Goalscorer", "Roma vs Lazio", "Paulo Dybala", _marketMapper));
            Assert.Null(OddsMatchingLogic.FindBestMarket(mockMarkets, "Last Goalscorer", "Roma vs Lazio", "Paulo Dybala", _marketMapper));
            Assert.Null(OddsMatchingLogic.FindBestMarket(mockMarkets, "Next Goalscorer (Goal 4) - Including Overtime", "Edmonton Oilers vs Calgary Flames", "Evan Bouchard", _marketMapper));
        }

        [Fact]
        public void Test_Bet1104_ComoVsManchesterUnited_DoubleChance_ComoSlashDraw()
        {
            var searchResult = new OddsPapiSearchResult
            {
                MatchName = "Como 1907 vs Manchester United"
            };

            var leg = new AiVisionLeg
            {
                Match = "Como vs Manchester United",
                Sport = "Soccer",
                Market = "Double Chance",
                Selection = "Como/Draw"
            };

            string display1X = OddsMatchingLogic.GetTranslatedOutcomeName("1X", searchResult.MatchName);
            Assert.Equal("Como 1907 or Draw", display1X);
            Assert.True(OddsMatchingLogic.IsOutcomeMatch("1X", display1X, leg, searchResult, "Double Chance Full Time"));

            string display12 = OddsMatchingLogic.GetTranslatedOutcomeName("12", searchResult.MatchName);
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("12", display12, leg, searchResult, "Double Chance Full Time"));

            string displayX2 = OddsMatchingLogic.GetTranslatedOutcomeName("X2", searchResult.MatchName);
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("X2", displayX2, leg, searchResult, "Double Chance Full Time"));

            string display2X = OddsMatchingLogic.GetTranslatedOutcomeName("2X", searchResult.MatchName);
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("2X", display2X, leg, searchResult, "Double Chance Full Time"));
        }

        [Theory]
        [InlineData("Como vs Manchester United", "Como 1907 vs Manchester United", "Como/Draw", "1X")]
        [InlineData("Como vs Manchester United", "Como 1907 vs Manchester United", "Draw/Como", "1X")]
        [InlineData("Como vs Manchester United", "Como 1907 vs Manchester United", "Como - Draw", "1X")]
        [InlineData("Como vs Manchester United", "Como 1907 vs Manchester United", "Como or Draw", "1X")]
        [InlineData("Como vs Manchester United", "Como 1907 vs Manchester United", "Como eller Uavgjort", "1X")]
        [InlineData("Como vs Manchester United", "Como 1907 vs Manchester United", "Como/Manchester United", "12")]
        [InlineData("Como vs Manchester United", "Como 1907 vs Manchester United", "Manchester United/Draw", "X2")]
        [InlineData("Como vs Manchester United", "Como 1907 vs Manchester United", "Draw/Manchester United", "X2")]
        [InlineData("Como vs Manchester United", "Como 1907 vs Manchester United", "1/X", "1X")]
        [InlineData("Como vs Manchester United", "Como 1907 vs Manchester United", "X/2", "X2")]
        [InlineData("Como vs Manchester United", "Como 1907 vs Manchester United", "1/2", "12")]
        [InlineData("Celta Vigo vs Juventus", "RC Celta de Vigo vs Juventus Turin", "Juventus/Draw", "X2")]
        [InlineData("Celta Vigo vs Juventus", "RC Celta de Vigo vs Juventus Turin", "Celta Vigo/Juventus", "12")]
        public void Test_DoubleChance_AllSeparatorFormatsAndTeamNameVariations(
            string legMatch,
            string opMatch,
            string selection,
            string expectedOpCode)
        {
            var searchResult = new OddsPapiSearchResult { MatchName = opMatch };
            var leg = new AiVisionLeg
            {
                Match = legMatch,
                Market = "Double Chance",
                Selection = selection
            };

            foreach (var code in new[] { "1X", "12", "X2" })
            {
                string display = OddsMatchingLogic.GetTranslatedOutcomeName(code, opMatch);
                bool matched = OddsMatchingLogic.IsOutcomeMatch(code, display, leg, searchResult, "Double Chance Full Time");
                Assert.Equal(code == expectedOpCode, matched);
            }
        }

        [Fact]
        public void Test_HalfTimeFullTime_SlashFormatStillMatchesCorrectly()
        {
            var searchResult = new OddsPapiSearchResult
            {
                MatchName = "Como 1907 vs Manchester United"
            };

            var leg = new AiVisionLeg
            {
                Match = "Como vs Manchester United",
                Market = "Half Time / Full Time",
                Selection = "Como/Draw"
            };

            string display1X = OddsMatchingLogic.GetTranslatedOutcomeName("1/X", searchResult.MatchName);
            Assert.True(OddsMatchingLogic.IsOutcomeMatch("1/X", display1X, leg, searchResult, "Half Time/Full Time"));

            string displayX1 = OddsMatchingLogic.GetTranslatedOutcomeName("X/1", searchResult.MatchName);
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("X/1", displayX1, leg, searchResult, "Half Time/Full Time"));
        }

        [Fact]
        public void Test_TeamSpecificMarketMapping_WithMultiTokenOpTeamNames()
        {
            var mockMarkets = new List<OddsPapiMarket>
            {
                new OddsPapiMarket { MarketId = "1", MarketName = "Over Under Team 1" },
                new OddsPapiMarket { MarketId = "2", MarketName = "Over Under Team 2" },
                new OddsPapiMarket { MarketId = "3", MarketName = "Corners - Over Under Team 1" },
                new OddsPapiMarket { MarketId = "4", MarketName = "Corners - Over Under Team 2" }
            };

            var comoGoals = OddsMatchingLogic.FindBestMarket(
                mockMarkets,
                "Como Total Goals",
                "Como vs Manchester United",
                "Over 1.5",
                _marketMapper,
                "Como 1907 vs Manchester United");
            Assert.NotNull(comoGoals);
            Assert.Equal("1", comoGoals.MarketId);

            var juveCorners = OddsMatchingLogic.FindBestMarket(
                mockMarkets,
                "Juventus Total Corners",
                "Celta Vigo vs Juventus",
                "Over 4.5",
                _marketMapper,
                "RC Celta de Vigo vs Juventus Turin");
            Assert.NotNull(juveCorners);
            Assert.Equal("4", juveCorners.MarketId);
        }

        [Theory]
        [InlineData("2nd Half Cards 1.5", "Bookings - Over Under Second Half")]
        [InlineData("2nd Half Total Cards", "Bookings - Over Under Second Half")]
        [InlineData("Second Half Cards", "Bookings - Over Under Second Half")]
        [InlineData("1st Half Cards 1.5", "Bookings - Over Under First Half")]
        [InlineData("1st Half Total Cards", "Bookings - Over Under First Half")]
        [InlineData("Total Cards 3.5", "Bookings - Over Under Full Time")]
        [InlineData("2nd Half Goals 1.5", "Over Under Second Half")]
        [InlineData("1st Half Goals 1.5", "Over Under First Half")]
        public void Test_Bet1105_HalfCardsAndGoalsMarkets_MatchExpectedOddsPapiMarket(string legMarket, string expectedOpMarket)
        {
            var mockMarkets = new List<OddsPapiMarket>
            {
                new OddsPapiMarket { MarketId = "b_ft", MarketName = "Bookings - Over Under Full Time" },
                new OddsPapiMarket { MarketId = "b_1h", MarketName = "Bookings - Over Under First Half" },
                new OddsPapiMarket { MarketId = "b_2h", MarketName = "Bookings - Over Under Second Half" },
                new OddsPapiMarket { MarketId = "g_ft", MarketName = "Over Under Full Time" },
                new OddsPapiMarket { MarketId = "g_1h", MarketName = "Over Under First Half" },
                new OddsPapiMarket { MarketId = "g_2h", MarketName = "Over Under Second Half" },
                new OddsPapiMarket { MarketId = "p_card", MarketName = "Player To Be Carded (incl. overtime)" }
            };

            var best = OddsMatchingLogic.FindBestMarket(
                mockMarkets,
                legMarket,
                "Como vs Manchester United",
                "Over 1.5",
                _marketMapper,
                "Como 1907 vs Manchester United");

            Assert.NotNull(best);
            Assert.Equal(expectedOpMarket, best.MarketName);
        }

        [Theory]
        [InlineData("Al-Anwar Club", "AL Anwar")]
        [InlineData("Al-Wahda Mecca", "Al-Wehda FC")]
        [InlineData("Al-Wehda Makkah", "Al-Wehda FC")]
        [InlineData("Al-Ettifaq Dammam", "Al-Ittifaq FC")]
        [InlineData("Al-Ettifaq", "Al-Ittifaq FC")]
        [InlineData("Al-Taawon Buraidah", "Al-Taawoun FC")]
        [InlineData("Al-Okhdood Najran", "Al-Okhdood Club")]
        [InlineData("Al-Akhdoud", "Al-Okhdood Club")]
        [InlineData("Al-Hazem Ar Rass", "Al-Hazm")]
        [InlineData("Al-Bukayriyah", "Al Bukiryah")]
        [InlineData("Al-Urubah", "Al-Orobah")]
        [InlineData("Al-Feiha", "Al-Fayha FC")]
        [InlineData("Al-Ahli Jeddah", "Al Ahli Saudi FC")]
        [InlineData("Al-Shabab Riyadh", "Al-Shabab FC (SA)")]
        [InlineData("Al-Hilal Riyadh", "Al Hilal SFC")]
        [InlineData("Al-Qadisiyah Khobar", "Al Qadsiah")]
        [InlineData("Al-Draih", "Diriyah Club")]
        public void Test_Bet1106_SaudiTeamMatching_HandlesTransliterationsAndCityQualifiers(string betslipTeam, string oddsPapiTeam)
        {
            var aliasService = new TeamAliasMappingService();
            Assert.True(TeamAliasMappingService.IsTeamMatch(betslipTeam, oddsPapiTeam), $"Expected '{betslipTeam}' to match '{oddsPapiTeam}'");

            string normBetslip = aliasService.NormalizeTeamName(betslipTeam, removeStopWords: true);
            string normOp = aliasService.NormalizeTeamName(oddsPapiTeam, removeStopWords: true);
            Assert.True(
                normBetslip.Contains(normOp, StringComparison.OrdinalIgnoreCase) ||
                normOp.Contains(normBetslip, StringComparison.OrdinalIgnoreCase),
                $"Expected normalized '{normBetslip}' and '{normOp}' to match");
        }

        [Fact]
        public void Test_Bet1107_1stHalfGoalsHandicap_MatchesAsianHandicapFirstHalfAndOutcome()
        {
            var mockMarkets = new List<OddsPapiMarket>
            {
                new OddsPapiMarket { MarketId = "ou_t1_1h", MarketName = "Over Under Team 1 First Half" },
                new OddsPapiMarket { MarketId = "ou_1h", MarketName = "Over Under First Half" },
                new OddsPapiMarket { MarketId = "ah_ft", MarketName = "Asian Handicap" },
                new OddsPapiMarket
                {
                    MarketId = "ah_1h",
                    MarketName = "Asian Handicap First Half",
                    OutcomeNames = new Dictionary<string, string>
                    {
                        ["1"] = "1 (-0.5)",
                        ["2"] = "1 (-1.0)",
                        ["3"] = "2 (-0.5)",
                        ["4"] = "2 (-1.0)"
                    }
                },
                new OddsPapiMarket { MarketId = "3w_1h", MarketName = "3-Way Handicap First Half" }
            };

            var bestMarket = OddsMatchingLogic.FindBestMarket(
                mockMarkets,
                "1st Half Goals Handicap 0 - 1",
                "Aston Villa vs Viking",
                "Aston Villa -1.0",
                _marketMapper,
                "Aston Villa vs Viking FK");

            Assert.NotNull(bestMarket);
            Assert.Equal("Asian Handicap First Half", bestMarket.MarketName);

            var searchResult = new OddsPapiSearchResult { MatchName = "Aston Villa vs Viking FK" };
            var leg = new AiVisionLeg
            {
                Match = "Aston Villa vs Viking",
                Sport = "Soccer",
                Market = "1st Half Goals Handicap 0 - 1",
                Selection = "Aston Villa -1.0"
            };

            string displayMatch = OddsMatchingLogic.GetTranslatedOutcomeName("1 (-1.0)", searchResult.MatchName);
            Assert.True(OddsMatchingLogic.IsOutcomeMatch("1 (-1.0)", displayMatch, leg, searchResult, bestMarket.MarketName));

            string displayWrongLine = OddsMatchingLogic.GetTranslatedOutcomeName("1 (-0.5)", searchResult.MatchName);
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("1 (-0.5)", displayWrongLine, leg, searchResult, bestMarket.MarketName));

            string displayWrongTeam = OddsMatchingLogic.GetTranslatedOutcomeName("2 (-1.0)", searchResult.MatchName);
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("2 (-1.0)", displayWrongTeam, leg, searchResult, bestMarket.MarketName));
        }

        [Theory]
        [InlineData("Soccer", 10)]
        [InlineData("Ice Hockey", 15)]
        [InlineData("Tennis", 12)]
        [InlineData("Table Tennis", -1)]
        [InlineData("Bordtennis", -1)]
        [InlineData("American Football", -1)]
        [InlineData("NFL", -1)]
        [InlineData("Basketball", -1)]
        public void ResolveSportId_MapsSportsCorrectly(string sport, int expectedSportId)
        {
            Assert.Equal(expectedSportId, OddsApiService.ResolveSportId(sport));
        }

        [Theory]
        [InlineData("Cobolli, Flavio", "Flavio Cobolli", true)]
        [InlineData("Fils, Arthur", "Arthur Fils", true)]
        [InlineData("Struff, Jan-Lennard", "Jan-Lennard Struff", true)]
        [InlineData("Auger-Aliassime, Felix", "F. Auger-Aliassime", true)]
        [InlineData("Medvedev, D", "Daniil Medvedev", true)]
        [InlineData("Wawrinka, Stanislas", "Stan Wawrinka", true)]
        [InlineData("de Minaur, Alex", "Alex De Minaur", true)]
        [InlineData("Li, Ann", "Ann Li", true)]
        [InlineData("Li, A", "Ann Li", true)]
        [InlineData("Li, Ann", "Lin Zhu", false)]
        [InlineData("Cerundolo, Francisco", "Francisco Cerundolo", true)]
        [InlineData("Cerundolo, F", "Francisco Cerundolo", true)]
        [InlineData("Cerundolo, Juan Manuel", "Francisco Cerundolo", false)]
        [InlineData("Cerundolo, J", "Francisco Cerundolo", false)]
        [InlineData("Fruhvirtova, Linda", "Brenda Fruhvirtova", false)]
        [InlineData("Collignon R / Goffin D", "R. Collignon/D. Goffin", true)]
        [InlineData("Krawietz K / Puetz T", "K. Krawietz/T. Puetz", true)]
        [InlineData("Collignon R / Goffin D", "K. Krawietz/T. Puetz", false)]
        public void Tennis_PlayerAndDoublesNameMatching_WorksCorrectly(string opParticipant, string betslipName, bool expected)
        {
            Assert.Equal(expected, TeamAliasMappingService.IsTeamMatch(opParticipant, betslipName));
            Assert.Equal(expected, TeamAliasMappingService.IsTeamMatch(betslipName, opParticipant));
        }

        [Fact]
        public void Tennis_MatchWinner_And_SwappedParticipants_MatchCorrectly()
        {
            var tennisMarkets = new List<OddsPapiMarket>
            {
                new OddsPapiMarket { MarketId = "1201", MarketName = "Winner" },
                new OddsPapiMarket { MarketId = "1202", MarketName = "First Set Winner" },
                new OddsPapiMarket { MarketId = "1203", MarketName = "Set Handicap" },
                new OddsPapiMarket { MarketId = "1204", MarketName = "Game Handicap" },
                new OddsPapiMarket { MarketId = "1205", MarketName = "Total Games Over Under" }
            };

            foreach (var rawMkt in new[] { "Match Odds", "Match Winner", "Match Result", "Moneyline", "To Win", "To Win Match", "Head to Head" })
            {
                var best = OddsMatchingLogic.FindBestMarket(
                    tennisMarkets,
                    rawMkt,
                    "Carlos Alcaraz vs Arthur Fils",
                    "Arthur Fils",
                    _marketMapper,
                    "Fils, Arthur vs Alcaraz, Carlos");

                Assert.NotNull(best);
                Assert.Equal("Winner", best.MarketName);
            }

            // Swapped fixture order: AI has "Carlos Alcaraz vs Arthur Fils", OddsPapi has "Fils, Arthur vs Alcaraz, Carlos"
            var searchResult = new OddsPapiSearchResult { MatchName = "Fils, Arthur vs Alcaraz, Carlos" };
            var leg = new AiVisionLeg
            {
                Sport = "Tennis",
                Match = "Carlos Alcaraz vs Arthur Fils",
                Market = "Match Odds",
                Selection = "Arthur Fils"
            };

            string disp1 = OddsMatchingLogic.GetTranslatedOutcomeName("1", searchResult.MatchName);
            string disp2 = OddsMatchingLogic.GetTranslatedOutcomeName("2", searchResult.MatchName);

            Assert.True(OddsMatchingLogic.IsOutcomeMatch("1", disp1, leg, searchResult, "Winner"));
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("2", disp2, leg, searchResult, "Winner"));
        }

        [Fact]
        public void Tennis_WinnerTarget_DoesNotSubstringMatchSetWinnerOrHockeyOvertimeWinner()
        {
            // 1. If a Tennis fixture only has "First Set Winner" and "Game Winner" (no match "Winner"),
            // a "Match Winner" bet must NOT match "First Set Winner" or "Game Winner".
            var partialTennisMarkets = new List<OddsPapiMarket>
            {
                new OddsPapiMarket { MarketId = "1202", MarketName = "First Set Winner" },
                new OddsPapiMarket { MarketId = "1299", MarketName = "Game Winner" }
            };

            var badMatch = OddsMatchingLogic.FindBestMarket(
                partialTennisMarkets,
                "Match Odds",
                "Carlos Alcaraz vs Arthur Fils",
                "Carlos Alcaraz",
                _marketMapper);
            Assert.Null(badMatch);

            // 2. In Ice Hockey, "Match Odds" (Regular Time 3-way) must NOT match "Winner (incl. overtime and penalties)"
            var hockeyMarketsWithoutRegTime = new List<OddsPapiMarket>
            {
                new OddsPapiMarket { MarketId = "1501", MarketName = "Winner (incl. overtime and penalties)" }
            };

            var badHockeyMatch = OddsMatchingLogic.FindBestMarket(
                hockeyMarketsWithoutRegTime,
                "Match Odds",
                "Toronto Maple Leafs vs Montreal Canadiens",
                "Toronto Maple Leafs",
                _marketMapper);
            Assert.Null(badHockeyMatch);
        }

        [Fact]
        public void Tennis_SetWinner_And_SpecificGameWinnerGuard_WorkCorrectly()
        {
            var tennisMarkets = new List<OddsPapiMarket>
            {
                new OddsPapiMarket { MarketId = "1201", MarketName = "Winner" },
                new OddsPapiMarket { MarketId = "1202", MarketName = "First Set Winner" },
                new OddsPapiMarket { MarketId = "1203", MarketName = "Second Set Winner" },
                new OddsPapiMarket { MarketId = "1204", MarketName = "Total Games First Set" }
            };

            var m1 = OddsMatchingLogic.FindBestMarket(tennisMarkets, "1st Set Winner", "Sonay Kartal vs Madison Keys", "Madison Keys", _marketMapper);
            Assert.NotNull(m1);
            Assert.Equal("First Set Winner", m1.MarketName);

            var m2 = OddsMatchingLogic.FindBestMarket(tennisMarkets, "Set 2 Winner", "Frances Tiafoe vs Flavio Cobolli", "Flavio Cobolli", _marketMapper);
            Assert.NotNull(m2);
            Assert.Equal("Second Set Winner", m2.MarketName);

            // Specific point-by-point game in a set ("1st Set - 4th Game Winner", "2nd Set - 4th Game Winner")
            // must NOT falsely resolve to "First Set Winner" or "Second Set Winner"
            var mGame1 = OddsMatchingLogic.FindBestMarket(tennisMarkets, "1st Set - 4th Game Winner", "Sonay Kartal vs Madison Keys", "Madison Keys", _marketMapper);
            Assert.Null(mGame1);

            var mGame2 = OddsMatchingLogic.FindBestMarket(tennisMarkets, "2nd Set - 4th Game Winner", "Frances Tiafoe vs Flavio Cobolli", "Flavio Cobolli", _marketMapper);
            Assert.Null(mGame2);
        }

        [Fact]
        public void Tennis_SetHandicap_And_GameHandicap_MatchCorrectly()
        {
            var tennisMarkets = new List<OddsPapiMarket>
            {
                new OddsPapiMarket { MarketId = "1201", MarketName = "Winner" },
                new OddsPapiMarket { MarketId = "1203", MarketName = "Set Handicap" },
                new OddsPapiMarket { MarketId = "1204", MarketName = "Game Handicap" },
                new OddsPapiMarket { MarketId = "1205", MarketName = "Game Handicap First Set" }
            };

            var setHcMkt = OddsMatchingLogic.FindBestMarket(tennisMarkets, "Set Handicap", "Karen Khachanov vs Jenson Brooksby", "Karen Khachanov -1.5", _marketMapper, "Khachanov, Karen vs Brooksby, Jenson");
            Assert.NotNull(setHcMkt);
            Assert.Equal("Set Handicap", setHcMkt.MarketName);

            var gameHcMkt = OddsMatchingLogic.FindBestMarket(tennisMarkets, "Game Handicap", "Karen Khachanov vs Jenson Brooksby", "Jenson Brooksby +3.5", _marketMapper, "Khachanov, Karen vs Brooksby, Jenson");
            Assert.NotNull(gameHcMkt);
            Assert.Equal("Game Handicap", gameHcMkt.MarketName);

            var set1GameHcMkt = OddsMatchingLogic.FindBestMarket(tennisMarkets, "1st Set Game Handicap", "Karen Khachanov vs Jenson Brooksby", "Karen Khachanov -1.5", _marketMapper, "Khachanov, Karen vs Brooksby, Jenson");
            Assert.NotNull(set1GameHcMkt);
            Assert.Equal("Game Handicap First Set", set1GameHcMkt.MarketName);

            var searchResult = new OddsPapiSearchResult { MatchName = "Khachanov, Karen vs Brooksby, Jenson" };

            // Participant 1 -1.5 Sets -> in OddsPapi hc=-1.5: "1 (-1.5)" is P1 -1.5, "2 (-1.5)" is P2 +1.5
            var legP1Minus = new AiVisionLeg
            {
                Sport = "Tennis",
                Match = "Karen Khachanov vs Jenson Brooksby",
                Market = "Set Handicap",
                Selection = "Karen Khachanov -1.5 Sets"
            };
            Assert.True(OddsMatchingLogic.IsOutcomeMatch("1 (-1.5)", OddsMatchingLogic.GetTranslatedOutcomeName("1 (-1.5)", searchResult.MatchName), legP1Minus, searchResult, "Set Handicap"));
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("1 (1.5)", OddsMatchingLogic.GetTranslatedOutcomeName("1 (1.5)", searchResult.MatchName), legP1Minus, searchResult, "Set Handicap"));
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("2 (-1.5)", OddsMatchingLogic.GetTranslatedOutcomeName("2 (-1.5)", searchResult.MatchName), legP1Minus, searchResult, "Set Handicap"));

            // Participant 2 +3.5 Games -> in OddsPapi hc=-3.5: "2 (-3.5)" is P2 +3.5
            var legP2Plus = new AiVisionLeg
            {
                Sport = "Tennis",
                Match = "Karen Khachanov vs Jenson Brooksby",
                Market = "Game Handicap",
                Selection = "Jenson Brooksby +3.5 Games"
            };
            Assert.True(OddsMatchingLogic.IsOutcomeMatch("2 (-3.5)", OddsMatchingLogic.GetTranslatedOutcomeName("2 (-3.5)", searchResult.MatchName), legP2Plus, searchResult, "Game Handicap"));
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("2 (3.5)", OddsMatchingLogic.GetTranslatedOutcomeName("2 (3.5)", searchResult.MatchName), legP2Plus, searchResult, "Game Handicap"));
        }

        [Fact]
        public void Tennis_TotalGames_MatchSetAndParticipant_MatchCorrectly()
        {
            var tennisMarkets = new List<OddsPapiMarket>
            {
                new OddsPapiMarket { MarketId = "1210", MarketName = "Total Games Over Under" },
                new OddsPapiMarket { MarketId = "1211", MarketName = "Total Games First Set" },
                new OddsPapiMarket { MarketId = "1212", MarketName = "Participant 1 Total Games" },
                new OddsPapiMarket { MarketId = "1213", MarketName = "Participant 2 Total Games" }
            };

            var searchResult = new OddsPapiSearchResult { MatchName = "Cobolli, Flavio vs Tiafoe, Frances" };

            // 1. Match Total Games
            var mTotal = OddsMatchingLogic.FindBestMarket(tennisMarkets, "Total Games", "Flavio Cobolli vs Frances Tiafoe", "Over 22.5", _marketMapper, searchResult.MatchName);
            Assert.NotNull(mTotal);
            Assert.Equal("Total Games Over Under", mTotal.MarketName);

            var legTotal = new AiVisionLeg { Sport = "Tennis", Match = "Flavio Cobolli vs Frances Tiafoe", Market = "Total Games", Selection = "Over 22.5 Games" };
            Assert.True(OddsMatchingLogic.IsOutcomeMatch("Over 22.5", "Over 22.5", legTotal, searchResult, "Total Games Over Under"));
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("Under 22.5", "Under 22.5", legTotal, searchResult, "Total Games Over Under"));
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("Over 22.5", "Over 22.5", legTotal, searchResult, "Participant 1 Total Games"));

            // 2. First Set Total Games
            var mSet1 = OddsMatchingLogic.FindBestMarket(tennisMarkets, "1st Set Total Games", "Flavio Cobolli vs Frances Tiafoe", "Over 9.5", _marketMapper, searchResult.MatchName);
            Assert.NotNull(mSet1);
            Assert.Equal("Total Games First Set", mSet1.MarketName);

            // 3. Participant 2 Total Games (Frances Tiafoe)
            var mP2 = OddsMatchingLogic.FindBestMarket(tennisMarkets, "Player Total Games", "Flavio Cobolli vs Frances Tiafoe", "Frances Tiafoe - Over 12.5", _marketMapper, searchResult.MatchName);
            Assert.NotNull(mP2);
            Assert.Equal("Participant 2 Total Games", mP2.MarketName);

            var legP2 = new AiVisionLeg { Sport = "Tennis", Match = "Flavio Cobolli vs Frances Tiafoe", Market = "Player Total Games", Selection = "Frances Tiafoe - Over 12.5" };
            Assert.True(OddsMatchingLogic.IsOutcomeMatch("Over 12.5", "Over 12.5", legP2, searchResult, "Participant 2 Total Games"));
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("Over 12.5", "Over 12.5", legP2, searchResult, "Participant 1 Total Games"));
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("Over 12.5", "Over 12.5", legP2, searchResult, "Total Games Over Under"));
        }

        [Fact]
        public void Tennis_TotalSets_And_ExactSets_MatchCorrectly()
        {
            var tennisMarkets = new List<OddsPapiMarket>
            {
                new OddsPapiMarket { MarketId = "1220", MarketName = "Total Sets Over Under" },
                new OddsPapiMarket { MarketId = "1221", MarketName = "Exact Sets" }
            };

            var searchResult = new OddsPapiSearchResult { MatchName = "Alcaraz, Carlos vs Medvedev, Daniil" };

            // 1. Over 2.5 Sets -> Total Sets Over Under
            var mOverUnder = OddsMatchingLogic.FindBestMarket(tennisMarkets, "Total Sets", "Carlos Alcaraz vs Daniil Medvedev", "Over 2.5", _marketMapper, searchResult.MatchName);
            Assert.NotNull(mOverUnder);
            Assert.Equal("Total Sets Over Under", mOverUnder.MarketName);

            var legOu = new AiVisionLeg { Sport = "Tennis", Match = "Carlos Alcaraz vs Daniil Medvedev", Market = "Total Sets", Selection = "Over 2.5 Sets" };
            Assert.True(OddsMatchingLogic.IsOutcomeMatch("Over 2.5", "Over 2.5", legOu, searchResult, "Total Sets Over Under"));
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("2", OddsMatchingLogic.GetTranslatedOutcomeName("2", searchResult.MatchName), legOu, searchResult, "Exact Sets"));

            // 2. "2 Sets" / "3 Sets" -> Exact Sets (and outcome "2" must NOT be mistaken for Participant 2!)
            var mExact = OddsMatchingLogic.FindBestMarket(tennisMarkets, "Total Sets", "Carlos Alcaraz vs Daniil Medvedev", "2 Sets", _marketMapper, searchResult.MatchName);
            Assert.NotNull(mExact);
            Assert.Equal("Exact Sets", mExact.MarketName);

            var legExact2 = new AiVisionLeg { Sport = "Tennis", Match = "Carlos Alcaraz vs Daniil Medvedev", Market = "Total Sets", Selection = "2 Sets" };
            Assert.True(OddsMatchingLogic.IsOutcomeMatch("2", OddsMatchingLogic.GetTranslatedOutcomeName("2", searchResult.MatchName), legExact2, searchResult, "Exact Sets"));
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("3", OddsMatchingLogic.GetTranslatedOutcomeName("3", searchResult.MatchName), legExact2, searchResult, "Exact Sets"));
        }

        [Fact]
        public void Tennis_SetBetting_CorrectScore_MatchesPureAndPlayerNamedScores()
        {
            var tennisMarkets = new List<OddsPapiMarket>
            {
                new OddsPapiMarket { MarketId = "1230", MarketName = "Correct Score" },
                new OddsPapiMarket { MarketId = "1231", MarketName = "Correct Score First Set" }
            };

            var mSetBetting = OddsMatchingLogic.FindBestMarket(tennisMarkets, "Set Betting", "Learner Tien vs Jiri Lehecka", "Jiri Lehecka 2-0", _marketMapper, "Tien, Learner vs Lehecka, Jiri");
            Assert.NotNull(mSetBetting);
            Assert.Equal("Correct Score", mSetBetting.MarketName);

            var searchResult = new OddsPapiSearchResult { MatchName = "Tien, Learner vs Lehecka, Jiri" };

            // 1. Player-named Set Betting: "Jiri Lehecka 2-0" where Jiri Lehecka is Participant 2 -> matches "0:2"
            var legNamedP2 = new AiVisionLeg
            {
                Sport = "Tennis",
                Match = "Learner Tien vs Jiri Lehecka",
                Market = "Set Betting",
                Selection = "Jiri Lehecka 2-0"
            };
            Assert.True(OddsMatchingLogic.IsOutcomeMatch("0:2", "0:2", legNamedP2, searchResult, "Correct Score"));
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("2:0", "2:0", legNamedP2, searchResult, "Correct Score"));

            // 2. Pure scoreline: "1-2" -> matches "1:2" when not swapped, and "2:1" when fixture participants are swapped
            var legPureScore = new AiVisionLeg
            {
                Sport = "Tennis",
                Match = "Learner Tien vs Jiri Lehecka",
                Market = "Correct Score",
                Selection = "1-2"
            };
            Assert.True(OddsMatchingLogic.IsOutcomeMatch("1:2", "1:2", legPureScore, searchResult, "Correct Score"));
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("2:1", "2:1", legPureScore, searchResult, "Correct Score"));

            var swappedSearchResult = new OddsPapiSearchResult { MatchName = "Lehecka, Jiri vs Tien, Learner" };
            Assert.True(OddsMatchingLogic.IsOutcomeMatch("2:1", "2:1", legPureScore, swappedSearchResult, "Correct Score"));
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("1:2", "1:2", legPureScore, swappedSearchResult, "Correct Score"));
        }

        [Fact]
        public void Tennis_TieBreak_Aces_DoubleFaults_And_ToWinASet_MatchCorrectly()
        {
            var tennisMarkets = new List<OddsPapiMarket>
            {
                new OddsPapiMarket { MarketId = "1240", MarketName = "Total Tie Breaks" },
                new OddsPapiMarket { MarketId = "1241", MarketName = "Tie Break First Set" },
                new OddsPapiMarket { MarketId = "1242", MarketName = "Total Aces" },
                new OddsPapiMarket { MarketId = "1243", MarketName = "Participant 1 Total Aces" },
                new OddsPapiMarket { MarketId = "1244", MarketName = "Participant 2 Total Double Faults" },
                new OddsPapiMarket { MarketId = "1245", MarketName = "Participant 1 To Win a Set" },
                new OddsPapiMarket { MarketId = "1246", MarketName = "Both Participants To Win a Set" }
            };

            var searchResult = new OddsPapiSearchResult { MatchName = "Cobolli, Flavio vs Tiafoe, Frances" };

            // Tie Break Yes/No -> Over 0.5 / Under 0.5
            var mTb = OddsMatchingLogic.FindBestMarket(tennisMarkets, "Tie Break in Match", "Flavio Cobolli vs Frances Tiafoe", "Yes", _marketMapper, searchResult.MatchName);
            Assert.NotNull(mTb);
            Assert.Equal("Total Tie Breaks", mTb.MarketName);

            var legTbYes = new AiVisionLeg { Sport = "Tennis", Match = "Flavio Cobolli vs Frances Tiafoe", Market = "Tie Break in Match", Selection = "Yes" };
            Assert.True(OddsMatchingLogic.IsOutcomeMatch("Over 0.5", "Over 0.5", legTbYes, searchResult, "Total Tie Breaks"));
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("Under 0.5", "Under 0.5", legTbYes, searchResult, "Total Tie Breaks"));
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("Over 1.5", "Over 1.5", legTbYes, searchResult, "Total Tie Breaks"));

            // Participant 1 Total Aces
            var mP1Aces = OddsMatchingLogic.FindBestMarket(tennisMarkets, "Player Aces", "Flavio Cobolli vs Frances Tiafoe", "Flavio Cobolli Over 6.5", _marketMapper, searchResult.MatchName);
            Assert.NotNull(mP1Aces);
            Assert.Equal("Participant 1 Total Aces", mP1Aces.MarketName);

            var legP1Aces = new AiVisionLeg { Sport = "Tennis", Match = "Flavio Cobolli vs Frances Tiafoe", Market = "Player Aces", Selection = "Flavio Cobolli Over 6.5" };
            Assert.True(OddsMatchingLogic.IsOutcomeMatch("Over 6.5", "Over 6.5", legP1Aces, searchResult, "Participant 1 Total Aces"));
            Assert.False(OddsMatchingLogic.IsOutcomeMatch("Over 6.5", "Over 6.5", legP1Aces, searchResult, "Total Aces"));

            // Participant 1 To Win a Set
            var mWinSet = OddsMatchingLogic.FindBestMarket(tennisMarkets, "To Win a Set", "Flavio Cobolli vs Frances Tiafoe", "Flavio Cobolli - Yes", _marketMapper, searchResult.MatchName);
            Assert.NotNull(mWinSet);
            Assert.Equal("Participant 1 To Win a Set", mWinSet.MarketName);

            // Doubles pair with '&' is not falsely flagged as Combination Market
            Assert.False(MarketMappingService.IsCombinationMarket("Match Winner", "R. Collignon/D. Goffin vs K. Krawietz/T. Puetz"));
            Assert.False(MarketMappingService.IsCombinationMarket("R. Collignon & D. Goffin", "R. Collignon/D. Goffin vs K. Krawietz/T. Puetz"));
        }
    }
}
