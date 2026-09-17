using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UpdateWatch2.Server.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentPushedSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActualLogLevel",
                table: "Agents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ActualUpdateCheckIntervalMinutes",
                table: "Agents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ActualUpdateCheckJitterSeconds",
                table: "Agents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DesiredLogLevel",
                table: "Agents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DesiredUpdateCheckIntervalMinutes",
                table: "Agents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DesiredUpdateCheckJitterSeconds",
                table: "Agents",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActualLogLevel",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "ActualUpdateCheckIntervalMinutes",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "ActualUpdateCheckJitterSeconds",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "DesiredLogLevel",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "DesiredUpdateCheckIntervalMinutes",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "DesiredUpdateCheckJitterSeconds",
                table: "Agents");
        }
    }
}
