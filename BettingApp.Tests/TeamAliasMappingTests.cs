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
        [InlineData("FC United", "fc united")] // Should fallback to full string since both are stopwords
        public void Test_NormalizeTeamName(string input, string expected)
        {
            string normalized = _mapper.NormalizeTeamName(input, removeStopWords: true);
            Assert.Equal(expected, normalized);
        }
    }
}
