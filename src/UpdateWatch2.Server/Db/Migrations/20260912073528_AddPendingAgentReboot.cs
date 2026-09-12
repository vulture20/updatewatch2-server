using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UpdateWatch2.Server.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingAgentReboot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BootTimeUtc",
                table: "Agents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastRebootCompletedAt",
                table: "Agents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastRebootErrorDetail",
                table: "Agents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastRebootOutcome",
                table: "Agents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PendingRebootRequestedAt",
                table: "Agents",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BootTimeUtc",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "LastRebootCompletedAt",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "LastRebootErrorDetail",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "LastRebootOutcome",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "PendingRebootRequestedAt",
                table: "Agents");
        }
    }
}
