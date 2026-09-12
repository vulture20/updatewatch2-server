using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UpdateWatch2.Server.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationThresholdEnabledFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // dotnet ef never picks up a non-default C# property initializer
            // as the migration's own AddColumn default (CLAUDE.md) — it
            // scaffolded defaultValue: false for both, hand-corrected to
            // true here to match AdminSettings.NotificationAffectedMachinesEnabled/
            // NotificationUpdatesPerMachineEnabled's actual intended default,
            // so an upgrading deployment's existing row starts with both
            // checkboxes on rather than silently off.
            migrationBuilder.AddColumn<bool>(
                name: "NotificationAffectedMachinesEnabled",
                table: "AdminSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotificationUpdatesPerMachineEnabled",
                table: "AdminSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "UpdateThresholdNotificationStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UpdatesPerMachineCrossed = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatesPerMachineNotifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    AffectedMachinesCrossed = table.Column<bool>(type: "INTEGER", nullable: false),
                    AffectedMachinesNotifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UpdateThresholdNotificationStates", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UpdateThresholdNotificationStates");

            migrationBuilder.DropColumn(
                name: "NotificationAffectedMachinesEnabled",
                table: "AdminSettings");

            migrationBuilder.DropColumn(
                name: "NotificationUpdatesPerMachineEnabled",
                table: "AdminSettings");
        }
    }
}
