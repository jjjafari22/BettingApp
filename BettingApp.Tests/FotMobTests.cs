using System;
using Xunit;
using BettingApp.Services;

namespace BettingApp.Tests
{
    public class FotMobTests
    {
        [Theory]
        [InlineData("Real Betis", "Osasuna", "Real Madrid", "Osasuna", null, false)] // Sibling Clash Real
        [InlineData("Manchester United", "Arsenal", "Manchester City", "Arsenal", null, false)] // Sibling Clash Manchester
        [InlineData("Wolverhampton Wanderers", "Chelsea", "Wolverhampton", "Chelsea", null, true)] // Verbose query
        [InlineData("West Ham", "Charlton Athletic", "West Ham United U21", "Charlton Athletic", null, false)] // U21 penalty
        [InlineData("West Ham", "Charlton Athletic", "West Ham", "Bournemouth", null, false)] // Away team mismatch
        [InlineData("Athletic Bilbao", "Sevilla", "Athletic Club", "Sevilla", null, true)] // Alias Bilbao
        [InlineData("Inter Milan", "Juventus", "Internazionale", "Juventus", null, true)] // Alias Inter
        [InlineData("Arsenal", "Man City", "Arsenal", "Manchester City", null, true)] // Alias Man City
        [InlineData("Spurs", "Chelsea", "Tottenham Hotspur", "Chelsea", null, true)] // Alias Spurs
        [InlineData("Man Utd", "Liverpool", "Manchester United", "Liverpool", null, true)] // Alias Man Utd
        [InlineData("Wolves", "Everton", "Wolverhampton Wanderers", "Everton", null, true)] // Alias Wolves
        [InlineData("FC Copenhagen", "Brondby", "FC København", "Brondby", null, true)] // Alias Copenhagen
        [InlineData("West Ham", "Charlton Athletic", "Charlton Athletic", "West Ham", null, true)] // Reversed order
        [InlineData("Vasco da Gama U20", "Palmeiras U20", "Vasco da Gama", "Palmeiras", null, false)] // U20 vs Senior match should be rejected
        [InlineData("Vasco da Gama", "Palmeiras", "Vasco da Gama U20", "Palmeiras U20", null, false)] // Senior vs U20 match should be rejected
        [InlineData("Vasco da Gama U20", "Palmeiras U20", "Vasco U20", "Palmeiras U20", null, true)] // U20 vs U20 match should be accepted
        [InlineData("Bayern Munich", "VfB Stuttgart", "Bayern München", "VfB Stuttgart", null, true)] // English translation of Munich to Munchen
        [InlineData("Olympiacos", "Jagiellonia Bialystok", "Olympiacos", "Jagiellonia Białystok", null, true)] // Polish diacritics
        [InlineData("Al-Qadisiyah", "Al-Ettifaq", "Al Qadsiah", "Al Ettifaq", null, true)] // Saudi aliases
        [InlineData("Al Qadisiya", "Al Ettifaq", "Al Qadsiah", "Al-Ettifaq", null, true)] // Saudi aliases variation
        [InlineData("Al Taawon Buraidah", "Al Qadisiya", "Al-Taawoun", "Al Qadsiah", null, true)] // Saudi aliases Taawon Buraidah
        [InlineData("Al Akhdood", "Al Adalh", "Al Akhdoud", "Al-Adalah", null, true)] // Saudi aliases Akhdood / Adalh
        [InlineData("Al Akhdood", "Al Adalh", "Al-Okhdood", "Al Adalah", null, true)] // Saudi aliases Okhdood / Adalah
        [InlineData("Al-Anwar Club", "Al-Wahda Mecca", "Al Anwar", "Al-Wehda", "Saudi First Division", true)] // Saudi aliases Al-Anwar Club vs Al-Wahda Mecca
        [InlineData("Al-Wahda Club", "Al-Tai", "Al-Wehda Mecca", "Al Taee", "Saudi First Division", true)] // Saudi aliases with different suffixes + Arabic skeleton
        [InlineData("Al-Jabalain", "Al-Zulfi", "Al Jabalayn", "Al Zulfe", "Saudi First Division", true)] // Arabic transliteration skeleton
        [InlineData("Eidsvold Turn", "Ullensaker/Kisa", "Eidsvold TF", "Ull/Kisa", null, true)] // Norwegian aliases
        [InlineData("SK Brann Women", "HJK Women", "Brann", "HJK", "UEFA Women's Europa Cup", true)] // Brann Women
        [InlineData("Valerenga Women", "Feyenoord Women", "Valerenga", "Feyenoord", "UEFA Women's Europa Cup", true)] // Valerenga Women
        [InlineData("Sporting CP Women", "Brondby IF Women", "Sporting CP", "Brøndby", "UEFA Women's Europa Cup", true)] // Sporting Women
        [InlineData("SK Brann Women", "HJK Women", "Brann", "HJK", "Champions League", false)] // Shouldn't match men's league
        [InlineData("Paris FC Women", "Arsenal Women", "Paris FC (W)", "Arsenal (W)", "Women's Champions League", true)]
        [InlineData("Lyon Women", "Chelsea Women", "Lyon (W)", "Chelsea (W)", "Women's Champions League", true)]
        [InlineData("Benfica Women", "Bayern Munich Women", "Benfica (W)", "Bayern Munich (W)", "Women's Champions League", true)]
        [InlineData("SL Benfica Women", "Bayern Munich Women", "Benfica (W)", "Bayern Munich (W)", "Women's Champions League", true)]
        [InlineData("Lens", "Paris FC", "Le Havre", "Paris Saint-Germain", "Ligue 1", false)] // Bet #9393: "Lens" must not substring-match "Le" in "Le Havre"
        [InlineData("Lens", "Paris FC", "Lens", "Paris FC", "Ligue 1", true)]
        [InlineData("FC Copenhagen", "Odense", "FC København", "OB", "Superligaen", true)] // Bet #9620/#9624: Odense -> OB
        [InlineData("Shanghai Port", "Cong An Hanoi FC", "Shanghai Port", "Cong An Ha Noi", "AFC Champions League Elite", true)] // Bet #9661: Hanoi <-> Ha Noi
        public void Test_AreTeamsMatching(string qHome, string qAway, string oHome, string oAway, string? optLeague, bool expected)
        {
            var mapper = new BettingApp.Services.TeamAliasMappingService();
            var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<BettingApp.Services.FotMobScraperService>.Instance;
            var service = new BettingApp.Services.FotMobScraperService(new System.Net.Http.HttpClient(), mapper, logger);
            bool result = service.AreTeamsMatching(qHome, qAway, oHome, oAway, optLeague);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void Test_GetTeamMatchScore_PrefersFullBidirectionalMatchOverOneWaySubset()
        {
            var mapper = new BettingApp.Services.TeamAliasMappingService();
            var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<BettingApp.Services.FotMobScraperService>.Instance;
            var service = new BettingApp.Services.FotMobScraperService(new System.Net.Http.HttpClient(), mapper, logger);

            int exactParisFcScore = service.GetTeamMatchScore("Lens", "Paris FC", "Lens", "Paris FC", "Ligue 1");
            int subsetPsgScore = service.GetTeamMatchScore("Lens", "Paris FC", "Lens", "Paris Saint-Germain", "Ligue 1");

            Assert.Equal(200, exactParisFcScore);
            Assert.Equal(150, subsetPsgScore);
            Assert.True(exactParisFcScore > subsetPsgScore);
        }

        [Fact]
        public void Test_TryExtractUnstartedMatchStartTime_FotMobAndEspn()
        {
            var futureUtc = DateTime.UtcNow.AddHours(5);
            string isoStr = futureUtc.ToString("yyyy-MM-ddTHH:mm:ss.000Z", System.Globalization.CultureInfo.InvariantCulture);

            string fotMobUnstartedJson = $@"{{""general"":{{""matchTimeUTCDate"":""{isoStr}"",""started"":false,""finished"":false}}}}";
            Assert.True(AiVisionService.TryExtractUnstartedMatchStartTime(fotMobUnstartedJson, "FotMob", out var parsedFotMob));
            Assert.Equal(futureUtc.Year, parsedFotMob.Year);
            Assert.Equal(futureUtc.Hour, parsedFotMob.Hour);

            string fotMobLiveJson = $@"{{""general"":{{""matchTimeUTCDate"":""{isoStr}"",""started"":true,""finished"":false}}}}";
            Assert.False(AiVisionService.TryExtractUnstartedMatchStartTime(fotMobLiveJson, "FotMob", out _));

            string espnScheduledJson = $@"{{""provider"":""ESPN"",""startTimeIso"":""{isoStr}"",""status"":""Scheduled"",""isFinished"":false}}";
            Assert.True(AiVisionService.TryExtractUnstartedMatchStartTime(espnScheduledJson, "ESPN", out var parsedEspn));
            Assert.Equal(futureUtc.Hour, parsedEspn.Hour);

            string espnLiveJson = $@"{{""provider"":""ESPN"",""startTimeIso"":""{isoStr}"",""status"":""In Progress"",""isFinished"":false}}";
            Assert.False(AiVisionService.TryExtractUnstartedMatchStartTime(espnLiveJson, "ESPN", out _));

            // Graceful fallback if API structure changes
            Assert.False(AiVisionService.TryExtractUnstartedMatchStartTime("{\"unexpected\":123}", "FotMob", out _));
            Assert.False(AiVisionService.TryExtractUnstartedMatchStartTime("{\"unexpected\":123}", "ESPN", out _));
        }

        [Theory]
        [InlineData("Over 2.5", true)]
        [InlineData("Over 3.5", true)]
        [InlineData("Under 1.5", true)]
        [InlineData("Yes", true)]
        [InlineData("No", true)]
        [InlineData("Draw", true)]
        [InlineData("Home or Draw", true)]
        [InlineData("Victor Osimhen", false)]
        [InlineData("Matteo Guendouzi", false)]
        [InlineData("Desire Doue - Yes", false)]
        public void Test_IsGenericBettingSelection_BlocksNonPlayerStrings(string selection, bool expectedIsGeneric)
        {
            Assert.Equal(expectedIsGeneric, PlayerMatchResolverService.IsGenericBettingSelection(selection));
        }
    }
}
