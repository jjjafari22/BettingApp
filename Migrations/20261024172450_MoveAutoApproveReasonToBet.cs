using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BettingApp.Migrations
{
    /// <inheritdoc />
    public partial class MoveAutoApproveReasonToBet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoApproveReason",
                table: "BetAiEvaluations");

            migrationBuilder.AddColumn<string>(
                name: "AutoApproveReason",
                table: "Bets",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoApproveReason",
                table: "Bets");

            migrationBuilder.AddColumn<string>(
                name: "AutoApproveReason",
                table: "BetAiEvaluations",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
