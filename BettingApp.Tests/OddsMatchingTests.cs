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
    }
}
