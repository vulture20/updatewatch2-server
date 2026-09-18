using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UpdateWatch2.Server.Db.Migrations
{
    /// <inheritdoc />
    // Hand-corrected after scaffolding (updatewatch2-agent#22/#23,
    // "wurde beim Selfupdate berücksichtigt, dass es jetzt zusätzliche
    // Releases gibt?") — `dotnet ef migrations add` initially generated a
    // migration that renamed WindowsInstaller*/LinuxDeb*/LinuxRpm* into the
    // new *X64/*Arm64 names by matching column TYPE alone (all nullable
    // TEXT/INTEGER), not by which old column actually corresponds to which
    // new one — it happened to map LinuxRpmFileName -> WindowsInstallerArm64FileName
    // and LinuxDebFileName -> LinuxRpmX64FileName, which would have silently
    // scrambled an already-downloaded agent release's recorded asset
    // filenames on any real upgrading deployment (the RPM's filename
    // relabeled as the Windows arm64 installer's, etc. — the file on disk
    // keeps its real name either way, so this wouldn't even fail loudly,
    // just quietly offer the wrong file under the wrong label). Rewritten
    // by hand so each pre-existing column renames to its own true X64
    // counterpart (the only architecture that ever existed before this
    // migration), and every new Arm64 column is added fresh with no data to
    // carry over — the same "never trust a scaffolded migration's exact
    // column-default/mapping choices without reading what it actually
    // does" discipline CLAUDE.md already documents for a different EF Core
    // migration gotcha.
    public partial class AddAgentUpdateAssetArchitectures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "WindowsInstallerFileName",
                table: "AgentUpdateStates",
                newName: "WindowsInstallerX64FileName");

            migrationBuilder.RenameColumn(
                name: "WindowsInstallerSha256",
                table: "AgentUpdateStates",
                newName: "WindowsInstallerX64Sha256");

            migrationBuilder.RenameColumn(
                name: "WindowsInstallerSizeBytes",
                table: "AgentUpdateStates",
                newName: "WindowsInstallerX64SizeBytes");

            migrationBuilder.RenameColumn(
                name: "LinuxDebFileName",
                table: "AgentUpdateStates",
                newName: "LinuxDebX64FileName");

            migrationBuilder.RenameColumn(
                name: "LinuxDebSha256",
                table: "AgentUpdateStates",
                newName: "LinuxDebX64Sha256");

            migrationBuilder.RenameColumn(
                name: "LinuxDebSizeBytes",
                table: "AgentUpdateStates",
                newName: "LinuxDebX64SizeBytes");

            migrationBuilder.RenameColumn(
                name: "LinuxRpmFileName",
                table: "AgentUpdateStates",
                newName: "LinuxRpmX64FileName");

            migrationBuilder.RenameColumn(
                name: "LinuxRpmSha256",
                table: "AgentUpdateStates",
                newName: "LinuxRpmX64Sha256");

            migrationBuilder.RenameColumn(
                name: "LinuxRpmSizeBytes",
                table: "AgentUpdateStates",
                newName: "LinuxRpmX64SizeBytes");

            migrationBuilder.AddColumn<string>(
                name: "WindowsInstallerArm64FileName",
                table: "AgentUpdateStates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WindowsInstallerArm64Sha256",
                table: "AgentUpdateStates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "WindowsInstallerArm64SizeBytes",
                table: "AgentUpdateStates",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LinuxDebArm64FileName",
                table: "AgentUpdateStates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LinuxDebArm64Sha256",
                table: "AgentUpdateStates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LinuxDebArm64SizeBytes",
                table: "AgentUpdateStates",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LinuxRpmArm64FileName",
                table: "AgentUpdateStates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LinuxRpmArm64Sha256",
                table: "AgentUpdateStates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LinuxRpmArm64SizeBytes",
                table: "AgentUpdateStates",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WindowsInstallerArm64FileName",
                table: "AgentUpdateStates");

            migrationBuilder.DropColumn(
                name: "WindowsInstallerArm64Sha256",
                table: "AgentUpdateStates");

            migrationBuilder.DropColumn(
                name: "WindowsInstallerArm64SizeBytes",
                table: "AgentUpdateStates");

            migrationBuilder.DropColumn(
                name: "LinuxDebArm64FileName",
                table: "AgentUpdateStates");

            migrationBuilder.DropColumn(
                name: "LinuxDebArm64Sha256",
                table: "AgentUpdateStates");

            migrationBuilder.DropColumn(
                name: "LinuxDebArm64SizeBytes",
                table: "AgentUpdateStates");

            migrationBuilder.DropColumn(
                name: "LinuxRpmArm64FileName",
                table: "AgentUpdateStates");

            migrationBuilder.DropColumn(
                name: "LinuxRpmArm64Sha256",
                table: "AgentUpdateStates");

            migrationBuilder.DropColumn(
                name: "LinuxRpmArm64SizeBytes",
                table: "AgentUpdateStates");

            migrationBuilder.RenameColumn(
                name: "WindowsInstallerX64FileName",
                table: "AgentUpdateStates",
                newName: "WindowsInstallerFileName");

            migrationBuilder.RenameColumn(
                name: "WindowsInstallerX64Sha256",
                table: "AgentUpdateStates",
                newName: "WindowsInstallerSha256");

            migrationBuilder.RenameColumn(
                name: "WindowsInstallerX64SizeBytes",
                table: "AgentUpdateStates",
                newName: "WindowsInstallerSizeBytes");

            migrationBuilder.RenameColumn(
                name: "LinuxDebX64FileName",
                table: "AgentUpdateStates",
                newName: "LinuxDebFileName");

            migrationBuilder.RenameColumn(
                name: "LinuxDebX64Sha256",
                table: "AgentUpdateStates",
                newName: "LinuxDebSha256");

            migrationBuilder.RenameColumn(
                name: "LinuxDebX64SizeBytes",
                table: "AgentUpdateStates",
                newName: "LinuxDebSizeBytes");

            migrationBuilder.RenameColumn(
                name: "LinuxRpmX64FileName",
                table: "AgentUpdateStates",
                newName: "LinuxRpmFileName");

            migrationBuilder.RenameColumn(
                name: "LinuxRpmX64Sha256",
                table: "AgentUpdateStates",
                newName: "LinuxRpmSha256");

            migrationBuilder.RenameColumn(
                name: "LinuxRpmX64SizeBytes",
                table: "AgentUpdateStates",
                newName: "LinuxRpmSizeBytes");
        }
    }
}
