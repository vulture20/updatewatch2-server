using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UpdateWatch2.Server.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentAutoUpdate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // defaultValue: true, not EF's own auto-generated false — this
            // column's whole point is an "on by default" toggle (CLAUDE.md's
            // "Agent auto-update" spec), which has to hold for an existing
            // deployment upgrading into this migration too, not just a
            // freshly seeded row (AdminSettingsStore.SeedFromDefaults
            // already gets that right for a brand new install).
            migrationBuilder.AddColumn<bool>(
                name: "AgentAutoUpdateEnabled",
                table: "AdminSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "GitHubToken",
                table: "AdminSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AgentUpdateStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LatestVersion = table.Column<string>(type: "TEXT", nullable: true),
                    CheckedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    WindowsInstallerFileName = table.Column<string>(type: "TEXT", nullable: true),
                    WindowsInstallerSha256 = table.Column<string>(type: "TEXT", nullable: true),
                    WindowsInstallerSizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    LinuxDebFileName = table.Column<string>(type: "TEXT", nullable: true),
                    LinuxDebSha256 = table.Column<string>(type: "TEXT", nullable: true),
                    LinuxDebSizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    LinuxRpmFileName = table.Column<string>(type: "TEXT", nullable: true),
                    LinuxRpmSha256 = table.Column<string>(type: "TEXT", nullable: true),
                    LinuxRpmSizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentUpdateStates", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentUpdateStates");

            migrationBuilder.DropColumn(
                name: "AgentAutoUpdateEnabled",
                table: "AdminSettings");

            migrationBuilder.DropColumn(
                name: "GitHubToken",
                table: "AdminSettings");
        }
    }
}
