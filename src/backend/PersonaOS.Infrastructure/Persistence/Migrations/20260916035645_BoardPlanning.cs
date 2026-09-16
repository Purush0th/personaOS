using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonaOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BoardPlanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "Sprints",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Priority",
                table: "Goals",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "medium");

            migrationBuilder.AddColumn<string>(
                name: "Priority",
                table: "BoardTasks",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "medium");

            migrationBuilder.CreateTable(
                name: "WorkItemAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ItemType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    StorageName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItemAttachments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkItemComments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ItemType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    Author = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItemComments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemAttachments_ItemType_ItemId",
                table: "WorkItemAttachments",
                columns: new[] { "ItemType", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemComments_ItemType_ItemId",
                table: "WorkItemComments",
                columns: new[] { "ItemType", "ItemId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkItemAttachments");

            migrationBuilder.DropTable(
                name: "WorkItemComments");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "Sprints");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "Goals");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "BoardTasks");
        }
    }
}
