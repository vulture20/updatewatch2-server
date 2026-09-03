using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UpdateWatch2.Server.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdminSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LogLevel = table.Column<string>(type: "TEXT", nullable: false),
                    BruteForceMaxAttempts = table.Column<int>(type: "INTEGER", nullable: false),
                    BruteForceWindowMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    BruteForceLockoutMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    SmtpHost = table.Column<string>(type: "TEXT", nullable: false),
                    SmtpPort = table.Column<int>(type: "INTEGER", nullable: false),
                    SmtpUsername = table.Column<string>(type: "TEXT", nullable: true),
                    SmtpPassword = table.Column<string>(type: "TEXT", nullable: true),
                    SmtpEncryption = table.Column<string>(type: "TEXT", nullable: false),
                    SmtpFromAddress = table.Column<string>(type: "TEXT", nullable: false),
                    SmtpFromName = table.Column<string>(type: "TEXT", nullable: false),
                    NotificationUpdatesPerMachineThreshold = table.Column<int>(type: "INTEGER", nullable: false),
                    NotificationAffectedMachinesThreshold = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminSettings");
        }
    }
}
