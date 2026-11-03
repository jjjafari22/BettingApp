using System;
using Xunit;
using BettingApp.Services;

namespace BettingApp.Tests
{
    public class TeamAliasMappingTests
    {
        private readonly TeamAliasMappingService _mapper = new TeamAliasMappingService();

        [Theory]
        [InlineData("Al Nassr", "al nassr")]
        [InlineData("Al Draih", "al diriyah")]
        [InlineData("Draih", "diriyah")]
        [InlineData("Athletic Bilbao", "athletic")]
        [InlineData("Athletic Club", "athletic")]
        [InlineData("Club Brugge", "brugge")]
        [InlineData("FC Bayern Munchen", "bayern munchen")]
        [InlineData("Paris Saint Germain", "paris saint germain")]
        [InlineData("PSG", "paris saint germain")]
        [InlineData("Manchester Utd", "manchester")]
        [InlineData("Man City", "manchester")]
        [InlineData("Spurs", "tottenham hotspur")]
        [InlineData("Internazionale", "internazionale")]
        [InlineData("Inter Milan", "internazionale")]
        [InlineData("Inter Milano", "internazionale")]
        [InlineData("Internazionale Milano", "internazionale")]
        [InlineData("Atletico Paranaense", "athletico parananse")]
        [InlineData("Athletico Paranaense", "athletico parananse")]
        [InlineData("Atletico PR", "athletico parananse")]
        [InlineData("FC United", "fc united")] // Should fallback to full string since both are stopwords
        public void Test_NormalizeTeamName(string input, string expected)
        {
            string normalized = _mapper.NormalizeTeamName(input, removeStopWords: true);
            Assert.Equal(expected, normalized);
        }

        [Theory]
        [InlineData("Atletico Paranaense", "Athletico Paranaense")]
        [InlineData("Atletico PR", "Athletico Paranaense")]
        [InlineData("Atletico-PR", "Athletico Paranaense")]
        [InlineData("Athletico-PR", "Athletico Paranaense")]
        [InlineData("Atletico GO", "AC Goianiense GO")]
        [InlineData("Atletico-GO", "AC Goianiense GO")]
        [InlineData("Atletico Goianiense", "AC Goianiense GO")]
        [InlineData("Atletico MG", "CA Mineiro MG")]
        [InlineData("Ruud, C", "Casper Ruud")]
        [InlineData("Cerundolo, JM", "Juan Manuel Cerundolo")]
        public void Test_IsTeamMatch_Paranaense(string teamA, string teamB)
        {
            Assert.True(TeamAliasMappingService.IsTeamMatch(teamA, teamB));
        }

        [Fact]
        public void Test_IsTeamMatch_MultiInitialDoesNotMatchDifferentFirstName()
        {
            Assert.False(TeamAliasMappingService.IsTeamMatch("Cerundolo, JM", "Francisco Cerundolo"));
            Assert.False(TeamAliasMappingService.IsTeamMatch("Cerundolo, F", "Juan Manuel Cerundolo"));
        }
    }
}
