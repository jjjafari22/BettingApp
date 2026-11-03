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

        [Fact]
        public void Test_EspnIsTennisMatch_SurnameCommaInitialsAndMultiInitials()
        {
            var espn = new EspnApiService(
                new System.Net.Http.HttpClient(),
                new TeamAliasMappingService(),
                new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<EspnApiService>.Instance);

            static System.Text.Json.JsonElement CreateTennisComp(
                string full1, string short1,
                string full2, string short2)
            {
                string json = $@"{{
                    ""competitors"": [
                        {{ ""athlete"": {{ ""displayName"": ""{full1}"", ""fullName"": ""{full1}"", ""shortName"": ""{short1}"" }} }},
                        {{ ""athlete"": {{ ""displayName"": ""{full2}"", ""fullName"": ""{full2}"", ""shortName"": ""{short2}"" }} }}
                    ]
                }}";
                return System.Text.Json.JsonDocument.Parse(json).RootElement.Clone();
            }

            // Bet #10204: "Ruud, C vs Cerundolo, JM" against ESPN's "Casper Ruud vs Juan Manuel Cerundolo"
            var ruudCerundolo = CreateTennisComp("Casper Ruud", "C. Ruud", "Juan Manuel Cerundolo", "J. Cerundolo");
            Assert.True(espn.IsTennisMatch(ruudCerundolo, "Ruud, C", "Cerundolo, JM"));
            Assert.True(espn.IsTennisMatch(ruudCerundolo, "Cerundolo, JM", "Ruud, C"));
            Assert.True(espn.IsTennisMatch(ruudCerundolo, "Casper Ruud", "Juan Manuel Cerundolo"));
            Assert.True(espn.IsTennisMatch(ruudCerundolo, "C. Ruud", "J.M. Cerundolo"));

            // Must NOT match Juan Manuel Cerundolo's brother Francisco Cerundolo ("Cerundolo, F")
            Assert.False(espn.IsTennisMatch(ruudCerundolo, "Ruud, C", "Cerundolo, F"));
            Assert.False(espn.IsTennisMatch(ruudCerundolo, "Ruud, C", "Francisco Cerundolo"));

            // Must NOT match players who only share a first name ("Carlos Alcaraz" vs "Carlos Taberner")
            var alcarazFritz = CreateTennisComp("Carlos Alcaraz", "C. Alcaraz", "Taylor Fritz", "T. Fritz");
            Assert.False(espn.IsTennisMatch(alcarazFritz, "Carlos Taberner", "Taylor Fritz"));
        }

        [Fact]
        public void Test_TennisRetirementAndWalkoverSafeguards()
        {
            string retiredJson = @"{
                ""provider"": ""ESPN"",
                ""sport"": ""Tennis"",
                ""statusType"": ""STATUS_RETIRED"",
                ""status"": ""Final (Retired)"",
                ""statusDetail"": ""Retired"",
                ""resultSummary"": ""Karen Khachanov (RUS) bt Denis Shapovalov (CAN) 6-3 6-7 (6-8) 1-4 ret""
            }";

            Assert.True(AiVisionService.TryDetectTennisRetirementOrWalkover(
                retiredJson, out bool isRetired, out bool isWalkover, out string summary));
            Assert.True(isRetired);
            Assert.False(isWalkover);
            Assert.Contains("1-4 ret", summary);

            var legs = new System.Collections.Generic.List<AiOutcomeLegResult>
            {
                new AiOutcomeLegResult
                {
                    Match = "Denis Shapovalov vs Karen Khachanov",
                    Outcome = "Lost",
                    VerificationSource = "ESPN_Verified",
                    Stats = "Verified via ESPN: Shapovalov retired at 1-4 in Set 3."
                }
            };

            var retiredMap = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Denis Shapovalov vs Karen Khachanov"] = summary
            };
            var walkoverMap = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            AiVisionService.ApplyTennisRetirementSafeguards(legs, retiredMap, walkoverMap);
            Assert.Equal("Unknown", legs[0].Outcome);
            Assert.Null(BetSchedulingLogic.DetermineAutoSettleStatus("UNKNOWN", legs));
        }
    }
}
