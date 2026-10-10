using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BettingApp.Migrations
{
    /// <inheritdoc />
    public partial class AddBetLegBookmakerOdds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BetLegBookmakerOdds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BetLegId = table.Column<int>(type: "int", nullable: false),
                    BookmakerName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OddsValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BetLegBookmakerOdds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BetLegBookmakerOdds_BetLegs_BetLegId",
                        column: x => x.BetLegId,
                        principalTable: "BetLegs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BetLegBookmakerOdds_BetLegId",
                table: "BetLegBookmakerOdds",
                column: "BetLegId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BetLegBookmakerOdds");
        }
    }
}
