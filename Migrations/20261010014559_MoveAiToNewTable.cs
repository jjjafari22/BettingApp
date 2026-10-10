using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BettingApp.Migrations
{
    /// <inheritdoc />
    public partial class MoveAiToNewTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiOutcomeResult",
                table: "Bets");

            migrationBuilder.DropColumn(
                name: "AiVisionError",
                table: "Bets");

            migrationBuilder.DropColumn(
                name: "AiVisionResultJson",
                table: "Bets");

            migrationBuilder.CreateTable(
                name: "BetAiEvaluations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BetId = table.Column<int>(type: "int", nullable: false),
                    AiVisionResultJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AiVisionError = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AiOutcomeResult = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BetAiEvaluations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BetAiEvaluations_Bets_BetId",
                        column: x => x.BetId,
                        principalTable: "Bets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BetAiEvaluations_BetId",
                table: "BetAiEvaluations",
                column: "BetId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BetAiEvaluations");

            migrationBuilder.AddColumn<string>(
                name: "AiOutcomeResult",
                table: "Bets",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiVisionError",
                table: "Bets",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiVisionResultJson",
                table: "Bets",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
