namespace UpdateWatch2.Server.Notifications;

/// <summary>
/// Holds the last result of <see cref="IEmailNotificationService.IsHealthyAsync"/>,
/// refreshed periodically by <see cref="SmtpHealthCheckWorker"/> rather than
/// on every read (updatewatch2-server#12 — "wire the SMTP warning banner to
/// the real reachability check, not just 'is it configured'"). A live TCP
/// probe on every <c>GET /api/admin/notifications/smtp-health</c> call
/// would add a real SMTP round trip to every settings-page load/poll — the
/// explicit trade-off the issue itself called out, and the one this
/// codebase chose (matching how <see cref="Certificates.CertificateExpiryWorker"/>
/// and every other periodic-check worker here already caches its own
/// result rather than probing on demand). Registered as a singleton — this
/// is process-wide runtime state, not per-request or persisted, so it
/// starts as "healthy: unknown" (<see cref="CheckedAt"/> null) until the
/// worker's own first tick.
/// </summary>
public interface ISmtpHealthCache
{
    bool IsHealthy { get; }

    /// <summary>Null until the worker has run at least once.</summary>
    DateTimeOffset? CheckedAt { get; }

    void Update(bool isHealthy, DateTimeOffset checkedAt);
}

public class SmtpHealthCache : ISmtpHealthCache
{
    private readonly object _lock = new();
    private bool _isHealthy;
    private DateTimeOffset? _checkedAt;

    public bool IsHealthy
    {
        get { lock (_lock) return _isHealthy; }
    }

    public DateTimeOffset? CheckedAt
    {
        get { lock (_lock) return _checkedAt; }
    }

    public void Update(bool isHealthy, DateTimeOffset checkedAt)
    {
        lock (_lock)
        {
            _isHealthy = isHealthy;
            _checkedAt = checkedAt;
        }
    }
}
