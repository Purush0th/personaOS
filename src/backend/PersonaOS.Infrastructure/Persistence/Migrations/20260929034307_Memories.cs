using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonaOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Memories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MemoryAutoSave",
                table: "InstanceConfig",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            // Existing installs get the new module switched on, like a fresh one.
            migrationBuilder.Sql("""
                UPDATE InstanceConfig
                SET Features = json_set(Features, '$.memory', json('true'))
                WHERE json_valid(Features) AND json_extract(Features, '$.memory') IS NULL;
                """);

            migrationBuilder.CreateTable(
                name: "Memories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Content = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SourceConversationId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Memories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Memories_Conversations_SourceConversationId",
                        column: x => x.SourceConversationId,
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Memories_SourceConversationId",
                table: "Memories",
                column: "SourceConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_Memories_UpdatedAtUtc",
                table: "Memories",
                column: "UpdatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Memories");

            migrationBuilder.DropColumn(
                name: "MemoryAutoSave",
                table: "InstanceConfig");
        }
    }
}
