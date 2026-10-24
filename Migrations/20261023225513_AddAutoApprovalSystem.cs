using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BettingApp.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoApprovalSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AutoApprovalDelaySeconds",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 180);

            migrationBuilder.AddColumn<decimal>(
                name: "AutoApprovalMaxNetLiabilityNOK",
                table: "Settings",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 5000m);

            migrationBuilder.AddColumn<decimal>(
                name: "AutoApprovalMaxOverPinnaclePercent",
                table: "Settings",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 3.0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AutoApprovalMaxStakeNOK",
                table: "Settings",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 300m);

            migrationBuilder.AddColumn<decimal>(
                name: "AutoApprovalMinPinnacleLimitEUR",
                table: "Settings",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 300m);

            migrationBuilder.AddColumn<bool>(
                name: "IsAutoApprovalEnabled",
                table: "Settings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AutoApproveAt",
                table: "Bets",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAutoApproved",
                table: "Bets",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AutoApproveReason",
                table: "BetAiEvaluations",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowAutoApproval",
                table: "AspNetUsers",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoApprovalDelaySeconds",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "AutoApprovalMaxNetLiabilityNOK",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "AutoApprovalMaxOverPinnaclePercent",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "AutoApprovalMaxStakeNOK",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "AutoApprovalMinPinnacleLimitEUR",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "IsAutoApprovalEnabled",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "AutoApproveAt",
                table: "Bets");

            migrationBuilder.DropColumn(
                name: "IsAutoApproved",
                table: "Bets");

            migrationBuilder.DropColumn(
                name: "AutoApproveReason",
                table: "BetAiEvaluations");

            migrationBuilder.DropColumn(
                name: "AllowAutoApproval",
                table: "AspNetUsers");
        }
    }
}
