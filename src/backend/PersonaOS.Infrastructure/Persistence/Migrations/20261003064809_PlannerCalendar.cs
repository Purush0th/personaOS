using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonaOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlannerCalendar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeOnly>(
                name: "EndTime",
                table: "PlannerItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NotifyBeforeMinutes",
                table: "PlannerItems",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Recurrence",
                table: "PlannerItems",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PlannerAlertLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    OccurrenceDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    StartUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlannerAlertLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlannerAlertLogs_PlannerItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "PlannerItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlannerOccurrences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    OriginalDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    IsCancelled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ScheduledTime = table.Column<TimeOnly>(type: "TEXT", nullable: true),
                    EndTime = table.Column<TimeOnly>(type: "TEXT", nullable: true),
                    AllDay = table.Column<bool>(type: "INTEGER", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlannerOccurrences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlannerOccurrences_PlannerItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "PlannerItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlannerAlertLogs_ItemId_OccurrenceDate_StartUtc",
                table: "PlannerAlertLogs",
                columns: new[] { "ItemId", "OccurrenceDate", "StartUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlannerAlertLogs_SentAtUtc",
                table: "PlannerAlertLogs",
                column: "SentAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PlannerOccurrences_Date",
                table: "PlannerOccurrences",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_PlannerOccurrences_ItemId_OriginalDate",
                table: "PlannerOccurrences",
                columns: new[] { "ItemId", "OriginalDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlannerAlertLogs");

            migrationBuilder.DropTable(
                name: "PlannerOccurrences");

            migrationBuilder.DropColumn(
                name: "EndTime",
                table: "PlannerItems");

            migrationBuilder.DropColumn(
                name: "NotifyBeforeMinutes",
                table: "PlannerItems");

            migrationBuilder.DropColumn(
                name: "Recurrence",
                table: "PlannerItems");
        }
    }
}
