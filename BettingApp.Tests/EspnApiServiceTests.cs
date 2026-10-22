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

        [Fact]
        public void Test_EspnIsEventMatch_SubstringAndSameCityCollisions()
        {
            var espn = new EspnApiService(
                new System.Net.Http.HttpClient(),
                new TeamAliasMappingService(),
                new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<EspnApiService>.Instance);

            static System.Text.Json.JsonElement CreateEvent(
                string disp1, string name1, string loc1, string abbr1,
                string disp2, string name2, string loc2, string abbr2)
            {
                string json = $@"{{
                    ""competitions"": [{{
                        ""competitors"": [
                            {{ ""team"": {{ ""displayName"": ""{disp1}"", ""name"": ""{name1}"", ""location"": ""{loc1}"", ""shortDisplayName"": ""{name1}"", ""abbreviation"": ""{abbr1}"" }} }},
                            {{ ""team"": {{ ""displayName"": ""{disp2}"", ""name"": ""{name2}"", ""location"": ""{loc2}"", ""shortDisplayName"": ""{name2}"", ""abbreviation"": ""{abbr2}"" }} }}
                        ]
                    }}]
                }}";
                return System.Text.Json.JsonDocument.Parse(json).RootElement.Clone();
            }

            // Bet #1115: Brooklyn Nets vs Charlotte Hornets ("Hornets" contains substring "nets")
            var netsHornetsEvent = CreateEvent("Brooklyn Nets", "Nets", "Brooklyn", "BKN", "Charlotte Hornets", "Hornets", "Charlotte", "CHA");
            Assert.True(espn.IsEventMatch(netsHornetsEvent, "Brooklyn Nets", "Charlotte Hornets"));
            Assert.True(espn.IsEventMatch(netsHornetsEvent, "Charlotte Hornets", "Brooklyn Nets"));

            // Same-city derby: Los Angeles Lakers vs LA Clippers
            var laDerby = CreateEvent("Los Angeles Lakers", "Lakers", "Los Angeles", "LAL", "LA Clippers", "Clippers", "LA", "LAC");
            Assert.True(espn.IsEventMatch(laDerby, "Los Angeles Lakers", "Los Angeles Clippers"));
            Assert.False(espn.IsEventMatch(laDerby, "Los Angeles Lakers", "Boston Celtics"));

            // Same-city derby: New York Rangers vs New York Islanders
            var nyDerby = CreateEvent("New York Islanders", "Islanders", "New York", "NYI", "New York Rangers", "Rangers", "New York", "NYR");
            Assert.True(espn.IsEventMatch(nyDerby, "New York Rangers", "New York Islanders"));
            Assert.False(espn.IsEventMatch(nyDerby, "New York Rangers", "Boston Bruins"));

            // Abbreviation substring collision: Denver ("DEN") vs Golden State ("GolDEN")
            var denGsw = CreateEvent("Golden State Warriors", "Warriors", "Golden State", "GS", "Denver Nuggets", "Nuggets", "Denver", "DEN");
            Assert.True(espn.IsEventMatch(denGsw, "Denver Nuggets", "Golden State Warriors"));
        }
    }
}
