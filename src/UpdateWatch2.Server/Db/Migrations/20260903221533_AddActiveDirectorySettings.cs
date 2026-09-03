using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UpdateWatch2.Server.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddActiveDirectorySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdBaseDn",
                table: "AdminSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AdBindDn",
                table: "AdminSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AdBindPassword",
                table: "AdminSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AdEnabled",
                table: "AdminSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AdEncryption",
                table: "AdminSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AdHost",
                table: "AdminSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AdLoginGroupDn",
                table: "AdminSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "AdPort",
                table: "AdminSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AdUserSearchFilter",
                table: "AdminSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdBaseDn",
                table: "AdminSettings");

            migrationBuilder.DropColumn(
                name: "AdBindDn",
                table: "AdminSettings");

            migrationBuilder.DropColumn(
                name: "AdBindPassword",
                table: "AdminSettings");

            migrationBuilder.DropColumn(
                name: "AdEnabled",
                table: "AdminSettings");

            migrationBuilder.DropColumn(
                name: "AdEncryption",
                table: "AdminSettings");

            migrationBuilder.DropColumn(
                name: "AdHost",
                table: "AdminSettings");

            migrationBuilder.DropColumn(
                name: "AdLoginGroupDn",
                table: "AdminSettings");

            migrationBuilder.DropColumn(
                name: "AdPort",
                table: "AdminSettings");

            migrationBuilder.DropColumn(
                name: "AdUserSearchFilter",
                table: "AdminSettings");
        }
    }
}
