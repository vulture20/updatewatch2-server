using UpdateWatch2.Server.AgentUpdates;
using UpdateWatch2.Server.Auth;
using UpdateWatch2.Server.Certificates;
using UpdateWatch2.Server.Notifications;

namespace UpdateWatch2.Server.Admin;

/// <summary>
/// Live, in-memory-cached, database-persisted admin settings. Consumers
/// that need current values at request time (<see cref="Auth.BruteForceLoginService"/>,
/// <see cref="Notifications.EmailNotificationService"/>) read the property
/// getters directly rather than going back to the database on every call —
/// <see cref="UpdateAsync"/> refreshes the cache as part of persisting a
/// change, so updates take effect immediately, not on next restart.
/// </summary>
public interface IAdminSettingsStore
{
    BruteForceOptions BruteForce { get; }

    SmtpOptions Smtp { get; }

    NotificationThresholdOptions NotificationThresholds { get; }

    AdOptions Ad { get; }

    /// <summary>Admin-configurable agent client certificate validity (updatewatch2-server#9).</summary>
    CertificateOptions Certificate { get; }

    /// <summary>Admin-configurable agent-auto-update toggle and optional GitHub token (updatewatch2-server#14).</summary>
    AgentAutoUpdateOptions AgentAutoUpdate { get; }

    /// <summary>
    /// The persisted log level. Reflected here immediately on change, and
    /// (server v0.30.2, found missing by a user report) now hot-reloads
    /// the running logger's minimum level too — <see cref="AdminSettingsStore.Apply"/>
    /// pushes it straight onto the live <c>IConfiguration</c>'s
    /// <c>Logging:LogLevel:Default</c> key, the same mechanism
    /// <c>Program.cs</c> uses at pre-DI startup, so a running container's
    /// `docker logs` reacts to a saved change with no restart needed —
    /// this used to be the one admin setting on this whole interface that
    /// required one.
    /// </summary>
    string LogLevel { get; }

    /// <summary>
    /// Days of audit log history to keep before <c>Audit.AuditLogRetentionWorker</c>'s
    /// periodic cleanup permanently discards older entries — 0 means
    /// unlimited/never discard. Default 90.
    /// </summary>
    int AuditLogRetentionDays { get; }

    /// <summary>Loads the persisted row into the cache, seeding one from appsettings.json's defaults if none exists yet. Call once at startup.</summary>
    Task InitializeAsync(CancellationToken ct = default);

    Task<AdminSettingsDto> UpdateAsync(UpdateAdminSettingsRequest request, CancellationToken ct = default);

    AdminSettingsDto ToDto();
}
