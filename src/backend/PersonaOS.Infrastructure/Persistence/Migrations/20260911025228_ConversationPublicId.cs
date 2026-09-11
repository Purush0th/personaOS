using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonaOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConversationPublicId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "Conversations",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            // Existing rows all land on "" from the default above, which would make the unique
            // index below fail on any instance with more than one conversation. Give each one a
            // distinct id first. randomblob(4) hexes to exactly 8 characters, matching the
            // length the application generates.
            migrationBuilder.Sql(
                "UPDATE Conversations SET PublicId = lower(hex(randomblob(4))) " +
                "WHERE PublicId IS NULL OR PublicId = '';");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_PublicId",
                table: "Conversations",
                column: "PublicId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Conversations_PublicId",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "Conversations");
        }
    }
}
