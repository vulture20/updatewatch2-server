using Microsoft.EntityFrameworkCore;
using UpdateWatch2.Server.Db.Entities;

namespace UpdateWatch2.Server.Db;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Agent> Agents => Set<Agent>();

    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    public DbSet<UpdateItem> UpdateItems => Set<UpdateItem>();

    public DbSet<AdminAccount> AdminAccounts => Set<AdminAccount>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Agent>()
            .HasIndex(a => a.Hostname)
            .IsUnique();

        modelBuilder.Entity<AdminAccount>()
            .HasIndex(a => a.Username)
            .IsUnique();

        modelBuilder.Entity<UpdateItem>()
            .HasOne(u => u.Agent)
            .WithMany()
            .HasForeignKey(u => u.AgentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
