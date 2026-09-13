using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UpdateWatch2.Server.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentOfflineDetection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "OfflineCrossed",
                table: "Agents",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OfflineNotifiedAt",
                table: "Agents",
                type: "TEXT",
                nullable: true);

            // dotnet ef never picks up a non-default C# property initializer
            // as the migration's own AddColumn default (CLAUDE.md) — it
            // scaffolded false/0/false for these three, hand-corrected here
            // to match AdminSettings.AgentOfflineNotificationEnabled/
            // AgentOfflineThresholdMinutes/AgentOnlineRecoveryNotificationEnabled's
            // actual intended defaults, so an upgrading deployment's
            // existing row starts with a real 15-minute threshold and both
            // checkboxes on, not a permanently-offline 0-minute threshold
            // with both silently off.
            migrationBuilder.AddColumn<bool>(
                name: "AgentOfflineNotificationEnabled",
                table: "AdminSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "AgentOfflineThresholdMinutes",
                table: "AdminSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.AddColumn<bool>(
                name: "AgentOnlineRecoveryNotificationEnabled",
                table: "AdminSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OfflineCrossed",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "OfflineNotifiedAt",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "AgentOfflineNotificationEnabled",
                table: "AdminSettings");

            migrationBuilder.DropColumn(
                name: "AgentOfflineThresholdMinutes",
                table: "AdminSettings");

            migrationBuilder.DropColumn(
                name: "AgentOnlineRecoveryNotificationEnabled",
                table: "AdminSettings");
        }
    }
}
