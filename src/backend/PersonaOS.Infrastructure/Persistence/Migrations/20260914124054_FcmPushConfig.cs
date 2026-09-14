using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonaOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FcmPushConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FcmClientConfigJson",
                table: "InstanceConfig",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FcmProjectId",
                table: "InstanceConfig",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FcmServiceAccountEncrypted",
                table: "InstanceConfig",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FcmClientConfigJson",
                table: "InstanceConfig");

            migrationBuilder.DropColumn(
                name: "FcmProjectId",
                table: "InstanceConfig");

            migrationBuilder.DropColumn(
                name: "FcmServiceAccountEncrypted",
                table: "InstanceConfig");
        }
    }
}
