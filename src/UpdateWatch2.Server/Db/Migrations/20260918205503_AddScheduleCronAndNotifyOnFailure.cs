using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UpdateWatch2.Server.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleCronAndNotifyOnFailure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CronExpression",
                table: "Schedules",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyOnFailure",
                table: "Schedules",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CronExpression",
                table: "Schedules");

            migrationBuilder.DropColumn(
                name: "NotifyOnFailure",
                table: "Schedules");
        }
    }
}
