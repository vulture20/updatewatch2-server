using UpdateWatch2.Server.Auth;
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

    /// <summary>
    /// The persisted log level. Reflected here immediately on change, but
    /// — unlike the other settings — does NOT hot-reload the running
    /// logger's minimum level; that only re-reads this value on next
    /// process start (see the comment in Program.cs). CLAUDE.md already
    /// scopes "dynamic log-level push" as a separate, harder problem.
    /// </summary>
    string LogLevel { get; }

    /// <summary>Loads the persisted row into the cache, seeding one from appsettings.json's defaults if none exists yet. Call once at startup.</summary>
    Task InitializeAsync(CancellationToken ct = default);

    Task<AdminSettingsDto> UpdateAsync(UpdateAdminSettingsRequest request, CancellationToken ct = default);

    AdminSettingsDto ToDto();
}
