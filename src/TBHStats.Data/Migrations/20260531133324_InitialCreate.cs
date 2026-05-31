using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TBHStats.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Acts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Acts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChestTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ColorLabel = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChestTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Difficulties",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Difficulties", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HeroClasses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HeroClasses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OptimizationProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SelectedMetric = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OptimizationProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tabs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    RecognitionText = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDataSource = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tabs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WidgetSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PosX = table.Column<double>(type: "REAL", nullable: false),
                    PosY = table.Column<double>(type: "REAL", nullable: false),
                    Width = table.Column<double>(type: "REAL", nullable: false),
                    Height = table.Column<double>(type: "REAL", nullable: false),
                    AlwaysOnTop = table.Column<bool>(type: "INTEGER", nullable: false),
                    Theme = table.Column<int>(type: "INTEGER", nullable: false),
                    PollIntervalMs = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WidgetSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Stages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ActId = table.Column<int>(type: "INTEGER", nullable: false),
                    DifficultyId = table.Column<int>(type: "INTEGER", nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Stages_Acts_ActId",
                        column: x => x.ActId,
                        principalTable: "Acts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Stages_Difficulties_DifficultyId",
                        column: x => x.DifficultyId,
                        principalTable: "Difficulties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RoiCalibrations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FieldKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    TabId = table.Column<int>(type: "INTEGER", nullable: true),
                    X = table.Column<double>(type: "REAL", nullable: false),
                    Y = table.Column<double>(type: "REAL", nullable: false),
                    W = table.Column<double>(type: "REAL", nullable: false),
                    H = table.Column<double>(type: "REAL", nullable: false),
                    OcrEngine = table.Column<int>(type: "INTEGER", nullable: false),
                    ParseHint = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoiCalibrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoiCalibrations_Tabs_TabId",
                        column: x => x.TabId,
                        principalTable: "Tabs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MetricSamples",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TakenAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StageId = table.Column<int>(type: "INTEGER", nullable: true),
                    Gold = table.Column<long>(type: "INTEGER", nullable: true),
                    Xp = table.Column<long>(type: "INTEGER", nullable: true),
                    XpToLevel = table.Column<long>(type: "INTEGER", nullable: true),
                    IsReliable = table.Column<bool>(type: "INTEGER", nullable: false),
                    HeroLevel = table.Column<int>(type: "INTEGER", nullable: true),
                    HeroDamage = table.Column<long>(type: "INTEGER", nullable: true),
                    NextLocation = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetricSamples", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MetricSamples_Stages_StageId",
                        column: x => x.StageId,
                        principalTable: "Stages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "StageAggregates",
                columns: table => new
                {
                    StageId = table.Column<int>(type: "INTEGER", nullable: false),
                    RunCount = table.Column<int>(type: "INTEGER", nullable: false),
                    AvgGoldPerHour = table.Column<double>(type: "REAL", nullable: false),
                    BestGoldPerHour = table.Column<double>(type: "REAL", nullable: false),
                    AvgXpPerHour = table.Column<double>(type: "REAL", nullable: false),
                    BestXpPerHour = table.Column<double>(type: "REAL", nullable: false),
                    AvgDurationSeconds = table.Column<double>(type: "REAL", nullable: false),
                    BestDurationSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageAggregates", x => x.StageId);
                    table.ForeignKey(
                        name: "FK_StageAggregates_Stages_StageId",
                        column: x => x.StageId,
                        principalTable: "Stages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StageRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StageId = table.Column<int>(type: "INTEGER", nullable: false),
                    DurationSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    GoldGained = table.Column<long>(type: "INTEGER", nullable: false),
                    XpGained = table.Column<long>(type: "INTEGER", nullable: false),
                    Hero_HeroClassId = table.Column<int>(type: "INTEGER", nullable: false),
                    Hero_Level = table.Column<int>(type: "INTEGER", nullable: false),
                    Hero_Damage = table.Column<long>(type: "INTEGER", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsPartial = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StageRuns_HeroClasses_Hero_HeroClassId",
                        column: x => x.Hero_HeroClassId,
                        principalTable: "HeroClasses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StageRuns_Stages_StageId",
                        column: x => x.StageId,
                        principalTable: "Stages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MetricSampleChests",
                columns: table => new
                {
                    MetricSampleId = table.Column<long>(type: "INTEGER", nullable: false),
                    ChestTypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Count = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetricSampleChests", x => new { x.MetricSampleId, x.ChestTypeId });
                    table.ForeignKey(
                        name: "FK_MetricSampleChests_ChestTypes_ChestTypeId",
                        column: x => x.ChestTypeId,
                        principalTable: "ChestTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MetricSampleChests_MetricSamples_MetricSampleId",
                        column: x => x.MetricSampleId,
                        principalTable: "MetricSamples",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StageAggregateChestRates",
                columns: table => new
                {
                    StageId = table.Column<int>(type: "INTEGER", nullable: false),
                    ChestTypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    RatePerHour = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageAggregateChestRates", x => new { x.StageId, x.ChestTypeId });
                    table.ForeignKey(
                        name: "FK_StageAggregateChestRates_ChestTypes_ChestTypeId",
                        column: x => x.ChestTypeId,
                        principalTable: "ChestTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StageAggregateChestRates_StageAggregates_StageId",
                        column: x => x.StageId,
                        principalTable: "StageAggregates",
                        principalColumn: "StageId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StageRunChests",
                columns: table => new
                {
                    StageRunId = table.Column<long>(type: "INTEGER", nullable: false),
                    ChestTypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Count = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageRunChests", x => new { x.StageRunId, x.ChestTypeId });
                    table.ForeignKey(
                        name: "FK_StageRunChests_ChestTypes_ChestTypeId",
                        column: x => x.ChestTypeId,
                        principalTable: "ChestTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StageRunChests_StageRuns_StageRunId",
                        column: x => x.StageRunId,
                        principalTable: "StageRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChestTypes_Key",
                table: "ChestTypes",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Difficulties_Key",
                table: "Difficulties",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HeroClasses_Key",
                table: "HeroClasses",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MetricSampleChests_ChestTypeId",
                table: "MetricSampleChests",
                column: "ChestTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_MetricSamples_StageId",
                table: "MetricSamples",
                column: "StageId");

            migrationBuilder.CreateIndex(
                name: "IX_RoiCalibrations_TabId",
                table: "RoiCalibrations",
                column: "TabId");

            migrationBuilder.CreateIndex(
                name: "IX_StageAggregateChestRates_ChestTypeId",
                table: "StageAggregateChestRates",
                column: "ChestTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_StageRunChests_ChestTypeId",
                table: "StageRunChests",
                column: "ChestTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_StageRuns_Hero_HeroClassId",
                table: "StageRuns",
                column: "Hero_HeroClassId");

            migrationBuilder.CreateIndex(
                name: "IX_StageRuns_StageId",
                table: "StageRuns",
                column: "StageId");

            migrationBuilder.CreateIndex(
                name: "IX_Stages_ActId_DifficultyId_Number",
                table: "Stages",
                columns: new[] { "ActId", "DifficultyId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stages_DifficultyId",
                table: "Stages",
                column: "DifficultyId");

            migrationBuilder.CreateIndex(
                name: "IX_Tabs_Key",
                table: "Tabs",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MetricSampleChests");

            migrationBuilder.DropTable(
                name: "OptimizationProfiles");

            migrationBuilder.DropTable(
                name: "RoiCalibrations");

            migrationBuilder.DropTable(
                name: "StageAggregateChestRates");

            migrationBuilder.DropTable(
                name: "StageRunChests");

            migrationBuilder.DropTable(
                name: "WidgetSettings");

            migrationBuilder.DropTable(
                name: "MetricSamples");

            migrationBuilder.DropTable(
                name: "Tabs");

            migrationBuilder.DropTable(
                name: "StageAggregates");

            migrationBuilder.DropTable(
                name: "ChestTypes");

            migrationBuilder.DropTable(
                name: "StageRuns");

            migrationBuilder.DropTable(
                name: "HeroClasses");

            migrationBuilder.DropTable(
                name: "Stages");

            migrationBuilder.DropTable(
                name: "Acts");

            migrationBuilder.DropTable(
                name: "Difficulties");
        }
    }
}
