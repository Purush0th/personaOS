using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonaOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GoalPeriodEnd : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "PeriodEnd",
                table: "Goals",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            // Existing goals get the end GoalPeriodCalculator.DefaultEnd would give them. Their
            // starts were snapped to the 1st, so these match the calendar period they were filed
            // under: the month's last day, 89 days on for a quarter, 31 December for a year.
            migrationBuilder.Sql("""
                UPDATE Goals SET PeriodEnd = CASE PeriodType
                    WHEN 'month' THEN date(PeriodStart, '+1 month', '-1 day')
                    WHEN 'quarter' THEN date(PeriodStart, '+89 days')
                    ELSE strftime('%Y', PeriodStart) || '-12-31'
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PeriodEnd",
                table: "Goals");
        }
    }
}
