using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonaOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChatMessageUnverifiedClaim : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "UnverifiedClaim",
                table: "ChatMessages",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UnverifiedClaim",
                table: "ChatMessages");
        }
    }
}
