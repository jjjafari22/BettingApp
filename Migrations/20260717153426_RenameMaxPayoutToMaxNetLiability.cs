using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BettingApp.Migrations
{
    /// <inheritdoc />
    public partial class RenameMaxPayoutToMaxNetLiability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxPayout",
                table: "AspNetUsers");

            migrationBuilder.AddColumn<decimal>(
                name: "MaxNetLiability",
                table: "AspNetUsers",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxNetLiability",
                table: "AspNetUsers");

            migrationBuilder.AddColumn<decimal>(
                name: "MaxPayout",
                table: "AspNetUsers",
                type: "decimal(18,0)",
                precision: 18,
                nullable: false,
                defaultValue: 0m);
        }
    }
}
