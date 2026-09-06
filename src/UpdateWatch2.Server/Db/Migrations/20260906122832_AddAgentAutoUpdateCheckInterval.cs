using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UpdateWatch2.Server.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentAutoUpdateCheckInterval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // defaultValue: 6, not EF's own auto-generated 0 — the same
            // "the migration default has to match the class-level default,
            // not the CLR zero-value, or an upgrading deployment's existing
            // row silently gets the wrong value" fix already applied to
            // AgentAutoUpdateEnabled's own migration. A leftover 0 here
            // would be actively dangerous, not just wrong: this worker's
            // Math.Max(0, ...) clamp treats 0 as "wait zero time", so an
            // existing production deployment upgrading through this
            // migration would start hammering GitHub's API in a hot loop
            // with no delay at all, rather than merely defaulting to a
            // slightly-wrong interval.
            migrationBuilder.AddColumn<int>(
                name: "AgentAutoUpdateCheckIntervalHours",
                table: "AdminSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 6);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgentAutoUpdateCheckIntervalHours",
                table: "AdminSettings");
        }
    }
}
