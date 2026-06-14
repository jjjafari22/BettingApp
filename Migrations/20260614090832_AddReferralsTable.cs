using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BettingApp.Migrations
{
    /// <inheritdoc />
    public partial class AddReferralsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Referrals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReferrerUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReferrerUserName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReferredUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReferredUserName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TotalBonusAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Tier1Paid = table.Column<bool>(type: "bit", nullable: false),
                    Tier2Paid = table.Column<bool>(type: "bit", nullable: false),
                    Tier3Paid = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Referrals", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Referrals");
        }
    }
}
