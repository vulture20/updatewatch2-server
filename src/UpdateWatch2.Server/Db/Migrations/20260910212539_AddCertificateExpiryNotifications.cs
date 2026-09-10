using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UpdateWatch2.Server.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddCertificateExpiryNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CertificateExpiryWarningLeadDays",
                table: "AdminSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.AddColumn<string>(
                name: "NotificationRecipientAddress",
                table: "AdminSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CertificateNotificationStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LastCaWarningThumbprint = table.Column<string>(type: "TEXT", nullable: true),
                    LastCaWarningSentAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastServerLeafRenewalThumbprint = table.Column<string>(type: "TEXT", nullable: true),
                    LastServerLeafRenewalNotAfter = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastServerLeafRenewalNotifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CertificateNotificationStates", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CertificateNotificationStates");

            migrationBuilder.DropColumn(
                name: "CertificateExpiryWarningLeadDays",
                table: "AdminSettings");

            migrationBuilder.DropColumn(
                name: "NotificationRecipientAddress",
                table: "AdminSettings");
        }
    }
}
