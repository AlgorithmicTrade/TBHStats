using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TBHStats.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAvgGainedToStageAggregate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "AvgGoldGained",
                table: "StageAggregates",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "AvgXpGained",
                table: "StageAggregates",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "RecentAvgGoldGained",
                table: "StageAggregates",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "RecentAvgXpGained",
                table: "StageAggregates",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AvgGoldGained",
                table: "StageAggregates");

            migrationBuilder.DropColumn(
                name: "AvgXpGained",
                table: "StageAggregates");

            migrationBuilder.DropColumn(
                name: "RecentAvgGoldGained",
                table: "StageAggregates");

            migrationBuilder.DropColumn(
                name: "RecentAvgXpGained",
                table: "StageAggregates");
        }
    }
}
