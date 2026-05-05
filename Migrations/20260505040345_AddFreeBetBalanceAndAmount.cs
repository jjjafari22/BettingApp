using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BettingApp.Migrations
{
    /// <inheritdoc />
    public partial class AddFreeBetBalanceAndAmount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "FreeBetAmount",
                table: "Bets",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FreeBetBalance",
                table: "AspNetUsers",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FreeBetAmount",
                table: "Bets");

            migrationBuilder.DropColumn(
                name: "FreeBetBalance",
                table: "AspNetUsers");
        }
    }
}
