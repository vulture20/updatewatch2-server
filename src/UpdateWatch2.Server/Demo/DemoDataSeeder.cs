using Microsoft.EntityFrameworkCore;
using UpdateWatch2.Server.Db;
using UpdateWatch2.Server.Db.Entities;

namespace UpdateWatch2.Server.Demo;

public class DemoDataSeeder(AppDbContext db, ILogger<DemoDataSeeder> logger) : IDemoDataSeeder
{
    /// <summary>
    /// Every seeded hostname starts with this — both the idempotency key
    /// (see <see cref="EnsureSeededAsync"/>) and a way to recognize demo
    /// data at a glance without any separate "is this fake" flag/column.
    /// </summary>
    public const string HostnamePrefix = "demo-";

    public async Task EnsureSeededAsync(CancellationToken ct = default)
    {
        if (await db.Agents.AnyAsync(a => a.Hostname.StartsWith(HostnamePrefix), ct))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;

        var buchhaltung = new Agent
        {
            Hostname = "demo-ws-buchhaltung-01",
            DnsName = "demo-ws-buchhaltung-01.demo.local",
            OperatingSystem = "Windows 11 Pro",
            IpAddress = "192.168.10.21",
            AgentVersion = AgentDemoVersion,
            Approved = true,
            RebootRequired = false,
            PendingUpdateCount = 0,
            LastAliveAt = now.AddMinutes(-3),
        };

        var vertrieb = new Agent
        {
            Hostname = "demo-ws-vertrieb-03",
            DnsName = "demo-ws-vertrieb-03.demo.local",
            OperatingSystem = "Windows 11 Pro",
            IpAddress = "192.168.10.47",
            AgentVersion = AgentDemoVersion,
            Approved = true,
            RebootRequired = true,
            PendingUpdateCount = 2,
            LastAliveAt = now.AddMinutes(-8),
        };

        var fileserver = new Agent
        {
            Hostname = "demo-srv-fileserver",
            DnsName = "demo-srv-fileserver.demo.local",
            OperatingSystem = "Windows Server 2022 Standard",
            IpAddress = "192.168.10.10",
            AgentVersion = AgentDemoVersion,
            Approved = true,
            RebootRequired = false,
            PendingUpdateCount = 1,
            LastAliveAt = now.AddMinutes(-1),
        };

        // Not yet approved — shows the admin approval queue, per CLAUDE.md's
        // onboarding flow. Never checked in, matching a real not-yet-approved
        // agent (no traffic is accepted from it before approval).
        var support = new Agent
        {
            Hostname = "demo-ws-support-07",
            DnsName = "demo-ws-support-07.demo.local",
            OperatingSystem = "Windows 10 Pro",
            IpAddress = "192.168.10.63",
            AgentVersion = AgentDemoVersion,
            Approved = false,
            RebootRequired = false,
            PendingUpdateCount = 0,
            LastAliveAt = null,
        };

        // A Linux entry — previews the planned future agent (CLAUDE.md:
        // "a future Linux agent... shares the same protocol and
        // certificate model").
        var webserver = new Agent
        {
            Hostname = "demo-srv-webserver",
            DnsName = "demo-srv-webserver.demo.local",
            OperatingSystem = "Debian GNU/Linux 12 (bookworm)",
            IpAddress = "192.168.10.15",
            AgentVersion = AgentDemoVersion,
            Approved = true,
            RebootRequired = true,
            PendingUpdateCount = 3,
            LastAliveAt = now.AddMinutes(-15),
        };

        // Stale — looks offline, to show what an agent that stopped
        // checking in looks like in the overview list.
        var legacy = new Agent
        {
            Hostname = "demo-ws-legacy-02",
            DnsName = "demo-ws-legacy-02.demo.local",
            OperatingSystem = "Windows 10 Pro",
            IpAddress = "192.168.10.88",
            AgentVersion = AgentDemoVersion,
            Approved = true,
            RebootRequired = false,
            PendingUpdateCount = 0,
            LastAliveAt = now.AddDays(-6),
        };

        db.Agents.AddRange(buchhaltung, vertrieb, fileserver, support, webserver, legacy);

        // Assigning the Agent navigation property (not AgentId directly)
        // lets EF Core's change tracker resolve the foreign key during the
        // single SaveChangesAsync below, even though these Agent rows
        // don't have real Ids yet.
        db.UpdateItems.AddRange(
            new UpdateItem { Agent = vertrieb, Title = "2026-08 Kumulatives Update für Windows 11 Version 24H2", PackageId = "KB5041585" },
            new UpdateItem { Agent = vertrieb, Title = "Sicherheitsupdate für Microsoft Edge", PackageId = "KB5042250" },
            new UpdateItem { Agent = fileserver, Title = "2026-08 Kumulatives Update für Windows Server 2022", PackageId = "KB5041160" },
            new UpdateItem { Agent = webserver, Title = "openssl Sicherheitsaktualisierung", PackageId = "openssl_3.0.13-1~deb12u1" },
            new UpdateItem { Agent = webserver, Title = "linux-image-amd64 Kernel-Update", PackageId = "linux-image-6.1.0-25-amd64" },
            new UpdateItem { Agent = webserver, Title = "nginx Sicherheitsaktualisierung", PackageId = "nginx_1.22.1-9+deb12u1" });

        await db.SaveChangesAsync(ct);

        logger.LogInformation("Seeded demo data (UPDATEWATCH2_DEMOMODE is enabled): 6 demo agents, 6 pending updates.");
    }

    // A plausible-looking version string, not tied to the real AgentVersion
    // constant (a different repo/assembly) — this is fake data, not a
    // real agent's self-report.
    private const string AgentDemoVersion = "0.3.0";
}
