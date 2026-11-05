using System;
using System.Collections.Generic;
using Xunit;
using BettingApp.Data;
using BettingApp.Services;

namespace BettingApp.Tests
{
    public class AiVisionExtractionTests
    {
        [Fact]
        public void GetAllScreenshotUrls_SingleAndMergedScreenshots_ReturnsOrderedUniqueUrls()
        {
            var singleBet = new Bet
            {
                ScreenshotUrl = "https://blob.core.windows.net/uploads/ss1.jpg",
                ExtraScreenshotUrls = null
            };
            Assert.False(singleBet.HasExtraScreenshots);
            Assert.Equal(new[] { "https://blob.core.windows.net/uploads/ss1.jpg" }, singleBet.GetAllScreenshotUrls());

            var mergedBet = new Bet
            {
                ScreenshotUrl = "https://blob.core.windows.net/uploads/ss1.jpg",
                ExtraScreenshotUrls = "https://blob.core.windows.net/uploads/ss2.jpg\nhttps://blob.core.windows.net/uploads/ss1.jpg\nhttps://blob.core.windows.net/uploads/ss3.jpg"
            };
            Assert.True(mergedBet.HasExtraScreenshots);
            Assert.Equal(
                new[]
                {
                    "https://blob.core.windows.net/uploads/ss1.jpg",
                    "https://blob.core.windows.net/uploads/ss2.jpg",
                    "https://blob.core.windows.net/uploads/ss3.jpg"
                },
                mergedBet.GetAllScreenshotUrls());
        }

        [Fact]
        public void CombineMultiScreenshotResults_DeduplicatesOverlappingLegsAndSelectsTrueStakeAndOdds()
        {
            var start1 = new DateTime(2026, 11, 5, 18, 0, 0, DateTimeKind.Utc);
            var start2 = new DateTime(2026, 11, 5, 20, 0, 0, DateTimeKind.Utc);

            // Screenshot 1: top of parlay slip (no stake footer visible, AI accidentally grabbed 1.85 as totalOdds)
            var ss1 = new AiVisionExtractionResult
            {
                Bookmaker = "Unibet",
                IsBetBuilder = false,
                IsLive = false,
                Stake = "",
                TotalOdds = "1.85",
                Legs = new List<AiVisionLeg>
                {
                    new() { Match = "Arsenal vs Chelsea", Sport = "Soccer", Market = "Match Result", Selection = "Arsenal", Odds = "1.85" },
                    // Overlapping leg scrolled across both screenshots (missing StartTime on ss1, populated on ss2)
                    new() { Match = "Real Madrid vs Barcelona", Sport = "Soccer", Market = "Both Teams To Score", Selection = "Yes", Odds = "1.65" }
                }
            };

            // Screenshot 2: bottom of parlay slip with overlapping leg + Bet Builder multi-selection + true stake/odds footer
            var ss2 = new AiVisionExtractionResult
            {
                Bookmaker = "Unknown",
                IsBetBuilder = true,
                IsLive = false,
                Stake = "500 kr",
                TotalOdds = "14.53",
                Legs = new List<AiVisionLeg>
                {
                    new() { Match = "  Real Madrid   vs Barcelona ", Sport = "Soccer", Market = "both teams to score", Selection = "YES", Odds = "1.65", StartTime = start1 },
                    new() { Match = "Bayern vs Dortmund", Sport = "Soccer", Market = "Match Result", Selection = "Bayern", Odds = "", StartTime = start2 },
                    new() { Match = "Bayern vs Dortmund", Sport = "Soccer", Market = "Total Goals", Selection = "Over 2.5", Odds = "2.40", StartTime = start2 }
                }
            };

            var combined = AiVisionExtractionResult.CombineMultiScreenshotResults(new[] { ss1, ss2 });

            Assert.NotNull(combined);
            Assert.Equal("Unibet", combined!.Bookmaker);
            Assert.True(combined.IsBetBuilder);
            Assert.Equal("500 kr", combined.Stake);
            Assert.Equal("14.53", combined.TotalOdds);
            Assert.Equal(500, AiVisionExtractionResult.TryParseStakeNok(combined.Stake));

            // 4 unique legs: Arsenal, Real/Barca (deduplicated once!), and both distinct Bet Builder legs for Bayern/Dortmund
            Assert.Equal(4, combined.Legs.Count);
            Assert.Equal("Arsenal vs Chelsea", combined.Legs[0].Match);
            Assert.Equal("Real Madrid vs Barcelona", combined.Legs[1].Match);
            Assert.Equal(start1, combined.Legs[1].StartTime);
            Assert.Equal("Match Result", combined.Legs[2].Market);
            Assert.Equal("Total Goals", combined.Legs[3].Market);
        }

        [Fact]
        public void CombineMultiScreenshotResults_AbsorbsCutOffMatchlessEdgeLegsAndDropsHeaderOnlyGhosts()
        {
            // Screenshot 1 (like Bet #1285): 5 full legs + 6th leg at bottom where match header was cut off
            var ss1 = new AiVisionExtractionResult
            {
                Bookmaker = "Coolbet",
                Legs = new List<AiVisionLeg>
                {
                    new() { Match = "Borussia Dortmund vs Real Betis", Sport = "Soccer", Market = "Total Goals 3.5", Selection = "Under 3.5", Odds = "1.61" },
                    new() { Match = "", Sport = "Soccer", Market = "Total Goals 3.5", Selection = "Under 3.5", Odds = "1.40" }
                }
            };

            // Screenshot 2 (like Bet #1284 / #1280): cropped header-only ghost at top + full legs including FC Porto vs Napoli
            var ss2 = new AiVisionExtractionResult
            {
                Bookmaker = "Coolbet",
                Stake = "2000",
                TotalOdds = "46.84",
                Legs = new List<AiVisionLeg>
                {
                    new() { Match = "RB Leipzig vs Manchester City", Sport = "Soccer", Market = "", Selection = "", Odds = "" },
                    new() { Match = "Borussia Dortmund vs Real Betis", Sport = "Soccer", Market = "Total Goals 3.5", Selection = "Under 3.5", Odds = "1.61" },
                    new() { Match = "FC Porto vs Napoli", Sport = "Soccer", Market = "Total Goals 3.5", Selection = "Under 3.5", Odds = "1.40" }
                }
            };

            var combined = AiVisionExtractionResult.CombineMultiScreenshotResults(new[] { ss1, ss2 });

            Assert.NotNull(combined);
            // Only the 2 real complete legs remain: the header-only ghost and the matchless duplicate of FC Porto vs Napoli are dropped
            Assert.Equal(2, combined!.Legs.Count);
            Assert.Equal("Borussia Dortmund vs Real Betis", combined.Legs[0].Match);
            Assert.Equal("FC Porto vs Napoli", combined.Legs[1].Match);
            Assert.Equal("1.40", combined.Legs[1].Odds);
        }
    }
}
