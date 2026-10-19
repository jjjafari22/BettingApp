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
    }
}
