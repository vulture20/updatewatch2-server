using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UpdateWatch2.Server.Db.Migrations
{
    /// <summary>
    /// Data-only repair migration — no schema change. The
    /// AddActiveDirectorySettings migration (20260903221533) ALTERed the
    /// already-existing AdminSettings table and, for any row that already
    /// existed at that point (i.e. any server installed before AD login
    /// existed), backfilled the new columns with SQL-level defaults that
    /// don't match AdOptions' actual C# defaults: AdEncryption/
    /// AdUserSearchFilter got "" instead of "StartTls"/the AD-shaped filter
    /// string, and AdPort got 0 instead of 389. AdminSettingsStore.Apply
    /// unconditionally does Enum.Parse&lt;AdEncryption&gt;(row.AdEncryption),
    /// which throws ArgumentException("Must specify valid information for
    /// parsing in the string.") for the empty string — crashing the whole
    /// app at startup for anyone upgrading an existing deployment. A brand
    /// new install is unaffected: AdminSettingsStore.SeedFromDefaults only
    /// ever runs against an empty table and always writes the real
    /// defaults, so this only ever repairs a pre-existing row. Reproduced
    /// live before writing this fix: applied migrations only up through
    /// AddAdminSettings, inserted a row matching what SeedFromDefaults
    /// wrote back when only the pre-AD columns existed, applied
    /// AddActiveDirectorySettings, and confirmed the exact reported
    /// exception on startup — then confirmed this migration fixes it.
    /// </summary>
    public partial class FixLegacyAdSettingsDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"AdminSettings\" SET \"AdEncryption\" = 'StartTls' WHERE \"AdEncryption\" = '';");
            migrationBuilder.Sql("UPDATE \"AdminSettings\" SET \"AdPort\" = 389 WHERE \"AdPort\" = 0;");
            migrationBuilder.Sql("UPDATE \"AdminSettings\" SET \"AdUserSearchFilter\" = '(&(objectClass=user)(sAMAccountName={0}))' WHERE \"AdUserSearchFilter\" = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally a no-op: this repairs data that was already
            // wrong (indistinguishable from a row an admin genuinely
            // configured this way after the fact, since the columns being
            // corrected are plain strings/ints, not a "was this ever set"
            // flag) — there's nothing correct to revert to.
        }
    }
}
