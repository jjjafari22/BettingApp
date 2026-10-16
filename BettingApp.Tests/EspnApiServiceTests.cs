using System;
using Xunit;
using BettingApp.Services;

namespace BettingApp.Tests
{
    public class EspnApiServiceTests
    {
        [Theory]
        [InlineData("American Football", "football", "nfl")]
        [InlineData("NFL", "football", "nfl")]
        [InlineData("Football", "football", "nfl")]
        [InlineData("Ice Hockey", "hockey", "nhl")]
        [InlineData("NHL", "hockey", "nhl")]
        [InlineData("Basketball", "basketball", "nba")]
        [InlineData("NBA", "basketball", "nba")]
        [InlineData("Baseball", "baseball", "mlb")]
        [InlineData("MLB", "baseball", "mlb")]
        [InlineData("Tennis", "tennis", "atp")]
        [InlineData("Tennis ATP", "tennis", "atp")]
        [InlineData("ATP", "tennis", "atp")]
        [InlineData("WTA", "tennis", "wta")]
        [InlineData("Tennis WTA", "tennis", "wta")]
        public void Test_ResolveEspnSportAndLeague_ValidSports(string rawSport, string expectedSport, string expectedLeague)
        {
            var result = EspnApiService.ResolveEspnSportAndLeague(rawSport);
            Assert.NotNull(result);
            Assert.Equal(expectedSport, result.Value.sport);
            Assert.Equal(expectedLeague, result.Value.league);
        }

        [Theory]
        [InlineData("Cricket")]
        [InlineData("Golf")]
        [InlineData("MMA")]
        [InlineData("")]
        [InlineData(null)]
        public void Test_ResolveEspnSportAndLeague_UnsupportedSports(string? rawSport)
        {
            var result = EspnApiService.ResolveEspnSportAndLeague(rawSport);
            Assert.Null(result);
        }
    }
}
