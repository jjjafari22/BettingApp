using System;
using Microsoft.Extensions.Caching.Memory;
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

        private sealed class FakeHostEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
        {
            public string EnvironmentName { get; set; } = "Production";
            public string ApplicationName { get; set; } = "BettingApp.Tests";
            public string ContentRootPath { get; set; } = "";
            public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
        }

        [Fact]
        public async System.Threading.Tasks.Task Test_ProdVsDev_PreBetMatchFiltering_And_PostSettlementCheck()
        {
            var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
            var emptyScoreboard = @"{""events"":[]}";
            foreach (var d in new[] { "20261101", "20261102", "20261103", "20261104", "20261105", "20261106", "20261107", "20261108" })
            {
                cache.Set($"espn_scoreboard_basketball_nba_{d}", emptyScoreboard);
            }

            // Game 1 on 20261102 (2026-11-03T00:00Z): STATUS_FINAL (finished 22h before betPlacedAt)
            cache.Set("espn_scoreboard_basketball_nba_20261102", @"{
                ""events"": [{
                    ""id"": ""401909901"",
                    ""date"": ""2026-11-03T00:00Z"",
                    ""name"": ""Cleveland Cavaliers at Oklahoma City Thunder"",
                    ""status"": { ""type"": { ""name"": ""STATUS_FINAL"", ""completed"": true, ""detail"": ""Final"" } },
                    ""competitions"": [{
                        ""competitors"": [
                            { ""homeAway"": ""home"", ""score"": ""115"", ""team"": { ""displayName"": ""Oklahoma City Thunder"", ""name"": ""Thunder"", ""location"": ""Oklahoma City"", ""shortDisplayName"": ""Thunder"", ""abbreviation"": ""OKC"" } },
                            { ""homeAway"": ""away"", ""score"": ""108"", ""team"": { ""displayName"": ""Cleveland Cavaliers"", ""name"": ""Cavaliers"", ""location"": ""Cleveland"", ""shortDisplayName"": ""Cavaliers"", ""abbreviation"": ""CLE"" } }
                        ]
                    }]
                }]
            }");
            cache.Set("espn_summary_basketball_nba_401909901", @"{""header"":{""id"":""401909901""},""boxscore"":{}}");

            // Game 2 on 20261104 (2026-11-05T01:00Z): STATUS_SCHEDULED (upcoming game)
            cache.Set("espn_scoreboard_basketball_nba_20261104", @"{
                ""events"": [{
                    ""id"": ""401909929"",
                    ""date"": ""2026-11-05T01:00Z"",
                    ""name"": ""Cleveland Cavaliers at Oklahoma City Thunder"",
                    ""status"": { ""type"": { ""name"": ""STATUS_SCHEDULED"", ""completed"": false, ""detail"": ""Wed, November 4th at 8:00 PM EST"" } },
                    ""competitions"": [{
                        ""competitors"": [
                            { ""homeAway"": ""home"", ""score"": ""0"", ""team"": { ""displayName"": ""Oklahoma City Thunder"", ""name"": ""Thunder"", ""location"": ""Oklahoma City"", ""shortDisplayName"": ""Thunder"", ""abbreviation"": ""OKC"" } },
                            { ""homeAway"": ""away"", ""score"": ""0"", ""team"": { ""displayName"": ""Cleveland Cavaliers"", ""name"": ""Cavaliers"", ""location"": ""Cleveland"", ""shortDisplayName"": ""Cavaliers"", ""abbreviation"": ""CLE"" } }
                        ]
                    }]
                }]
            }");

            var prodEspn = new EspnApiService(
                new System.Net.Http.HttpClient(),
                new TeamAliasMappingService(),
                cache,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<EspnApiService>.Instance,
                new FakeHostEnvironment { EnvironmentName = "Production" });

            var devEspn = new EspnApiService(
                new System.Net.Http.HttpClient(),
                new TeamAliasMappingService(),
                cache,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<EspnApiService>.Instance,
                new FakeHostEnvironment { EnvironmentName = "Development" });

            var betPlacedAt = new DateTime(2026, 11, 3, 22, 15, 26, DateTimeKind.Utc);
            var poisonedStartTime = new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc);

            // 1. PROD (Bet #10381): Even if betStartsAt was poisoned with Game 1's 2026-11-03T00:00Z, PROD ignores it,
            // hard-rejects Game 1, and finds Game 2 (401909929 on 2026-11-05T01:00Z).
            string? prodRematchJson = await prodEspn.GetMatchStatsJsonAsync(
                "Oklahoma City Thunder vs Cleveland Cavaliers",
                "Basketball - NBA",
                betPlacedAt: betPlacedAt,
                betStartsAt: poisonedStartTime,
                betId: 10381);
            Assert.NotNull(prodRematchJson);
            Assert.Contains("401909929", prodRematchJson);
            Assert.Contains("2026-11-05T01:00Z", prodRematchJson);

            // 2. DEV (Bet #1271): Also picks upcoming Game 2 (401909929) over yesterday's finished Game 1 via ranking penalty.
            string? devRematchJson = await devEspn.GetMatchStatsJsonAsync(
                "Oklahoma City Thunder vs Cleveland Cavaliers",
                "Basketball - NBA",
                betPlacedAt: betPlacedAt,
                betStartsAt: null,
                betId: 1271);
            Assert.NotNull(devRematchJson);
            Assert.Contains("401909929", devRematchJson);

            // 3. Single finished match from before betPlacedAt (no upcoming rematch):
            // PROD strictly rejects it (returns null), whereas DEV allows it so finished slips from earlier today can be tested!
            cache.Set("espn_scoreboard_basketball_nba_20261104", emptyScoreboard);

            string? prodSinglePastJson = await prodEspn.GetMatchStatsJsonAsync(
                "Oklahoma City Thunder vs Cleveland Cavaliers",
                "Basketball - NBA",
                betPlacedAt: betPlacedAt,
                betStartsAt: poisonedStartTime,
                betId: 10381);
            Assert.Null(prodSinglePastJson);

            string? devSinglePastJson = await devEspn.GetMatchStatsJsonAsync(
                "Oklahoma City Thunder vs Cleveland Cavaliers",
                "Basketball - NBA",
                betPlacedAt: betPlacedAt,
                betStartsAt: poisonedStartTime,
                betId: 1272);
            Assert.NotNull(devSinglePastJson);
            Assert.Contains("401909901", devSinglePastJson);

            // 4. Partly-graded bet post-settlement check in PROD (runs days later, e.g. Nov 6, for a bet uploaded BEFORE Game 1 on Nov 2):
            // Because Game 1 (2026-11-03T00:00Z) started AFTER betPlacedAt (2026-11-02T20:00Z), PROD still finds and grades Game 1!
            var earlierBetPlacedAt = new DateTime(2026, 11, 2, 20, 0, 0, DateTimeKind.Utc);
            string? prodPostSettlementJson = await prodEspn.GetMatchStatsJsonAsync(
                "Oklahoma City Thunder vs Cleveland Cavaliers",
                "Basketball - NBA",
                betPlacedAt: earlierBetPlacedAt,
                betStartsAt: new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc),
                betId: 10381);
            Assert.NotNull(prodPostSettlementJson);
            Assert.Contains("401909901", prodPostSettlementJson);
        }
    }
}
