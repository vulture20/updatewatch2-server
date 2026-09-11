using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UpdateWatch2.Server.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddLastInstallErrorDetail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastInstallErrorDetail",
                table: "Agents",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastInstallErrorDetail",
                table: "Agents");
        }
    }
}
