using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonaOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SpeechService : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SpeechApiKeyEncrypted",
                table: "InstanceConfig",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpeechBaseUrl",
                table: "InstanceConfig",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpeechSttModel",
                table: "InstanceConfig",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpeechTtsModel",
                table: "InstanceConfig",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpeechTtsVoice",
                table: "InstanceConfig",
                type: "TEXT",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SpeechApiKeyEncrypted",
                table: "InstanceConfig");

            migrationBuilder.DropColumn(
                name: "SpeechBaseUrl",
                table: "InstanceConfig");

            migrationBuilder.DropColumn(
                name: "SpeechSttModel",
                table: "InstanceConfig");

            migrationBuilder.DropColumn(
                name: "SpeechTtsModel",
                table: "InstanceConfig");

            migrationBuilder.DropColumn(
                name: "SpeechTtsVoice",
                table: "InstanceConfig");
        }
    }
}
