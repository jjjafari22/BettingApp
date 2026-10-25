using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BettingApp.Migrations
{
    /// <inheritdoc />
    public partial class AddTieredAutoApprovalSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoApprovalAllowBetBuilders",
                table: "Settings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AutoApprovalAllowLiveBets",
                table: "Settings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsTier1PinnacleEnabled",
                table: "Settings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsTier2SoftBookiesEnabled",
                table: "Settings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsTier3SmallStakeEnabled",
                table: "Settings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Tier2MaxNetLiabilityNOK",
                table: "Settings",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 3000m);

            migrationBuilder.AddColumn<decimal>(
                name: "Tier2MaxOverBookieAvgPercent",
                table: "Settings",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 3.0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Tier2MaxStakeNOK",
                table: "Settings",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 300m);

            migrationBuilder.AddColumn<decimal>(
                name: "Tier3MaxNetLiabilityNOK",
                table: "Settings",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 2000m);

            migrationBuilder.AddColumn<decimal>(
                name: "Tier3MaxStakeNOK",
                table: "Settings",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 200m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoApprovalAllowBetBuilders",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "AutoApprovalAllowLiveBets",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "IsTier1PinnacleEnabled",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "IsTier2SoftBookiesEnabled",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "IsTier3SmallStakeEnabled",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Tier2MaxNetLiabilityNOK",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Tier2MaxOverBookieAvgPercent",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Tier2MaxStakeNOK",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Tier3MaxNetLiabilityNOK",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Tier3MaxStakeNOK",
                table: "Settings");
        }
    }
}
