using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BettingApp.Migrations
{
    /// <inheritdoc />
    public partial class AddBroadcastRecipientNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RecipientNames",
                table: "Broadcasts",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RecipientNames",
                table: "Broadcasts");
        }
    }
}
