using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonaOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ModelProfilesAndSummaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AiContextTokens",
                table: "InstanceConfig",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SummarizedThroughMessageId",
                table: "Conversations",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Summary",
                table: "Conversations",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiContextTokens",
                table: "InstanceConfig");

            migrationBuilder.DropColumn(
                name: "SummarizedThroughMessageId",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "Summary",
                table: "Conversations");
        }
    }
}
