using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UpdateWatch2.Server.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddCertificateValiditySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // defaultValue is deliberately 730, not EF's auto-generated 0
            // (the CLR default for int, since there's no fluent
            // .HasDefaultValue() config) — matching CertificateOptions'
            // real default exactly. A mismatch here would silently apply
            // the wrong agent certificate validity to every row that
            // already existed before this migration ran, the same class
            // of bug FixLegacyAdSettingsDefaults.cs had to repair (see its
            // doc comment) — this migration avoids needing a companion
            // repair migration by getting the default right the first time.
            migrationBuilder.AddColumn<int>(
                name: "AgentCertificateValidityDays",
                table: "AdminSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 730);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgentCertificateValidityDays",
                table: "AdminSettings");
        }
    }
}
