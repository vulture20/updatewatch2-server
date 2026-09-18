using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UpdateWatch2.Server.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PendingConditionalRebootScheduleRunId",
                table: "Agents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PendingInstallScheduleRunId",
                table: "Agents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PendingRebootScheduleRunId",
                table: "Agents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Agents_Hostname",
                table: "Agents",
                column: "Hostname");

            migrationBuilder.CreateTable(
                name: "Schedules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ScheduleType = table.Column<int>(type: "INTEGER", nullable: false),
                    Pattern = table.Column<int>(type: "INTEGER", nullable: true),
                    OnceAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    WeeklyDays = table.Column<string>(type: "TEXT", nullable: true),
                    TimeOfDay = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    IntervalDays = table.Column<int>(type: "INTEGER", nullable: true),
                    IntervalStartDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ActionInstall = table.Column<bool>(type: "INTEGER", nullable: false),
                    ActionReboot = table.Column<bool>(type: "INTEGER", nullable: false),
                    RebootOnlyIfRequired = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeadlineHours = table.Column<int>(type: "INTEGER", nullable: false),
                    NextRunAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastRunAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Schedules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleAgents",
                columns: table => new
                {
                    ScheduleId = table.Column<int>(type: "INTEGER", nullable: false),
                    Hostname = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleAgents", x => new { x.ScheduleId, x.Hostname });
                    table.ForeignKey(
                        name: "FK_ScheduleAgents_Agents_Hostname",
                        column: x => x.Hostname,
                        principalTable: "Agents",
                        principalColumn: "Hostname",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScheduleAgents_Schedules_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "Schedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ScheduleId = table.Column<int>(type: "INTEGER", nullable: false),
                    FiredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DeadlineAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ActionInstallSnapshot = table.Column<bool>(type: "INTEGER", nullable: false),
                    ActionRebootSnapshot = table.Column<bool>(type: "INTEGER", nullable: false),
                    RebootOnlyIfRequiredSnapshot = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduleRuns_Schedules_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "Schedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleRunAgents",
                columns: table => new
                {
                    ScheduleRunId = table.Column<int>(type: "INTEGER", nullable: false),
                    Hostname = table.Column<string>(type: "TEXT", nullable: false),
                    InstallStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    RebootStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorDetail = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleRunAgents", x => new { x.ScheduleRunId, x.Hostname });
                    table.ForeignKey(
                        name: "FK_ScheduleRunAgents_ScheduleRuns_ScheduleRunId",
                        column: x => x.ScheduleRunId,
                        principalTable: "ScheduleRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleAgents_Hostname",
                table: "ScheduleAgents",
                column: "Hostname");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleRuns_ScheduleId",
                table: "ScheduleRuns",
                column: "ScheduleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScheduleAgents");

            migrationBuilder.DropTable(
                name: "ScheduleRunAgents");

            migrationBuilder.DropTable(
                name: "ScheduleRuns");

            migrationBuilder.DropTable(
                name: "Schedules");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Agents_Hostname",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "PendingConditionalRebootScheduleRunId",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "PendingInstallScheduleRunId",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "PendingRebootScheduleRunId",
                table: "Agents");
        }
    }
}
