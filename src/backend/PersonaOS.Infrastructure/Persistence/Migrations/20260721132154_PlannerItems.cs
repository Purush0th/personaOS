using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonaOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlannerItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlannerItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    ScheduledTime = table.Column<TimeOnly>(type: "time", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    GoalId = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlannerItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlannerItems_Goals_GoalId",
                        column: x => x.GoalId,
                        principalTable: "Goals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlannerItems_Date_SortOrder",
                table: "PlannerItems",
                columns: new[] { "Date", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_PlannerItems_GoalId",
                table: "PlannerItems",
                column: "GoalId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlannerItems");
        }
    }
}
