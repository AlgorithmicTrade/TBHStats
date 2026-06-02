using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TBHStats.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWindowPlacement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WindowPlacement",
                columns: table => new
                {
                    WindowKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PosX = table.Column<double>(type: "REAL", nullable: false),
                    PosY = table.Column<double>(type: "REAL", nullable: false),
                    Width = table.Column<double>(type: "REAL", nullable: false),
                    Height = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WindowPlacement", x => x.WindowKey);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WindowPlacement");
        }
    }
}
