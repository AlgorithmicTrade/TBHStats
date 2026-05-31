using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TBHStats.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRecencyAwareAggregation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "RecentAvgDurationSeconds",
                table: "StageAggregates",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "RecentAvgGoldPerHour",
                table: "StageAggregates",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "RecentAvgXpPerHour",
                table: "StageAggregates",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "RecentBestDurationSeconds",
                table: "StageAggregates",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "RecentBestGoldPerHour",
                table: "StageAggregates",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "RecentBestXpPerHour",
                table: "StageAggregates",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<long>(
                name: "RecentHeroDamageMax",
                table: "StageAggregates",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RecentHeroDamageMin",
                table: "StageAggregates",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecentHeroLevelMax",
                table: "StageAggregates",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecentHeroLevelMin",
                table: "StageAggregates",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecentRunCount",
                table: "StageAggregates",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "RecentRatePerHour",
                table: "StageAggregateChestRates",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "RecentWindowSize",
                table: "OptimizationProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Scope",
                table: "OptimizationProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RecentAvgDurationSeconds",
                table: "StageAggregates");

            migrationBuilder.DropColumn(
                name: "RecentAvgGoldPerHour",
                table: "StageAggregates");

            migrationBuilder.DropColumn(
                name: "RecentAvgXpPerHour",
                table: "StageAggregates");

            migrationBuilder.DropColumn(
                name: "RecentBestDurationSeconds",
                table: "StageAggregates");

            migrationBuilder.DropColumn(
                name: "RecentBestGoldPerHour",
                table: "StageAggregates");

            migrationBuilder.DropColumn(
                name: "RecentBestXpPerHour",
                table: "StageAggregates");

            migrationBuilder.DropColumn(
                name: "RecentHeroDamageMax",
                table: "StageAggregates");

            migrationBuilder.DropColumn(
                name: "RecentHeroDamageMin",
                table: "StageAggregates");

            migrationBuilder.DropColumn(
                name: "RecentHeroLevelMax",
                table: "StageAggregates");

            migrationBuilder.DropColumn(
                name: "RecentHeroLevelMin",
                table: "StageAggregates");

            migrationBuilder.DropColumn(
                name: "RecentRunCount",
                table: "StageAggregates");

            migrationBuilder.DropColumn(
                name: "RecentRatePerHour",
                table: "StageAggregateChestRates");

            migrationBuilder.DropColumn(
                name: "RecentWindowSize",
                table: "OptimizationProfiles");

            migrationBuilder.DropColumn(
                name: "Scope",
                table: "OptimizationProfiles");
        }
    }
}
