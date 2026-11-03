using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BettingApp.Migrations
{
    /// <inheritdoc />
    public partial class AddExtraScreenshotUrlsToBet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExtraScreenshotUrls",
                table: "Bets",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExtraScreenshotUrls",
                table: "Bets");
        }
    }
}
