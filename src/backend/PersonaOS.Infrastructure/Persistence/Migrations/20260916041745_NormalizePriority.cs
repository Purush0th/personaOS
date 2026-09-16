using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonaOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NormalizePriority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The BoardPlanning migration added Priority with an empty default before that default
            // was corrected, so rows that existed at the time carry "" and show a blank priority.
            migrationBuilder.Sql("UPDATE Goals SET Priority = 'medium' WHERE Priority IS NULL OR Priority = '';");
            migrationBuilder.Sql("UPDATE BoardTasks SET Priority = 'medium' WHERE Priority IS NULL OR Priority = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data repair only; there is nothing to undo.
        }
    }
}
