namespace UpdateWatch2.Server.Db.Entities;

/// <summary>
/// Single-row table (same one-row convention as <see cref="AdminSettings"/>/
/// <see cref="AgentUpdateState"/>) recording when an admin last acknowledged
/// the certificate-rejection warning banner (CLAUDE.md "Certificate
/// rejection visibility") — <see cref="Certificates.CertificateRejectionService.GetStatusAsync"/>
/// only counts a rejection newer than <see cref="AcknowledgedAt"/> toward
/// the banner, so acknowledging silences everything seen so far without
/// waiting for the 24h lookback window to age it out on its own, while a
/// genuinely new rejection after that point still shows up immediately.
/// Deliberately does NOT affect <see cref="Certificates.CertificateRejectionService.GetRecentByHostnameAsync"/>
/// (the per-agent warning icon) — acknowledging the banner means "an admin
/// has seen this", not "the underlying agent's certificate problem is
/// fixed"; only a later successful heartbeat clears that.
/// </summary>
public class CertificateRejectionAcknowledgement
{
    public int Id { get; set; }

    public DateTimeOffset AcknowledgedAt { get; set; }

    public required string AcknowledgedBy { get; set; }
}
