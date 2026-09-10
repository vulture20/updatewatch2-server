using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UpdateWatch2.Server.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLogRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // defaultValue: 90, not EF's own auto-generated 0 — the same
            // "the migration default has to match the class-level default,
            // not the CLR zero-value" fix CLAUDE.md already documents for
            // AgentAutoUpdateEnabled/AgentAutoUpdateCheckIntervalHours'
            // own migrations. Doubly important here, not just "slightly
            // wrong": 0 is the actual "unlimited — never discard" sentinel
            // for this column (see AdminSettings.AuditLogRetentionDays's
            // doc comment), so a leftover 0 wouldn't just under- or
            // over-retain for an upgrading deployment's existing row — it
            // would silently turn retention off entirely, the opposite of
            // this feature's whole point.
            migrationBuilder.AddColumn<int>(
                name: "AuditLogRetentionDays",
                table: "AdminSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 90);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AuditLogRetentionDays",
                table: "AdminSettings");
        }
    }
}
