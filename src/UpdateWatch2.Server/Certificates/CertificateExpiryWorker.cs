using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using UpdateWatch2.Server.Admin;
using UpdateWatch2.Server.Audit;
using UpdateWatch2.Server.Db;
using UpdateWatch2.Server.Db.Entities;
using UpdateWatch2.Server.Notifications;

namespace UpdateWatch2.Server.Certificates;

/// <summary>
/// Periodically checks the CA root and the server's own TLS leaf for
/// approaching expiry (<see cref="CertificateOptions.CertificateExpiryWarningLeadDays"/>,
/// default 60 — matches the agent's own <c>CertificateRenewalLeadTimeDays</c>
/// default), at the user's explicit request ("Meldung per Mail, falls das
/// CA- oder Server-Zertifikat abläuft. Auch bei eventueller automatischer
/// Erneuerung."). This project's third server-side <see cref="BackgroundService"/>,
/// after <see cref="AgentUpdates.AgentUpdateCheckWorker"/> and
/// <see cref="AuditLogRetentionWorker"/> — same shape as both: a check
/// immediately on startup, then every <see cref="_checkInterval"/> (24
/// hours in production), re-reading the live admin-configured lead time on
/// every tick.
///
/// The two certificates get genuinely different treatment, not just the
/// same warning twice:
/// <list type="bullet">
/// <item>
/// The <b>server leaf</b> is self-healing — <see cref="ICertificateAuthority.RenewServerLeafIfNearExpiry"/>
/// runs unconditionally on every tick, regardless of whether email is even
/// configured, since an expired server leaf breaks every agent's mTLS
/// connection outright and that's worth fixing on its own merits, not just
/// worth mentioning. The email here is a courtesy "this happened, here's
/// the new expiry, no action needed" notice, not a call to action.
/// </item>
/// <item>
/// The <b>CA root</b> never renews itself — <c>InternalCertificateAuthority</c>'s
/// own doc comment is explicit that root rotation is deliberately a rare,
/// multi-step admin action (<see cref="ICertificateAuthority.PrepareRotation"/>/
/// <see cref="ICertificateAuthority.ActivateRotation"/>/<see cref="ICertificateAuthority.RetirePreviousRoot"/>),
/// never something this class does on its own. So this is a pure warning:
/// "plan a rotation", with nothing this worker can do to fix it for the
/// admin.
/// </item>
/// </list>
///
/// A new <see cref="Db.Entities.CertificateNotificationState"/> singleton
/// row (same one-row-table convention as <see cref="Db.Entities.AdminSettings"/>/
/// <see cref="Db.Entities.AgentUpdateState"/>) tracks, by thumbprint, which
/// generation of each certificate has already been audit-logged/emailed
/// about — re-sending the identical warning every single day for 60 days
/// straight would make the audit log unusable and would very quickly
/// train an admin to ignore these emails entirely. A CA-root warning is
/// naturally durable as-is: the root's thumbprint doesn't change on its
/// own, so "already warned for this thumbprint" only clears once an admin
/// actually rotates it. A server-leaf renewal is different —
/// <see cref="ICertificateAuthority.RenewServerLeafIfNearExpiry"/> only
/// ever returns non-null on the one tick the renewal actually happens, so
/// the state row's real job there is letting a LATER tick retry the
/// notification email alone (without re-renewing anything) if the first
/// send attempt failed.
///
/// <see cref="CertificateOptions.CertificateExpiryNotificationsEnabled"/>
/// (server v0.30.1, at the user's explicit request — an explicit off
/// switch, not just the implicit "no recipient configured" one) folds
/// into the same "can we actually email about this" gate a missing
/// recipient already produces: disabled behaves exactly like no
/// recipient, in both branches — audit-logged immediately, no email
/// attempt, nothing left pending to retry. It never affects the server
/// leaf's own unconditional self-renewal, only whether anyone gets
/// emailed about either certificate.
///
/// Both branches follow the same "audit-log and mark done only once
/// actually handled" rule: if there's no recipient configured, "handled"
/// means immediately (nothing to retry); if there is, "handled" means the
/// email actually sent successfully — a transient SMTP failure leaves the
/// state row untouched, so the exact same audit-log-plus-email attempt
/// retries whole on the next tick rather than the audit trail recording an
/// email that never actually went out.
/// </summary>
public class CertificateExpiryWorker(
    IServiceScopeFactory scopeFactory,
    IAdminSettingsStore settingsStore,
    ICertificateAuthority certificateAuthority,
    ILogger<CertificateExpiryWorker> logger,
    TimeSpan? checkInterval = null) : BackgroundService
{
    public static readonly TimeSpan DefaultCheckInterval = TimeSpan.FromHours(24);

    private readonly TimeSpan _checkInterval = checkInterval ?? DefaultCheckInterval;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Certificate expiry check failed unexpectedly.");
            }

            try
            {
                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task CheckOnceAsync(CancellationToken ct)
    {
        var leadDays = settingsStore.Certificate.CertificateExpiryWarningLeadDays;
        var leadTime = TimeSpan.FromDays(leadDays);
        var smtp = settingsStore.Smtp;
        var recipient = smtp.NotificationRecipientAddress;
        // The explicit CertificateExpiryNotificationsEnabled toggle folds
        // into the same "can we actually email about this" gate a missing
        // recipient already produces — disabling it behaves exactly like
        // leaving the recipient empty: audit-log immediately, no email
        // attempt, nothing left pending to retry.
        var canEmail = smtp.IsConfigured && !string.IsNullOrWhiteSpace(recipient) && settingsStore.Certificate.CertificateExpiryNotificationsEnabled;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var auditLog = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

        var state = await db.CertificateNotificationStates.SingleOrDefaultAsync(ct);
        if (state is null)
        {
            state = new CertificateNotificationState();
            db.CertificateNotificationStates.Add(state);
        }

        var stateChanged = false;
        stateChanged |= await CheckServerLeafAsync(scope, db, auditLog, state, leadTime, canEmail, recipient, ct);
        stateChanged |= await CheckCaRootAsync(scope, auditLog, state, leadTime, leadDays, canEmail, recipient, ct);

        if (stateChanged)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<bool> CheckServerLeafAsync(
        IServiceScope scope, AppDbContext db, IAuditLogService auditLog, CertificateNotificationState state,
        TimeSpan leadTime, bool canEmail, string? recipient, CancellationToken ct)
    {
        var changed = false;

        var renewed = certificateAuthority.RenewServerLeafIfNearExpiry(leadTime);
        if (renewed is not null)
        {
            var thumbprint = renewed.GetCertHashString(HashAlgorithmName.SHA256);
            logger.LogInformation("Server certificate was renewed automatically; new thumbprint {Thumbprint}, expires {NotAfter:O}.", thumbprint, renewed.NotAfter);
            await auditLog.LogAsync("system", "certificate.server-leaf.renewed", $"Thumbprint {thumbprint}, expires {renewed.NotAfter:O}", ct);
            state.LastServerLeafRenewalThumbprint = thumbprint;
            state.LastServerLeafRenewalNotAfter = renewed.NotAfter;
            state.LastServerLeafRenewalNotifiedAt = null;
            changed = true;
        }

        if (state.LastServerLeafRenewalThumbprint is not null && state.LastServerLeafRenewalNotifiedAt is null)
        {
            if (!canEmail)
            {
                state.LastServerLeafRenewalNotifiedAt = DateTimeOffset.UtcNow;
                changed = true;
            }
            else
            {
                try
                {
                    var email = scope.ServiceProvider.GetRequiredService<IEmailNotificationService>();
                    await email.SendNotificationAsync(
                        recipient!,
                        "UpdateWatch2: server certificate renewed automatically",
                        "The UpdateWatch2 server's own TLS certificate (presented to agents for mutual TLS) "
                            + $"was renewed automatically as it approached expiry.\n\nNew expiry: {state.LastServerLeafRenewalNotAfter:yyyy-MM-dd}\n\n"
                            + "No action is required — this is an informational notice.",
                        "UpdateWatch2: Serverzertifikat automatisch erneuert",
                        "Das eigene TLS-Zertifikat des UpdateWatch2-Servers (das den Agents für mTLS präsentiert wird) "
                            + $"wurde automatisch erneuert, da es sich dem Ablaufdatum näherte.\n\nNeues Ablaufdatum: {state.LastServerLeafRenewalNotAfter:yyyy-MM-dd}\n\n"
                            + "Es ist keine Aktion erforderlich — dies ist eine reine Information.",
                        ct);
                    state.LastServerLeafRenewalNotifiedAt = DateTimeOffset.UtcNow;
                    changed = true;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Failed to send the server-certificate-renewed notification email.");
                }
            }
        }

        return changed;
    }

    private async Task<bool> CheckCaRootAsync(
        IServiceScope scope, IAuditLogService auditLog, CertificateNotificationState state,
        TimeSpan leadTime, int leadDays, bool canEmail, string? recipient, CancellationToken ct)
    {
        var root = certificateAuthority.RootCertificate;
        if (root.NotAfter > DateTimeOffset.UtcNow.Add(leadTime))
        {
            return false;
        }

        var rootThumbprint = root.GetCertHashString(HashAlgorithmName.SHA256);
        logger.LogWarning(
            "CA root certificate {Thumbprint} expires {NotAfter:O} — within the configured {LeadDays}-day warning window; a manual root rotation is required (it does not renew itself).",
            rootThumbprint, root.NotAfter, leadDays);

        if (state.LastCaWarningThumbprint == rootThumbprint)
        {
            return false;
        }

        if (!canEmail)
        {
            await auditLog.LogAsync("system", "certificate.ca-root.expiry-warning", $"Thumbprint {rootThumbprint}, expires {root.NotAfter:O} (no notification recipient configured)", ct);
            state.LastCaWarningThumbprint = rootThumbprint;
            state.LastCaWarningSentAt = DateTimeOffset.UtcNow;
            return true;
        }

        try
        {
            var email = scope.ServiceProvider.GetRequiredService<IEmailNotificationService>();
            await email.SendNotificationAsync(
                recipient!,
                "UpdateWatch2: CA root certificate expiring soon",
                $"The UpdateWatch2 internal CA root certificate will expire on {root.NotAfter:yyyy-MM-dd}.\n\n"
                    + "This certificate does not renew itself — plan a root rotation well ahead of that date "
                    + "(Administration → Certificates → CA root rotation).",
                "UpdateWatch2: CA-Wurzelzertifikat läuft bald ab",
                $"Das interne CA-Wurzelzertifikat von UpdateWatch2 läuft am {root.NotAfter:yyyy-MM-dd} ab.\n\n"
                    + "Dieses Zertifikat erneuert sich nicht von selbst — plane eine Root-Rotation rechtzeitig vor diesem Datum "
                    + "(Einstellungen → Zertifikate → CA-Root-Rotation).",
                ct);
            await auditLog.LogAsync("system", "certificate.ca-root.expiry-warning", $"Thumbprint {rootThumbprint}, expires {root.NotAfter:O}", ct);
            state.LastCaWarningThumbprint = rootThumbprint;
            state.LastCaWarningSentAt = DateTimeOffset.UtcNow;
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to send the CA-root expiry warning email.");
            return false;
        }
    }
}
