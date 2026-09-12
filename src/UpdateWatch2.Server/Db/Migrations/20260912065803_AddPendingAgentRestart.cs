using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UpdateWatch2.Server.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingAgentRestart : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastRestartCompletedAt",
                table: "Agents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastRestartErrorDetail",
                table: "Agents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastRestartOutcome",
                table: "Agents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PendingRestartRequestedAt",
                table: "Agents",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastRestartCompletedAt",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "LastRestartErrorDetail",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "LastRestartOutcome",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "PendingRestartRequestedAt",
                table: "Agents");
        }
    }
}
