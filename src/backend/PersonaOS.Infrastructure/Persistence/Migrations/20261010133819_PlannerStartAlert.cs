using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonaOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlannerStartAlert : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlannerAlertLogs_ItemId_OccurrenceDate_StartUtc",
                table: "PlannerAlertLogs");

            migrationBuilder.AddColumn<bool>(
                name: "AtStart",
                table: "PlannerAlertLogs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_PlannerAlertLogs_ItemId_OccurrenceDate_StartUtc_AtStart",
                table: "PlannerAlertLogs",
                columns: new[] { "ItemId", "OccurrenceDate", "StartUtc", "AtStart" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlannerAlertLogs_ItemId_OccurrenceDate_StartUtc_AtStart",
                table: "PlannerAlertLogs");

            migrationBuilder.DropColumn(
                name: "AtStart",
                table: "PlannerAlertLogs");

            migrationBuilder.CreateIndex(
                name: "IX_PlannerAlertLogs_ItemId_OccurrenceDate_StartUtc",
                table: "PlannerAlertLogs",
                columns: new[] { "ItemId", "OccurrenceDate", "StartUtc" },
                unique: true);
        }
    }
}
