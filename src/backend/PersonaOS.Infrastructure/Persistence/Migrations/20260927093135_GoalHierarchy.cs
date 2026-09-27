using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonaOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GoalHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The "dropped" status is gone (owner, 2026-09-27): a goal that no longer matters is
            // deleted. Dropped goals are deleted here the way GoalService deletes a goal: their
            // tasks and planner items keep going without a goal, their comments and attachment
            // records go with them.
            migrationBuilder.Sql("""
                UPDATE BoardTasks SET GoalId = NULL WHERE GoalId IN (SELECT Id FROM Goals WHERE Status = 'dropped');
                UPDATE PlannerItems SET GoalId = NULL WHERE GoalId IN (SELECT Id FROM Goals WHERE Status = 'dropped');
                UPDATE Reminders SET GoalId = NULL WHERE GoalId IN (SELECT Id FROM Goals WHERE Status = 'dropped');
                DELETE FROM WorkItemComments WHERE ItemType = 'goal' AND ItemId IN (SELECT Id FROM Goals WHERE Status = 'dropped');
                DELETE FROM WorkItemAttachments WHERE ItemType = 'goal' AND ItemId IN (SELECT Id FROM Goals WHERE Status = 'dropped');
                DELETE FROM Goals WHERE Status = 'dropped';
                """);

            // SQLite adds a nullable column with its foreign key in place. EF's AddColumn +
            // AddForeignKey rebuilds the whole table instead, outside the migration's transaction,
            // so a crash half-way would leave the database needing repair by hand.
            migrationBuilder.Sql(
                "ALTER TABLE Goals ADD COLUMN ParentGoalId INTEGER NULL REFERENCES Goals (Id) ON DELETE RESTRICT;");

            migrationBuilder.CreateIndex(
                name: "IX_Goals_ParentGoalId",
                table: "Goals",
                column: "ParentGoalId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Goals_Goals_ParentGoalId",
                table: "Goals");

            migrationBuilder.DropIndex(
                name: "IX_Goals_ParentGoalId",
                table: "Goals");

            migrationBuilder.DropColumn(
                name: "ParentGoalId",
                table: "Goals");
        }
    }
}
