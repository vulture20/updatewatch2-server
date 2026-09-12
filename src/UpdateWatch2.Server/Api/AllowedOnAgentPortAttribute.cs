namespace UpdateWatch2.Server.Api;

/// <summary>
/// Marks a controller or action as legitimately reachable on the
/// agent-facing Kestrel listener (<c>Kestrel:AgentPort</c>, 8796 by
/// default) — see <c>Program.cs</c>'s port-gating middleware, which
/// rejects (404) any request arriving on that port whose resolved
/// endpoint doesn't carry this attribute.
///
/// <para>
/// Exists because ASP.NET Core doesn't otherwise separate routing/static
/// files by which Kestrel listener a request arrived on: <c>UseStaticFiles</c>,
/// <c>MapFallbackToFile</c>, and every controller route are registered
/// globally on one pipeline shared by both the browser-facing HTTP port
/// (8795) and the agent-facing mTLS port (8796). Before this existed, a
/// browser (or anything else) connecting directly to 8796 got served the
/// full admin SPA, and — since a session cookie isn't port-scoped — an
/// already-authenticated admin's browser could reach the entire
/// cookie-gated admin API there too, undermining any network-segmentation
/// assumption that "8796 is agent-only, safe to expose more broadly than
/// 8795." Found by a user question, not by an automated scan.
/// </para>
///
/// <para>
/// Deliberately an explicit, per-endpoint allowlist rather than inferring
/// "agent-facing" from something implicit (e.g. "requires the
/// AgentCertificate policy, or is anonymous") — <c>AuthController</c>'s
/// login/logout endpoints are also anonymous, and inferring from policy
/// alone would miss <c>VersionController</c> (anonymous, no certificate
/// policy, but still genuinely needed on 8796 — <c>HeartbeatWorker</c>
/// polls it over the same connection it uses for everything else). An
/// explicit marker, applied right next to the controllers/actions it
/// describes, is what actually stays correct as agent-facing routes are
/// added later.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AllowedOnAgentPortAttribute : Attribute;
