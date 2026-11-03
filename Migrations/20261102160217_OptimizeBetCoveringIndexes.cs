using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BettingApp.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeBetCoveringIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Bets_Status",
                table: "Bets");

            migrationBuilder.DropIndex(
                name: "IX_Bets_UserId",
                table: "Bets");

            migrationBuilder.CreateIndex(
                name: "IX_Bets_NextCheckTime",
                table: "Bets",
                column: "NextCheckTime")
                .Annotation("SqlServer:Include", new[] { "Status", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Bets_Status",
                table: "Bets",
                column: "Status")
                .Annotation("SqlServer:Include", new[] { "UpdatedAt", "CreatedAt", "AutoApproveAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Bets_UserId",
                table: "Bets",
                column: "UserId")
                .Annotation("SqlServer:Include", new[] { "Status", "Odds", "AmountNOK", "FreeBetAmount", "UpdatedAt", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Bets_NextCheckTime",
                table: "Bets");

            migrationBuilder.DropIndex(
                name: "IX_Bets_Status",
                table: "Bets");

            migrationBuilder.DropIndex(
                name: "IX_Bets_UserId",
                table: "Bets");

            migrationBuilder.CreateIndex(
                name: "IX_Bets_Status",
                table: "Bets",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Bets_UserId",
                table: "Bets",
                column: "UserId");
        }
    }
}
