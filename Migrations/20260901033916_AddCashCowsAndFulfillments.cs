using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BettingApp.Migrations
{
    /// <inheritdoc />
    public partial class AddCashCowsAndFulfillments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CashCowsJson",
                table: "Settings");

            migrationBuilder.AddColumn<decimal>(
                name: "FulfilledAmount",
                table: "Transactions",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "ReceiverId",
                table: "Transactions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceiverName",
                table: "Transactions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceiverType",
                table: "Transactions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SenderId",
                table: "Transactions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SenderName",
                table: "Transactions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SenderType",
                table: "Transactions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CashCows",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Vipps = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Revolut = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BankTransfer = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OtherPlatformName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OtherPaymentDetails = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Balance = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashCows", x => x.Id);
                });

            migrationBuilder.Sql(@"
                UPDATE t
                SET 
                    t.SenderId = CASE WHEN t.Type = 'Deposit' THEN t.UserId ELSE NULL END,
                    t.SenderType = CASE WHEN t.Type = 'Deposit' THEN 'User' ELSE NULL END,
                    t.SenderName = CASE WHEN t.Type = 'Deposit' THEN ISNULL(u.FirstName, '') + ' ' + ISNULL(u.LastName, '') ELSE NULL END,
                    
                    t.ReceiverId = CASE WHEN t.Type IN ('Withdrawal', 'Free Bet') THEN t.UserId ELSE NULL END,
                    t.ReceiverType = CASE WHEN t.Type IN ('Withdrawal', 'Free Bet') THEN 'User' ELSE NULL END,
                    t.ReceiverName = CASE WHEN t.Type IN ('Withdrawal', 'Free Bet') THEN ISNULL(u.FirstName, '') + ' ' + ISNULL(u.LastName, '') ELSE NULL END
                FROM Transactions t
                INNER JOIN AspNetUsers u ON t.UserId = u.Id
                WHERE t.SenderId IS NULL AND t.ReceiverId IS NULL AND t.UserId IS NOT NULL AND t.UserId != '';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CashCows");

            migrationBuilder.DropColumn(
                name: "FulfilledAmount",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ReceiverId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ReceiverName",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ReceiverType",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "SenderId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "SenderName",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "SenderType",
                table: "Transactions");

            migrationBuilder.AddColumn<string>(
                name: "CashCowsJson",
                table: "Settings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }
    }
}
