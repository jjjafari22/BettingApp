using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BettingApp.Migrations
{
    /// <inheritdoc />
    public partial class AddOddsPapiIdsAndUrls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FixtureId",
                table: "BetLegs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BookmakerUrl",
                table: "BetLegBookmakerOdds",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MarketId",
                table: "BetLegBookmakerOdds",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OutcomeId",
                table: "BetLegBookmakerOdds",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlayerId",
                table: "BetLegBookmakerOdds",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FixtureId",
                table: "BetLegs");

            migrationBuilder.DropColumn(
                name: "BookmakerUrl",
                table: "BetLegBookmakerOdds");

            migrationBuilder.DropColumn(
                name: "MarketId",
                table: "BetLegBookmakerOdds");

            migrationBuilder.DropColumn(
                name: "OutcomeId",
                table: "BetLegBookmakerOdds");

            migrationBuilder.DropColumn(
                name: "PlayerId",
                table: "BetLegBookmakerOdds");
        }
    }
}
