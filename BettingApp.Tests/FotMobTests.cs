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
        [InlineData("Eidsvold Turn", "Ullensaker/Kisa", "Eidsvold TF", "Ull/Kisa", null, true)] // Norwegian aliases
        [InlineData("SK Brann Women", "HJK Women", "Brann", "HJK", "UEFA Women's Europa Cup", true)] // Brann Women
        [InlineData("Valerenga Women", "Feyenoord Women", "Valerenga", "Feyenoord", "UEFA Women's Europa Cup", true)] // Valerenga Women
        [InlineData("Sporting CP Women", "Brondby IF Women", "Sporting CP", "Brøndby", "UEFA Women's Europa Cup", true)] // Sporting Women
        [InlineData("SK Brann Women", "HJK Women", "Brann", "HJK", "Champions League", false)] // Shouldn't match men's league
        [InlineData("Paris FC Women", "Arsenal Women", "Paris FC (W)", "Arsenal (W)", "Women's Champions League", true)]
        [InlineData("Lyon Women", "Chelsea Women", "Lyon (W)", "Chelsea (W)", "Women's Champions League", true)]
        [InlineData("Benfica Women", "Bayern Munich Women", "Benfica (W)", "Bayern Munich (W)", "Women's Champions League", true)]
        [InlineData("SL Benfica Women", "Bayern Munich Women", "Benfica (W)", "Bayern Munich (W)", "Women's Champions League", true)]
        public void Test_AreTeamsMatching(string qHome, string qAway, string oHome, string oAway, string? optLeague, bool expected)
        {
            var mapper = new BettingApp.Services.TeamAliasMappingService();
            var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<BettingApp.Services.FotMobScraperService>.Instance;
            var service = new BettingApp.Services.FotMobScraperService(new System.Net.Http.HttpClient(), mapper, logger);
            bool result = service.AreTeamsMatching(qHome, qAway, oHome, oAway, optLeague);
            Assert.Equal(expected, result);
        }
    }
}
