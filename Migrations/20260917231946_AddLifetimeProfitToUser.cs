using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BettingApp.Migrations
{
    /// <inheritdoc />
    public partial class AddLifetimeProfitToUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "LifetimeProfit",
                table: "AspNetUsers",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql(@"
                UPDATE u
                SET u.LifetimeProfit = COALESCE(
                    (
                        SELECT 
                            SUM(CASE WHEN b.Status = 'Won' THEN FLOOR(COALESCE(b.AmountNOK, 0) * b.Odds - b.FreeBetAmount) ELSE 0 END) 
                            - SUM(CASE WHEN b.Status IN ('Won', 'Lost') THEN COALESCE(b.AmountNOK, 0) - b.FreeBetAmount ELSE 0 END)
                        FROM Bets b
                        WHERE b.UserId = u.Id
                    ), 0)
                FROM AspNetUsers u;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LifetimeProfit",
                table: "AspNetUsers");
        }
    }
}
