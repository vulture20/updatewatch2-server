using Microsoft.EntityFrameworkCore;
using UpdateWatch2.Server.Db.Entities;

namespace UpdateWatch2.Server.Db;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Agent> Agents => Set<Agent>();

    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    public DbSet<UpdateItem> UpdateItems => Set<UpdateItem>();

    public DbSet<AdminAccount> AdminAccounts => Set<AdminAccount>();

    public DbSet<AdminSettings> AdminSettings => Set<AdminSettings>();

    public DbSet<AgentUpdateState> AgentUpdateStates => Set<AgentUpdateState>();

    public DbSet<UpdateFilter> UpdateFilters => Set<UpdateFilter>();

    public DbSet<CertificateRejectionAcknowledgement> CertificateRejectionAcknowledgements => Set<CertificateRejectionAcknowledgement>();

    public DbSet<CertificateNotificationState> CertificateNotificationStates => Set<CertificateNotificationState>();

    public DbSet<UpdateThresholdNotificationState> UpdateThresholdNotificationStates => Set<UpdateThresholdNotificationState>();

    public DbSet<SessionInvalidation> SessionInvalidations => Set<SessionInvalidation>();

    public DbSet<Schedule> Schedules => Set<Schedule>();

    public DbSet<ScheduleAgent> ScheduleAgents => Set<ScheduleAgent>();

    public DbSet<ScheduleRun> ScheduleRuns => Set<ScheduleRun>();

    public DbSet<ScheduleRunAgent> ScheduleRunAgents => Set<ScheduleRunAgent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Agent>()
            .HasIndex(a => a.Hostname)
            .IsUnique();

        modelBuilder.Entity<AdminAccount>()
            .HasIndex(a => a.Username)
            .IsUnique();

        modelBuilder.Entity<SessionInvalidation>()
            .HasIndex(s => s.Username)
            .IsUnique();

        modelBuilder.Entity<UpdateFilter>()
            .HasIndex(f => f.Name)
            .IsUnique();

        modelBuilder.Entity<UpdateItem>()
            .HasOne(u => u.Agent)
            .WithMany()
            .HasForeignKey(u => u.AgentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ScheduleAgent>()
            .HasKey(sa => new { sa.ScheduleId, sa.Hostname });

        modelBuilder.Entity<ScheduleAgent>()
            .HasOne(sa => sa.Schedule)
            .WithMany(s => s.Agents)
            .HasForeignKey(sa => sa.ScheduleId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ScheduleAgent>()
            .HasOne(sa => sa.Agent)
            .WithMany()
            .HasForeignKey(sa => sa.Hostname)
            .HasPrincipalKey(a => a.Hostname)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ScheduleRun>()
            .HasOne(r => r.Schedule)
            .WithMany()
            .HasForeignKey(r => r.ScheduleId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ScheduleRunAgent>()
            .HasKey(ra => new { ra.ScheduleRunId, ra.Hostname });

        modelBuilder.Entity<ScheduleRunAgent>()
            .HasOne(ra => ra.ScheduleRun)
            .WithMany(r => r.Agents)
            .HasForeignKey(ra => ra.ScheduleRunId)
            .OnDelete(DeleteBehavior.Cascade);

        // Deliberately no formal relationship for ScheduleRunAgent.Hostname
        // -> Agent (unlike ScheduleAgent above): a run's history is meant
        // to survive even if the target agent is later deleted, the same
        // "keep the audit trail" reasoning AuditLogEntry's own free-text
        // Actor field already follows — a plain string, not cascade-deleted
        // alongside the agent it once referred to.
    }
}
