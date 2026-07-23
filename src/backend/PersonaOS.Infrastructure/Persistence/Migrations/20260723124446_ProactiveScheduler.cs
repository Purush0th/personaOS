using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonaOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProactiveScheduler : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeOnly>(
                name: "EveningRollupTime",
                table: "InstanceConfig",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "MorningBriefTime",
                table: "InstanceConfig",
                type: "time",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProactiveJobRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    LocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RanAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Pushed = table.Column<bool>(type: "bit", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProactiveJobRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProactiveJobRuns_JobName_LocalDate",
                table: "ProactiveJobRuns",
                columns: new[] { "JobName", "LocalDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProactiveJobRuns");

            migrationBuilder.DropColumn(
                name: "EveningRollupTime",
                table: "InstanceConfig");

            migrationBuilder.DropColumn(
                name: "MorningBriefTime",
                table: "InstanceConfig");
        }
    }
}
