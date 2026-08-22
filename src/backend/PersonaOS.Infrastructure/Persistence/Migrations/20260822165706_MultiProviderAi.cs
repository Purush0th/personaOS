using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonaOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MultiProviderAi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ClaudeModel",
                table: "InstanceConfig",
                newName: "AiModel");

            migrationBuilder.AddColumn<string>(
                name: "AiBaseUrl",
                table: "InstanceConfig",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            // Existing installs keep their current behaviour: default to the Anthropic provider.
            migrationBuilder.AddColumn<string>(
                name: "AiProvider",
                table: "InstanceConfig",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "anthropic");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiBaseUrl",
                table: "InstanceConfig");

            migrationBuilder.DropColumn(
                name: "AiProvider",
                table: "InstanceConfig");

            migrationBuilder.RenameColumn(
                name: "AiModel",
                table: "InstanceConfig",
                newName: "ClaudeModel");
        }
    }
}
