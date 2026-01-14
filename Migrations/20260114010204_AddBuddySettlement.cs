using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BettingApp.Migrations
{
    /// <inheritdoc />
    public partial class AddBuddySettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SettlementSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SettlementJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UserCount = table.Column<int>(type: "int", nullable: false),
                    TransactionCount = table.Column<int>(type: "int", nullable: false),
                    TotalVolume = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettlementSnapshots", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SettlementSnapshots");
        }
    }
}
