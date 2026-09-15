using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonaOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SprintBoard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TaskId",
                table: "PlannerItems",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Number",
                table: "Goals",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Sprints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PlanningNudgedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CommittedPoints = table.Column<int>(type: "INTEGER", nullable: true),
                    AddedPoints = table.Column<int>(type: "INTEGER", nullable: true),
                    RemovedPoints = table.Column<int>(type: "INTEGER", nullable: false),
                    CompletedPoints = table.Column<int>(type: "INTEGER", nullable: true),
                    CarriedOverPoints = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sprints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BoardTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    GoalId = table.Column<int>(type: "INTEGER", nullable: true),
                    Points = table.Column<int>(type: "INTEGER", nullable: true),
                    SprintId = table.Column<int>(type: "INTEGER", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    AddedMidSprint = table.Column<bool>(type: "INTEGER", nullable: false),
                    CarryOverCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoardTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BoardTasks_Goals_GoalId",
                        column: x => x.GoalId,
                        principalTable: "Goals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_BoardTasks_Sprints_SprintId",
                        column: x => x.SprintId,
                        principalTable: "Sprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            // Goals stop nesting. Every sub-goal, at any depth, becomes a task under its top-level
            // goal: completed ones as done, dropped ones are left behind. This runs while
            // ParentGoalId still exists; the column is dropped afterwards.
            migrationBuilder.Sql("""
                WITH RECURSIVE ancestry(Id, RootId) AS (
                    SELECT Id, Id FROM Goals WHERE ParentGoalId IS NULL
                    UNION ALL
                    SELECT g.Id, a.RootId FROM Goals g JOIN ancestry a ON g.ParentGoalId = a.Id
                )
                INSERT INTO BoardTasks (Number, Title, Description, GoalId, Points, SprintId, Status,
                                        SortOrder, AddedMidSprint, CarryOverCount, CompletedAtUtc,
                                        CreatedAtUtc, UpdatedAtUtc)
                SELECT ROW_NUMBER() OVER (ORDER BY g.Id), g.Title, g.Description, a.RootId, NULL, NULL,
                       CASE WHEN g.Status = 'completed' THEN 'done' ELSE 'todo' END,
                       ROW_NUMBER() OVER (ORDER BY g.Id) - 1, 0, 0,
                       CASE WHEN g.Status = 'completed' THEN g.UpdatedAtUtc END,
                       g.CreatedAtUtc, g.UpdatedAtUtc
                FROM Goals g JOIN ancestry a ON a.Id = g.Id
                WHERE g.ParentGoalId IS NOT NULL AND g.Status <> 'dropped';
                """);

            // Planner items linked to a sub-goal now point at its top-level goal.
            migrationBuilder.Sql("""
                WITH RECURSIVE ancestry(Id, RootId) AS (
                    SELECT Id, Id FROM Goals WHERE ParentGoalId IS NULL
                    UNION ALL
                    SELECT g.Id, a.RootId FROM Goals g JOIN ancestry a ON g.ParentGoalId = a.Id
                )
                UPDATE PlannerItems
                SET GoalId = (SELECT RootId FROM ancestry WHERE ancestry.Id = PlannerItems.GoalId)
                WHERE GoalId IN (SELECT Id FROM Goals WHERE ParentGoalId IS NOT NULL);
                """);

            // Remove the sub-goals, leaves first: the self-reference is ON DELETE RESTRICT, which is
            // checked row by row. Goal hierarchies are a few levels deep; eight passes is plenty.
            for (var pass = 0; pass < 8; pass++)
            {
                migrationBuilder.Sql("""
                    DELETE FROM Goals
                    WHERE ParentGoalId IS NOT NULL
                      AND Id NOT IN (SELECT ParentGoalId FROM Goals WHERE ParentGoalId IS NOT NULL);
                    """);
            }

            // GOAL-n keys for the remaining goals, in creation order.
            migrationBuilder.Sql("""
                UPDATE Goals SET Number = (SELECT COUNT(*) FROM Goals g2 WHERE g2.Id <= Goals.Id);
                """);

            // Existing installs get the new module switched on, like a fresh one.
            migrationBuilder.Sql("""
                UPDATE InstanceConfig
                SET Features = json_set(Features, '$.board', json('true'))
                WHERE json_valid(Features) AND json_extract(Features, '$.board') IS NULL;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Goals_Goals_ParentGoalId",
                table: "Goals");

            migrationBuilder.DropIndex(
                name: "IX_Goals_ParentGoalId",
                table: "Goals");

            migrationBuilder.DropColumn(
                name: "ParentGoalId",
                table: "Goals");

            migrationBuilder.CreateIndex(
                name: "IX_PlannerItems_TaskId",
                table: "PlannerItems",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_Goals_Number",
                table: "Goals",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BoardTasks_GoalId",
                table: "BoardTasks",
                column: "GoalId");

            migrationBuilder.CreateIndex(
                name: "IX_BoardTasks_Number",
                table: "BoardTasks",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BoardTasks_SprintId_Status_SortOrder",
                table: "BoardTasks",
                columns: new[] { "SprintId", "Status", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Sprints_Number",
                table: "Sprints",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sprints_Status",
                table: "Sprints",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_PlannerItems_BoardTasks_TaskId",
                table: "PlannerItems",
                column: "TaskId",
                principalTable: "BoardTasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PlannerItems_BoardTasks_TaskId",
                table: "PlannerItems");

            migrationBuilder.DropTable(
                name: "BoardTasks");

            migrationBuilder.DropTable(
                name: "Sprints");

            migrationBuilder.DropIndex(
                name: "IX_PlannerItems_TaskId",
                table: "PlannerItems");

            migrationBuilder.DropIndex(
                name: "IX_Goals_Number",
                table: "Goals");

            migrationBuilder.DropColumn(
                name: "TaskId",
                table: "PlannerItems");

            migrationBuilder.DropColumn(
                name: "Number",
                table: "Goals");

            migrationBuilder.AddColumn<int>(
                name: "ParentGoalId",
                table: "Goals",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Goals_ParentGoalId",
                table: "Goals",
                column: "ParentGoalId");

            migrationBuilder.AddForeignKey(
                name: "FK_Goals_Goals_ParentGoalId",
                table: "Goals",
                column: "ParentGoalId",
                principalTable: "Goals",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
