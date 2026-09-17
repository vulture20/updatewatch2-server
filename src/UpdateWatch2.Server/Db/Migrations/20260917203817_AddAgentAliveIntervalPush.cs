using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UpdateWatch2.Server.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentAliveIntervalPush : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ActualAliveIntervalMinutes",
                table: "Agents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DesiredAliveIntervalMinutes",
                table: "Agents",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActualAliveIntervalMinutes",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "DesiredAliveIntervalMinutes",
                table: "Agents");
        }
    }
}
