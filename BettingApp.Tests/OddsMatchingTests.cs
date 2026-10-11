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
    }
}
