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
    }
}
