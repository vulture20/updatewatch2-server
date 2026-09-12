# Changelog

All notable changes to the UpdateWatch2 Server are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and versioning follows [SemVer](https://semver.org/), starting at `0.x.x`
(beta) per the project's CLAUDE.md. This file tracks the **server**
version specifically — one of CLAUDE.md's four independent version
numbers (server, agent, transfer protocol, DB schema), which evolve on
their own schedules; a protocol or schema bump is called out inline
below where a change caused one, but this changelog isn't those
changelogs.

## [0.30.13] - 2026-09-12

### Fixed

- **A tag push to this repo published the Docker image but never created a matching GitHub Release — every server release before this had to be created by hand, unlike the agent repo's own `release.yml`, which already does this on every tag.** Found by the user directly, right after `v0.30.12` was tagged and its image published: "Beim Server fehlt leider v0.30.12 als Release." `docker-publish.yml` gained a new `release` job (needs `build-and-push`, gated on `startsWith(github.ref, 'refs/tags/v')`) using `softprops/action-gh-release@v3` with `generate_release_notes: true` — the same shape the agent's own release job already uses, minus a `files:` list, since this repo has no downloadable release artifact (only the container image `build-and-push` already publishes). Live-verified end to end, not just reasoned about: tagging and pushing this very version (`v0.30.13`) triggered the workflow, and a GitHub Release for it appeared automatically with no manual `gh release create` step needed — the first server release this project has ever produced without one.

## [0.30.12] - 2026-09-12

### Added

- **An admin can now remotely reboot an agent's machine from the admin UI, at the user's explicit request ("Es fehlt noch die Funktion um den Client über die Oberfläche neu starten zu können") — protocol `0.11.0`, DB schema `0.16.0`.** Reboots the whole machine, not just the agent's own service process (an earlier same-day version of this feature only restarted the service — corrected before ever being tagged, once the user clarified they meant a full machine reboot). Deliberately never conflated with an OS-update install (CLAUDE.md's "update installation never triggers a reboot itself... the admin decides when to actually trigger a reboot" rule is exactly what this implements). `AgentDetailPage` gained a "Reboot machine" button (confirm-gated, since the machine is briefly unreachable) and its own status card, mirroring the existing install-trigger/install-status pattern exactly. New `Agent.PendingRebootRequestedAt`/`LastRebootOutcome`/`LastRebootErrorDetail`/`LastRebootCompletedAt` columns; `IAgentService.TriggerRebootAsync`/`AcknowledgeRebootAsync` mirror `Updates.IUpdateService`'s existing `TriggerInstallAsync`/`AcknowledgeInstallAsync` field-for-field, including the same fire-and-forget delivery (the request is surfaced to the agent as an additive `rebootRequested` field on the existing `alive` heartbeat response, picked up on the agent's own next poll, never pushed). `POST /api/agents/{hostname}/reboot` (admin-session-gated, alongside Approve/Reissue/Delete on `AgentsController`) triggers it; `POST /api/agents/{hostname}/reboot-ack` (mTLS-gated, on `AgentProtocolController` rather than `AgentsController` — that controller's class-level `[Authorize]` is the cookie-session scheme, which an agent's client certificate could never also satisfy on the same request) is the agent's acknowledgement. New `RebootOutcome` enum (`[JsonConverter(JsonStringEnumConverter)]`, mirroring `Updates.InstallOutcome`'s own established reasoning for why the default numeric encoding isn't used for a wire-facing enum in this codebase) kept deliberately separate from `InstallOutcome` even though the shape is identical. "Succeeded" only ever means the platform's reboot command was scheduled successfully, not that the machine has actually come back up — see the `BootTimeUtc` bullet below for that.
- **`Agent.BootTimeUtc`, at the user's explicit request, so an admin can actually confirm a triggered reboot took effect** ("noch die Uptime des Clients um kontrollieren zu können, ob der Neustart funktioniert hat") — self-reported by the agent on every `alive` heartbeat (an additive, nullable `bootTimeUtc` field on that request body, the same refresh channel `updatewatch2-agent#6` already established for DnsName/OperatingSystem/IpAddress/AgentVersion), computed agent-side from `Environment.TickCount64` (portable on both Windows and Linux, so no platform-specific agent code was needed). Surfaced on `AgentDetailDto` and shown in `AgentDetailPage`'s Identity card as a relative "X ago" time (reusing the existing `formatRelativeTime` helper the agents-overview list already uses for "last seen") — a reboot having actually happened shows up as this value jumping forward to a recent timestamp on the next heartbeat, independent of `LastRebootOutcome`.
- Real test coverage: `AgentServiceTests` (trigger/acknowledge, including the audit-log-actor and stale-error-detail-clearing cases already established for the sibling install feature) and `ApiEndpointTests` (admin-session-required, not-found, and the mTLS-gate-exists-even-though-`WebApplicationFactory`-can't-exercise-real-TLS case — same standing limitation this file's own `Renew_rejects_a_request_with_no_client_certificate` test already documents). Web: `AgentDetailPage.test.tsx` covers the trigger/confirm/decline/pending/outcome/error-detail cases (mirroring the existing install-trigger test suite) plus the new uptime display, including a fake-timers test confirming a `bootTimeUtc` renders as the expected relative time.

## [0.30.11] - 2026-09-12

### Fixed

- **Agent self-update for v0.15.5 never offered any asset on either platform ("Agent release 0.15.5 has no asset for this platform (WindowsInstaller)" / "(LinuxDeb)") — a real production incident, root-caused against the live `AgentUpdateStates` row and a real GitHub API check, not just reasoned about.** GitHub's `/releases/latest` can report a release's tag before every job of this project's own multi-job release pipeline has actually finished attaching its asset to that release — confirmed live: the affected instance's periodic check ran at `05:59:30 UTC`, when the `v0.15.5` release object already existed but carried zero assets, while the real `.exe`/`.deb`/`.rpm` files didn't finish uploading until `06:30:56`/`06:30:57`, about half an hour later. `CheckForUpdatesAsync` committed `state.LatestVersion = "0.15.5"` anyway, with every asset slot left `null` — and because `AssetsPresentOnDisk` treats a `null` filename as "not expected, nothing to check" (the correct behavior for a release that genuinely never ships one of the three platforms), every subsequent check saw `0.15.5` as both already-known and fully present on disk, permanently reporting `UpToDate` and never retrying, even once the real assets existed on GitHub minutes later. Fixed with a new guard: if downloading a release's assets classifies zero of them into any of the three known slots, `CheckForUpdatesAsync` no longer advances `LatestVersion` at all — it's recorded as a retryable failure instead, so the next periodic or manual check re-fetches the release from scratch rather than treating an assets-still-publishing snapshot as the final word. A production instance already stuck in the old broken state (a `null`-everywhere row pinned to the newest tag) needs one manual intervention to recover on this fixed build — either a manual asset upload for that version via the admin UI, or an admin editing the stuck row so a fresh check no longer looks "already known" — since the fix only prevents the bad state from being written going forward, it doesn't retroactively repair a row already saved before this release.

## [0.30.10] - 2026-09-11

### Added

- **`AgentDetailPage` now shows WHY a remote install failed, at the user's explicit request after a real production incident took raising the agent's log level and live-tailing journalctl just to find "apt-get ... exited with code 100: E: There were unauthenticated packages..." — protocol `0.10.0`, DB schema `0.15.5`.** `POST .../install-ack`'s body gained an additive, nullable `errorDetail` field (`InstallAckRequest.ErrorDetail`) — the agent's own OS-level tool output or a caught exception's message, only ever meaningful alongside a `Failed` outcome. Stored on a new `Agent.LastInstallErrorDetail` column (cleared on a subsequent `Succeeded` ack so a stale reason never lingers next to a since-fixed install), surfaced on `AgentDetailDto`, and shown in the Install status card next to "Fehlgeschlagen"/"Failed" whenever present. An older agent build simply never sends the field, so nothing new shows — same backward-compatible pattern as every prior additive heartbeat/ack change.
- **Root cause of the specific incident that prompted the above, investigated directly rather than assumed: an apt-repository trust/signing problem on the affected host, unrelated to the selective-install feature itself.** `apt-get -y ...` refuses "unauthenticated packages" (an untrusted/unsigned repository) with exit code 100 regardless of whether the install is scoped (`install --only-upgrade -- <selection>`) or unscoped (`dist-upgrade`) — confirmed by re-reading and re-testing `AptUpdateSession.BuildInstallArgs`/`DownloadAndInstallAsync` live against a real package cache in this project's own dev sandbox across several dependency-entangled scenarios (a package pulling in others via automatic dependency resolution), all of which apt resolved and installed successfully; only a package genuinely failing signature verification reproduces the reported error. Not a code bug — the fix is on the affected host (import the missing repository GPG key). The `LastInstallErrorDetail` feature above exists so a report like this is diagnosable straight from the admin UI next time, without needing a live-tailed journalctl session at all.

## [0.30.9] - 2026-09-11

### Added

- **The SQLite database now reclaims disk space on its own, at the user's explicit request ("Wird eventuell ein Aufräumprozess für die Datenbank benötigt? (VACUUM oder ähnlich)").** SQLite never returns freed pages (from audit log retention purges, an agent's update-item churn on every report, deleted agents, etc.) back to the OS on its own — the `.sqlite` file only ever grows. A new `IDatabaseMaintenanceService` switches the database to `auto_vacuum = INCREMENTAL` once, idempotently, at startup (a one-time `VACUUM` is unavoidable to convert an already-non-empty database's file layout the first time; every startup after that is a no-op), and a new `DatabaseVacuumWorker` — this project's fourth server-side `BackgroundService` — reclaims freed pages every 24 hours, same cadence/reasoning as `AuditLogRetentionWorker`. Both operations also explicitly checkpoint the WAL afterward (`PRAGMA wal_checkpoint(TRUNCATE)`) — this database runs in WAL journal mode by default (confirmed by hand, not something this project explicitly configures), so without an explicit checkpoint the reclaimed space would just sit in the `-wal` sidecar file until SQLite's own automatic checkpoint threshold eventually triggers, which for this project's realistically low write volume could take a very long time and would defeat the point of the feature. Real unit test coverage against an actual SQLite file (not mocked) confirms the mode switch, the idempotency, and that the combined on-disk footprint (main file + WAL) genuinely shrinks after deleting a bulk of rows and reclaiming. Live-verified against a real running server, not just `dotnet test`: the one-time conversion logged and ran on first startup (`PRAGMA auto_vacuum` confirmed `2`/INCREMENTAL on the real file afterward, with the seeded admin account still intact and login still working), and a restart confirmed no second conversion runs.

## [0.30.8] - 2026-09-11

### Added

- **An admin can now install only some of an agent's pending updates while sparing others, at the user's explicit request ("Schaffe eine Möglichkeit nur bestimmte Updates zu installieren und manche auszusparen") — protocol `0.9.0`, DB schema `0.15.4`.** `POST /api/agents/{hostname}/install`'s body gained an optional `updateItemIds` field (`Updates.TriggerInstallRequest`) naming the specific `UpdateItem.Id` rows to install; a null/absent body still installs everything, unchanged, so every existing caller keeps working exactly as before. `TriggerInstallAsync` translates the selected ids into their `PackageId`s (the identifier an agent can actually re-match against what it independently finds pending — a KB number on Windows, a bare package name on Linux), stores them as a JSON array in a new `Agent.PendingInstallUpdateIds` column, and hands them back on the next `alive` heartbeat as an additive `installUpdateIds` field, mirroring exactly how `installRequested` already rides that same response. Live-verified end to end against a real running server, including a genuine mTLS handshake: registered/approved/certified a real agent, triggered a selective install for one of two seeded updates, and confirmed both the stored `PendingInstallUpdateIds` and the real `alive` response (over TLS, with the agent's own issued certificate) carried exactly the right value — followed by install-ack clearing both fields and a separate no-body trigger confirming the legacy "install everything" path still works unchanged.

### Fixed

- **The pending-updates list's "Detected at" column always showed today's date, no matter how long an update had actually been pending — reported by the user directly.** `UpdateService.ReportUpdatesAsync` used to unconditionally delete and recreate every `UpdateItem` row on every single agent report, resetting `DetectedAt` every time even for an update nothing had actually changed about. Fixed by merging the newly reported set against what's already known instead: matched by `PackageId` (falling back to `Title` when neither side has one), a still-pending update keeps its original `DetectedAt`; one no longer reported gets removed; a genuinely new one gets a fresh `DetectedAt` as before. This also happens to be exactly what makes `UpdateItem.Id` stable enough across reports for the selective-install feature above to rely on.

## [0.30.7] - 2026-09-11

### Added

- **A site-wide error banner when agent auto-update can't reach the update server or download a release, at the user's explicit request ("Fehlermeldung in Banner, falls keine Verbindung zum Updateserver hergestellt oder kein Update heruntergeladen werden konnte. Das soll nur passieren, wenn die automatischen Updates eingeschaltet sind.").** A new `AgentUpdateErrorBanner` (web), polled every 15s like `CertificateRejectionBanner`, shows `AgentUpdateStatusDto.LastError` on every page once logged in. Deliberately gated on `Enabled`, not just a nonzero `LastError` alone: the periodic check keeps running and keeps recording a failure even while the admin-UI toggle is off, so an ungated banner would show permanent, misleading noise on exactly the deployments (offline, relying only on the manual-upload escape hatch below) where that's expected and not a problem. Confirmed live: seeded a simulated GitHub-unreachable error directly in the DB, then disabled the feature via `PUT /api/admin/settings` and confirmed `GET /api/admin/agent-update-status` still returns that same `lastError` with `enabled: false` — exactly the case the client-side gate exists for.

### Changed

- **The manual-upload escape hatch (added in `0.30.6`) no longer asks the admin to type the release version — it's extracted from the uploaded filenames instead, at the user's explicit request ("Die Version der manuell hochgeladenen Agent-Binaries sollte besser aus dem Dateinamen extrahiert werden. Das ist weniger fehleranfällig.").** A new `AgentUpdateVersionExtractor` pulls the version from a plain `x.y.z` digit run in the filename — one generic pattern covers all three of this project's real release-asset naming conventions rather than three separate exact-format regexes. Uploading multiple files in one request now also requires them to agree on the same extracted version — a real validation the old free-text field could never provide — with a clear 400 (and no files written) on a mismatch or an unextractable filename. `IAgentUpdateService.UploadAssetsAsync`'s own signature is unchanged; the extraction and cross-file validation live in the controller. Live-verified against a real running server: a single-file upload worked with no version field sent at all, a filename with no embedded version and a two-file request with mismatched versions were both rejected with specific messages, and a same-version two-file upload spanning all three real naming conventions (`.exe`/`.deb`/`.rpm`) succeeded.

## [0.30.6] - 2026-09-11

### Added

- **Agent release binaries can now be manually uploaded to the server, at the user's explicit request ("Agent-Binaries sollen auch händisch auf dem Server hinterlegt bzw. hochgeladen werden können, falls der Server bewusst keine Internetverbindung haben soll.") — DB schema `0.15.3`.** A new `POST /api/admin/agent-update-status/upload` (multipart, admin-session gated) lets an admin upload a release's `.exe`/`.deb`/`.rpm` files directly, for a server that deliberately has no internet access — the previous GitHub-only check (`updatewatch2-server#14`) had no offline alternative at all. The uploaded files feed the exact same `AgentUpdateState` row and `AgentUpdates:Path` storage the GitHub download already uses, so an agent is offered a manually uploaded release identically to a GitHub-downloaded one (same `agentUpdateAvailable` heartbeat field, same `/api/agent/updates/{fileName}` download route) — no agent-side change needed at all. Uploading the same version again merges into the existing asset set (only the slots actually provided are replaced, so the `.deb` and `.exe` can be uploaded separately); uploading a genuinely different version replaces the whole known set outright, the same full-reset behavior a new GitHub release already triggers, so an agent is never offered filenames mixed across two versions. A new `AgentUpdateState.ManuallyUploaded` flag (reset to false the moment a real GitHub download succeeds) is shown on the admin UI's existing agent-auto-update card ("Source: GitHub" / "Manually uploaded"). Still requires the feature's own Enabled toggle, same as the GitHub check — uploading while disabled is rejected with 400, not silently discarded. The endpoint needed its own explicit `[RequestSizeLimit]`/`MultipartBodyLengthLimit` (200 MB) — Kestrel's/ASP.NET Core's defaults (30 MB / 128 MB) would otherwise silently reject a normal upload, since this project's own real Windows installer already runs close to 30 MB on its own. Live-verified against a real running server instance, not just `dotnet test`: uploaded a real `.deb`+`.exe` pair over real HTTP, confirmed the on-disk files and their DB-recorded SHA-256 were byte-for-byte correct against an independent `sha256sum`, confirmed the merge-vs-replace behavior for a same-version vs. a new-version upload, and confirmed an invalid version, an unrecognized file extension, and an unauthenticated request are all rejected.

### Fixed

- **`/api/version`'s reported server version had silently drifted since `v0.25.4`, even though the repo-root `VERSION` file and this changelog had long since moved on to `0.30.5` — found while bumping the version for the feature above, not reported by a user.** `AppVersion.Current` (the constant that endpoint actually reads) is, per its own doc comment, meant to be kept in sync with `VERSION` by hand on every release — but nothing enforces that, and it simply hadn't been touched since `v0.25.4`. Confirmed live: a freshly built server genuinely reported `{"server":"0.25.4", ...}` moments before the fix and `{"server":"0.30.6", ...}` after it, from an identical checkout otherwise — meaning the admin UI's Info tab (and anything else reading `/api/version`) had been showing a stale server version for several releases. Fixed by setting `AppVersion.Current` to match `VERSION`. No automated check ties the two together, so this can drift again the same way.

## [0.30.5] - 2026-09-11

### Added

- **A way to reset the local admin account's password via an environment variable, at the user's explicit request ("Schaffe eine Möglichkeit das Admin-Passwort zurückzusetzen (überschreiben durch Umgebungsvariable?)").** Previously a locked-out admin (forgotten password, no AD configured, no other login path) had no recovery option short of resetting the entire `/app/data` volume — which also invalidates every session and wipes the whole database. Setting `UPDATEWATCH2_RESET_ADMIN_PASSWORD` to a `PasswordPolicy`-valid value (≥16 chars, upper/lower/digit/symbol) now overwrites the admin account's password with it on the next startup, unconditionally — unlike the existing change-password flow, it doesn't need the current password. Deliberately safe to leave set indefinitely rather than needing to be unset the moment it's used: a SHA-256 fingerprint of the applied value (`AdminAccount.PasswordResetEnvValueHash`, DB schema `0.15.2`) is stored, and the reset only ever re-applies when the variable's *value* actually changes — so a forgotten, stale env var doesn't silently clobber a password the admin has since changed through the UI on every future restart, and a genuinely new value still resets again on demand. An invalid value is logged as an error and never applied; a valid one is logged as a warning (without echoing the password itself) and recorded in the audit log (`admin.password.reset-via-environment`). Live-verified end to end against a real Docker container: a fresh reset let a login with the new password succeed; restarting with the identical value produced no second reset (same password still required); changing the password via the API and restarting with the now-stale env var still set left the UI-set password in effect, with the old reset-env value rejected.

### Fixed

- **A CI-only test race, found by CI itself, not by any local run: `NotificationsControllerTests` intermittently 401'd on a login using a known-good seeded password.** The new tests above for `ResetPasswordFromEnvironmentIfConfiguredAsync` set the real, process-wide `UPDATEWATCH2_RESET_ADMIN_PASSWORD` environment variable — and unlike this project's existing precedent for that pattern (`UPDATEWATCH2_AUTOUPDATE`, read only on-demand by the specific service under test), this variable is now read unconditionally by `AdminAccountService` on every real `Program.cs` startup, including every `WebApplicationFactory<Program>`-based integration test. With xUnit's default parallel-across-classes execution, a real host build could observe the variable set mid-flight by a concurrently-running `AdminAccountServiceTests` test, silently resetting the admin account another test had just seeded a known password for. A full local `dotnet test` run passed clean every time (timing never happened to overlap); GitHub Actions' CI run caught it. Fixed with `[assembly: CollectionBehavior(DisableTestParallelization = true)]` (new `AssemblyInfo.cs`) — the same class of "a fast local run can't reproduce a real CI-only race" gap this project's own agent repo already documented once for `WorkerTests`.

## [0.30.4] - 2026-09-11

### Added

- **The Info tab now shows every relevant certificate with all available details, at the user's request.** Previously the server's own agent-facing TLS leaf (the certificate Kestrel presents on port 8796) had no admin-facing representation anywhere at all — the Certificates tab only ever showed the CA root's thumbprint/expiry. Now, for the current CA root, the server's own leaf, and (only when present) the previous and pending CA roots from an in-progress rotation, the Info tab shows Subject, Issuer, Serial number, SHA-256 thumbprint, issued date, and expiry date. `GET /api/admin/certificate-authority` (`CertificateAuthorityController`) gained the additive fields to carry this (`current`/`previous`/`pending` `NotBefore`/`Subject`/`Issuer`/`SerialNumber`, plus a new `serverLeaf*` group) — read straight from the live `X509Certificate2` instances rather than added to `ICertificateAuthority.GetRotationStatus`/`CaRotationStatus` itself, so that interface's own core rotation-status shape stays untouched. Live-verified against a real Docker container: the endpoint returns the server's real leaf subject/issuer/serial alongside the CA root's.

## [0.30.3] - 2026-09-11

### Fixed

- **The "Send test email" button always showed an error, even though the email genuinely sent and arrived — reported by the user exactly that way: "the mail test runs into an error, but the actual sending works, the test mail arrives."** `NotificationsController.SendTestEmail` returned `Ok()` on success — HTTP 200 with an empty body — but `web/src/api/client.ts`'s `apiClient` only skips `response.json()` for a `204 No Content`; a 200 with no body made that call throw a JSON parse error, which the UI then displayed as a generic error, well after the real email had already been sent and delivered. Every other genuinely-bodyless success response in this codebase (`AuthController.Logout`/`ChangePassword`) already returns `NoContent()` — this was the one place that didn't. Fixed by returning `NoContent()` instead. Live-verified against a real Docker container and a real SMTP server (MailHog): the endpoint now returns `204 No Content`, and the test email still arrives. New test (`NotificationsControllerTests`, a fake `IEmailNotificationService` standing in for a real send since no mail server is available in CI) asserts the success response is genuinely `204` with an empty body, not just that a send was attempted.

## [0.30.2] - 2026-09-11

### Fixed

- **Changing the log level via the admin UI had no effect on a running container's `docker logs` — reported by the user as "the Docker container's logging is still broken."** This was a known, documented limitation (`IAdminSettingsStore.LogLevel`'s own doc comment: "does NOT hot-reload... only re-reads this value on next process start"), but confirmed live against a real container to be the actual cause of the report: a fresh install defaults to `INFO`; if an admin (or an earlier debugging session) ever changed it, `docker logs` would show almost nothing afterward until the container was restarted, including basic host startup messages, since a lowered level like `WARNING` even suppresses those.
  - `AdminSettingsStore.Apply` now pushes a changed log level straight onto the running `IConfiguration`'s `Logging:LogLevel:Default` key — the same mechanism `Program.cs` already used at pre-DI startup, just re-triggered on every settings load/update instead of only once.
  - That alone wasn't sufficient, and a throwaway harness proved it wasn't before this shipped: mutating a value through `IConfigurationRoot`'s indexer does **not** itself raise that root's reload/change token (only an underlying provider's own reload mechanism, or an explicit `IConfigurationRoot.Reload()` call, does) — and `Microsoft.Extensions.Logging`'s `IOptionsMonitor<LoggerFilterOptions>` only re-evaluates `Logging:LogLevel:*` when that token fires. Without an explicit `Reload()` call added right after the indexer write, `ILogger.IsEnabled(...)` never changed post-startup — this is exactly why `Program.cs`'s own pre-`Build()` indexer write "just worked" without one: nothing had read/cached `LoggerFilterOptions` yet at that point, a fundamentally different situation from changing an already-cached value on a fully running host.
  - Live-verified against a real Docker container end to end, not just reasoned about: logged in via the real API, `PUT /api/admin/settings` with `logLevel: "DEBUG"`, then — with **no restart** — a subsequent request produced `dbug:`-level EF Core log lines in `docker logs` immediately.
  - New `LogLevelMapper` (shared by `Program.cs` and `AdminSettingsStore`, replacing a near-identical local function that used to live only in `Program.cs`).

## [0.30.1] - 2026-09-10

### Added

- **A real test-mail mechanism — found missing by a user report.** `IEmailNotificationService.SendTestEmailAsync` existed since this project's earliest SMTP work, with a doc comment claiming it backed "the Administration test-mail button", but no controller had ever actually injected `IEmailNotificationService` — the endpoint/button it was written for was never built, so an admin had no way to confirm SMTP host/port/credentials/encryption actually work before relying on any of it. New `POST /api/admin/notifications/test-email` (`NotificationsController`) plus a "Send test email" field+button in the Notifications tab close the gap.
- **`AdminSettings.CertificateExpiryNotificationsEnabled`** (Notifications tab, default true, at the user's explicit request for an explicit switch — not just the already-existing implicit "no recipient configured" off-switch). Turns the CA-root/server-certificate expiry emails off entirely; the server leaf's own unconditional self-renewal and both certificates' audit log entries keep happening regardless — this only controls whether anyone gets emailed. `CertificateExpiryWorker` folds it into the same gate a missing recipient already produced.
- DB schema bumped to `0.15.1`.

## [0.30.0] - 2026-09-10

### Added

- **Email notification when the CA root or the server's own TLS certificate
  is approaching expiry — including a distinct notice when the server
  certificate renews itself automatically.** At the user's explicit
  request. New `AdminSettings.NotificationRecipientAddress` (Notifications
  tab) is where these go — separate from `SmtpFromAddress`, which is this
  server's own identity as a *sender*, not a destination; empty means the
  checks still run but nothing gets emailed. New
  `AdminSettings.CertificateExpiryWarningLeadDays` (Certificates tab,
  default 60 — matches the agent's own `CertificateRenewalLeadTimeDays`
  default) controls how far ahead of `NotAfter` either certificate counts
  as "approaching expiry".
  - The **server leaf** self-heals: `ICertificateAuthority.RenewServerLeafIfNearExpiry`
    proactively regenerates it once it's within the lead time — running
    unconditionally, regardless of whether email is even configured,
    since an expired server leaf breaks every agent's mTLS connection
    outright and is worth fixing on its own merits. The email here is a
    courtesy "this happened, no action needed" notice.
  - The **CA root** never renews itself (root rotation stays a deliberate,
    multi-step admin action, per `InternalCertificateAuthority`'s own
    long-standing design) — this is a pure warning: "plan a rotation."
  - New third server-side `BackgroundService`, `CertificateExpiryWorker`
    (after `AgentUpdateCheckWorker`/`AuditLogRetentionWorker`) — checks
    immediately on startup, then every 24 hours. A new
    `CertificateNotificationState` singleton row (same one-row-table
    convention as `AdminSettings`/`AgentUpdateState`) tracks, by
    thumbprint, which certificate generation has already been
    audit-logged/emailed about, so the same warning doesn't repeat every
    single day for up to 60 days straight — a CA-root warning stays
    "handled" until the root actually changes; a server-leaf renewal's
    notification specifically survives a transient SMTP failure and
    retries whole (audit-log + email together) on the next tick, since
    `RenewServerLeafIfNearExpiry` itself only ever fires once per actual
    renewal and wouldn't naturally resurface the event otherwise.
  - New `IEmailNotificationService.SendNotificationAsync(toAddress, subject, body)` —
    the first real automated notification email this project sends
    (previously only a manual test-mail button existed); also the
    primitive the still-unimplemented update-threshold notification
    (CLAUDE.md) is expected to reuse.
  - DB schema bumped to `0.15.0`.

## [0.29.1] - 2026-09-10

### Fixed

- **`SmtpWarningBanner` needed a manual F5 to notice a just-fixed SMTP
  configuration** — reported by the user. It only ever fetched
  `/api/admin/settings` once, on mount; `AdminPage` (a sibling under
  `App.tsx`, not its parent) had no way to tell it a save had just
  changed `smtpConfigured`. Fixed with a same-tab `window` `CustomEvent`
  (`src/adminSettingsEvents.ts`, `announceAdminSettingsSaved`/
  `onAdminSettingsSaved`) — `AdminPage.handleSubmit` announces the
  `smtpConfigured` value from the very settings object the server's own
  `PUT` response just returned (no extra round trip), and the banner
  updates from that immediately, disappearing (or staying, if the save
  left it still unconfigured) the same moment "Save" succeeds. Real test
  coverage in the new `SmtpWarningBanner.test.tsx`, rendering the banner
  and `AdminPage` together the way `App.tsx` actually does, since a test
  that only renders one or the other can't exercise the cross-component
  event at all.

## [0.29.0] - 2026-09-10

### Added

- **A new "Info" settings tab, holding the server/protocol/database schema
  version numbers previously shown unconditionally above the tabs.** At
  the user's request. `Administration` is renamed to `Settings`
  (`Einstellungen`) throughout — matching the source Nocturne mockup's own
  nav label exactly, not just an arbitrary new name.
- The SMTP-not-configured banner now carries a "Configure SMTP" link
  straight to the Notifications tab (`/admin?tab=notifications`,
  `AdminPage` reads `?tab=` on mount), matching a mockup affordance the
  first redesign pass had left out — previously just static text with no
  way to act on it from the banner itself.

### Changed

- **Closer alignment with the source "UpdateWatch2 Redesign" mockup**, at
  the user's request that the admin UI "still deviates massively from the
  template." A line-by-line comparison against the mockup's own markup
  and its Nocturne `styles.css` (re-fetched via `DesignSync`) turned up
  several real gaps the first redesign pass (`v0.28.0`) had missed:
  - **Heading scale was compressed to roughly 3/4 of Nocturne's own
    sizes** (h1 32px vs. the source's 42px, h2 22 vs. 32, down to h6 12
    vs. 13) — every page read quieter/denser than intended. Restored to
    the source's exact 42/32/25/20/16/13 scale; `AgentDetailPage`'s
    "Pending updates" sub-heading gets the same explicit 18px override
    the mockup itself gives it, so it doesn't balloon along with the new
    h2 default.
  - **`.card-title` (the stat-card numbers on the Agents overview) was
    22px against the source's 17px**, and `.card` padding used
    `--space-4` (11.2px) instead of the source's tighter `--space-3`
    (8.4px) — both now match exactly.
  - **Destructive actions (delete agent, delete update filter) used a
    red `.btn-danger` treatment the source design system has no
    equivalent for** — Nocturne is deliberately a mono-accent system
    with "no saturated flood outside the accent" (its own `styles.css`
    comment), and the mockup renders both as plain, unremarkable
    secondary/ghost buttons, relying on the native `confirm()` dialog
    for the "are you sure" friction instead of button color. Matched
    exactly; `--color-danger`/`.btn-danger` stay defined for genuine
    error states (`.login-error` banners), just no longer applied to
    these two buttons.
  - The theme-toggle button used `.btn-ghost` (borderless) instead of
    the source's bordered `.btn-secondary` treatment for an icon button;
    the header had no `border-bottom` separating it from page content
    even though the mockup's `.nav` always has one; the logged-in
    username lacked the source's muted, 13px treatment; the login card's
    internal gap used the `--space-4` token (11.2px) instead of the
    source's literal 16px, and its subtitle used `.card-body` (opacity
    0.8) instead of the source's `.text-muted` (a noticeably fainter
    ~55% mix) — all now matched exactly.
  - A settings tab's "Save"/"Saved" row gets a `border-top` divider
    only when it's nested inside that tab's last card (Active Directory,
    Certificates, Update filters, Audit log) — never when it's its own
    standalone row after the cards (General, Notifications), exactly
    following the source's own inconsistency between tabs rather than
    picking one treatment for all six. New `.tab-save-row-divided`
    modifier class captures this.
  - `OneTimeSecretDialog`'s Close button referenced a `.btn-secondary`
    class that was never actually defined anywhere in `index.css` —
    dead code, functionally harmless (an unclassed button already
    renders identically) but cleaned up.
  - The Agents overview table's "Reboot required" column only wrapped
    the value in a `.tag-neutral` pill for the true case, leaving a bare
    unstyled "—" for the false case — the mockup always wraps both in
    the same pill. Now consistent.
  - Removed an "Add filter" `<h3>` sub-heading in the Update filters tab
    that doesn't exist in the source mockup at all (the field labels
    already make the form's purpose clear without it) — at the h3 size
    the corrected scale above gives it (25px), it read as a second,
    oddly-placed page title.

## [0.28.1] - 2026-09-10

### Fixed

- **The Nocturne redesign (`v0.28.0`) broke the whole Administration
  area — all six settings tabs (including Audit Log) rendered stacked on
  top of each other simultaneously instead of one at a time.** Reported
  by the user as "the admin area isn't formatted right and doesn't work,
  and neither does the logging [tab]." Root cause: `AdminPage` hides an
  inactive tab via the plain HTML `hidden` attribute
  (`<div hidden={tab !== 'general'} className="tab-panel">`), but
  Nocturne's `.tab-panel { display: flex; ... }` rule — added for the
  redesign's vertical-tabs layout — overrides the browser's own
  `[hidden] { display: none }` user-agent rule: per the CSS cascade, an
  author-stylesheet declaration always wins over a user-agent one
  regardless of selector specificity, so setting `display` on the same
  element `hidden` is applied to silently cancels `hidden` out. Every
  panel was therefore visible at once — General's cards immediately
  followed by Notifications', Active Directory's, Certificates',
  Update Filters', and Audit Log's (the user's "logging"), each with
  its own duplicate Save button, an obviously "not properly formatted"
  wall of content, and clicking a tab visibly did nothing since
  everything was already showing. Not caught by the existing test suite
  (all 90 tests, `AdminPage.test.tsx` included, stayed green before and
  after this fix) because Vitest's jsdom environment never loads
  `index.css` at all — Testing Library's `getByRole`/`getByLabelText`
  queries read the `hidden` attribute's accessibility-tree semantics
  directly, which stayed correct throughout, regardless of what the real
  CSS cascade actually rendered. Fixed with one added rule,
  `.tab-panel[hidden] { display: none; }`, restoring the browser default
  for exactly the element it was being overridden on.

## [0.28.0] - 2026-09-10

### Added

- **Admin UI redesign: "Nocturne".** Adopted wholesale from a Claude Design
  canvas the user shared ("UpdateWatch2 Redesign"), replacing the
  Discord-derived Blurple/green/magenta system `DESIGN.md` previously
  described. Dark blue-grey ground by default (`#161826`), a single
  blurple accent (`#9184d9`, identical in both themes — no second brand
  color), Inter throughout for headings and body, an 8px-based radius
  scale, outlined-not-filled buttons, and fading-at-both-ends table/divider
  rules. `web/src/theme/tokens.css` and
  `Resources/Themes/{light,dark}.json` updated to match; `DESIGN.md`
  rewritten to describe the new system as its own source of truth.
  `AgentListItemDto` gained `OperatingSystem`/`LastAliveAt` (mirroring
  fields `AgentDetailDto` already had) so the redesigned overview list can
  show a per-row OS icon, an OS filter, and a "last seen" column without a
  per-agent round trip — no protocol or schema bump, since both already
  existed as `Agent` columns, just not surfaced on the list DTO before.
  See CLAUDE.md's own "Key configurable behaviors to preserve" entry for
  the full breakdown of what changed structurally (stat cards, filters,
  sortable columns, the agent-detail 3-card layout, a real modal dialog
  for certificate reissuance, the settings page's vertical tab list).

## [0.27.0] - 2026-09-10

### Added

- **Admin-configurable audit log retention.** New `AdminSettings.AuditLogRetentionDays`
  (default 90, DB schema bumped to `0.14.0`), editable in the Administration
  UI's Audit Log tab via a fixed dropdown (30/60/90/180/365 days, or
  "Unlimited (never discard)" — `0` is the sentinel for that, validated
  server-side against exactly that set, not just "must be positive").
  `AuditLogService.PurgeOlderThanAsync` permanently deletes every entry
  older than the configured window (a no-op when unlimited) and records
  its own outcome as a new entry (actor `system`, action
  `audit-log.purged`) whenever it actually deletes something. Driven by
  a new `AuditLogRetentionWorker` — this project's second server-side
  `BackgroundService` after `AgentUpdateCheckWorker` — which checks
  immediately on startup and then once every 24 hours, re-reading the
  live setting on every tick so an admin lowering it takes effect on the
  very next pass rather than only for future entries. Worked around the
  same EF-Core-on-SQLite "a `DateTimeOffset` comparison operator can't be
  translated" gap this file already documents elsewhere: `PurgeOlderThanAsync`
  projects down to just `(Id, Timestamp)` pairs, filters for staleness
  client-side, then bulk-deletes by the resulting `Id` list via
  `ExecuteDeleteAsync` — a single real SQL `DELETE`, not a
  load-then-remove-then-`SaveChanges` round trip, and no `DateTimeOffset`
  predicate anywhere in the translated SQL either way.

## [0.26.0] - 2026-09-09

### Added

- New session-authenticated `GET /api/admin/certificate-authority/download`
  (`CertificateAuthorityController`), returning the current CA root's raw
  DER bytes and audit-logging the download as `ca.root.downloaded` (SHA-256
  thumbprint of the exported root as the audit `Details`, same convention
  as the sibling `ca.rotation.*` actions). Lets an admin obtain today's CA
  root through their own already-authenticated session rather than only
  via the existing anonymous, agent-facing `GET /api/agent/ca-certificate`
  — which is untouched and remains exactly what a genuinely
  not-pre-seeded agent falls back to for trust-on-first-use (TOFU) at its
  own first contact. The admin UI's Certificates tab (`AdminPage.tsx`)
  gained a matching "Download CA root certificate" link next to the CA
  rotation status, a plain `<a download>` rather than one of the existing
  mutation buttons since a GET performs no client-side action to await.
  Pairs with agent v0.15.0's new NSIS `/CACERT=<path>` silent-install
  switch: an admin downloads the root here and hands it to a fresh
  agent's installer, so that agent's `RegistrationWorker` never has a
  TOFU window to begin with (its existing `EnsureCaPinnedAsync` early-return
  already skips the fetch once anything is pre-seeded at its fixed local
  trust-store path — no agent-side trust-bootstrap code needed to change).
  No protocol or DB schema bump — this is a purely
  admin-facing addition; the agent-facing wire protocol is untouched.

## [0.25.4] - 2026-09-09

### Fixed

- **A single rejected certificate could be recorded twice, with the
  wrong one winning.** Reported by the user: after a certificate
  reissue, an agent still connecting with its old certificate showed
  "Zertifikat nicht vertrauenswürdig (Kette führt zu keiner
  vertrauten CA-Wurzel)" (`NotTrusted`) as the last rejection reason
  — misleading, since the certificate's chain was never the actual
  problem. Live-verified against a real mTLS handshake, not just
  reasoned about: calling `context.Fail(...)` *inside*
  `OnCertificateValidated` (the correct, specific `UnknownAgent`/
  `AgentNotApproved` classification for a cryptographically valid
  certificate that just doesn't match a known/approved agent) also
  fires `OnAuthenticationFailed` afterward, for that same request —
  an assumption this project's own code comments got wrong when that
  handler was added (`v0.22.0`). Both events then recorded a
  rejection microseconds apart, and `GetRecentByHostnameAsync`'s
  "most recent wins" grouping always surfaced `OnAuthenticationFailed`'s
  generic `NotTrusted` fallback, silently overwriting the correct,
  more specific classification `OnCertificateValidated` had just
  recorded. Fixed with a `HttpContext.Items` marker set by
  `OnCertificateValidated`'s own explicit `Fail` call, which
  `OnAuthenticationFailed` now checks before recording anything — a
  genuine intrinsic chain/validity failure (which never reaches
  `OnCertificateValidated` at all) is unaffected and still classified
  and recorded exactly as before.

## [0.25.3] - 2026-09-09

### Fixed

- **The Docker image publish pipeline was broken by the previous
  release** — `v0.25.2`'s new `<Version>` element reads the repo-root
  `VERSION` file via `$(MSBuildProjectDirectory)/../../VERSION`, which
  resolves correctly from a normal checkout but not inside
  `docker/Dockerfile`'s build stage: that build context only ever
  copies `src/UpdateWatch2.Server/` in, never the repo root, so the
  expression resolved to `/VERSION` with nothing there and `dotnet
  restore` failed outright ("could not be evaluated") — confirmed
  live, not just reasoned about: `docker-publish.yml` failed on `main`
  and both the `v0.25.1`/`v0.25.2` tag pushes, meaning neither tag's
  image was ever actually published. Fixed with `COPY VERSION
  /VERSION` in the Dockerfile's build stage, matching where that
  expression looks from there — the same class of gap
  `Database:Path`/`Certs:Path` already needed explicit
  container-specific values for, rather than trusting a dev-relative
  default. Verified with a real local `docker build`, not just
  `dotnet build`/`dotnet test`: succeeds end to end, and the resulting
  image's `UpdateWatch2.Server.dll` genuinely carries "Copyright (C)
  2026 Thorsten Schröpel"/"0.25.3"/"UpdateWatch2 Server" in its
  assembly metadata.

## [0.25.2] - 2026-09-09

### Fixed

- The compiled server binary's Company/Product/Copyright file-version
  resource fields were blank, the same gap the agent repo's own
  `UpdateWatch2.Agent.csproj` was fixed to close (agent v0.14.1):
  `<Authors>`/`<Company>`/`<Product>`/`<Copyright>` were never set at
  all. Now "Copyright (C) 2026 Thorsten Schröpel", matching
  README.md's own copyright line; `<Version>` also now reads the
  repo-root `VERSION` file, the same source `AppVersion.cs` is bumped
  from by hand, so File version/Product version match the actual
  server version too. Prepared alongside the agent-side fix but not
  committed at the time — landing it now for the same consistency.

## [0.25.1] - 2026-09-09

### Changed

- Requested by the user: a rejected-certificate audit log entry's
  `Actor` no longer falls back to the certificate's own thumbprint
  when no hostname can be resolved from it — a thumbprint means
  nothing to an admin scanning the audit log at a glance, while the
  remote IP address (the new fallback) is at least actionable. Only
  the fallback changed — the hostname still takes priority when
  resolvable, and `Details` still carries the thumbprint unchanged
  either way.

## [0.25.0] - 2026-09-09

### Added

- The audit log is now viewable in the admin UI — a new "Audit Log"
  tab under Administration, paginated (50 entries per page) and
  searchable (a single search box filtering actor/action/details
  server-side). Every admin/security-relevant action already written
  via `IAuditLogService.LogAsync` (agent approve/delete/reissue,
  admin settings changes, CA rotation steps, update-filter CRUD,
  certificate rejections, ...) was previously recorded but had no way
  to actually be seen anywhere — this closes that gap. New
  `GET /api/admin/audit-log` (admin-session gated,
  `page`/`pageSize`/`search` query params, `pageSize` clamped to
  [1, 200] server-side). Ordered by `Id` descending, not `Timestamp`
  — this project's EF Core/SQLite combo can't translate an `OrderBy`
  on a `DateTimeOffset` column at all (see
  `CertificateRejectionService`'s own note on the same gap), and `Id`
  gives the identical newest-first order since every entry is written
  with `LogAsync`'s own default `Timestamp`, while staying a real
  server-side `ORDER BY`/`LIMIT`/`OFFSET` instead of pulling the whole
  table into memory just to page it.

## [0.24.0] - 2026-09-09

### Added

- The certificate-rejection warning banner can now be acknowledged —
  found by a user report that it "stayed forever" with no way to
  confirm/close it, since it previously only cleared once the 24h
  lookback window aged an entry out on its own. A new
  `POST /api/admin/certificate-rejections/acknowledge` (admin-session
  gated, audit-logged as `certificate-rejections.acknowledge`)
  silences the banner for every rejection recorded up to that point —
  shared across every admin session, not a per-session dismiss; a
  genuinely new rejection afterward still shows up immediately.
  Deliberately does not affect the per-agent warning icon/reason
  (`AgentsListPage`/`AgentDetailPage`) — acknowledging the banner means
  "an admin has seen this", not "the underlying agent's certificate
  problem is fixed"; that still only clears once the agent itself
  heartbeats successfully again (see `0.23.1`).

### Changed

- DB schema bumped to `0.13.0` for the new
  `CertificateRejectionAcknowledgements` table (a single-row table,
  same convention as `AdminSettings`/`AgentUpdateState`).

## [0.23.1] - 2026-09-09

### Fixed

- The warning icon/reason from the previous release's certificate-
  rejection flagging didn't clear once the underlying problem was
  actually fixed — it lingered for up to 24 hours after an agent had
  already gone back to authenticating successfully, since only the
  rejection's own age (`ICertificateRejectionService`'s 24h lookback
  window) decided whether to show it, with no way for a later success
  to clear it early. `AgentService` now also compares the rejection's
  timestamp against the agent's own `LastAliveAt` (updated on every
  successful heartbeat) and clears the flag immediately once a
  heartbeat has landed since the rejection happened — found by a user
  report, not by testing.

## [0.23.0] - 2026-09-08

### Added

- Agents affected by a rejected client certificate are now flagged
  directly in the admin UI, not just the general warning banner: a
  warning icon next to the hostname on the overview list, and the
  reason (plus when it happened) on the agent's own detail page,
  "sofern bekannt" — whenever it can be attributed. Attribution works
  from the certificate's own Subject CN (every agent leaf's Subject is
  `CN=<hostname>`, readable even from an expired/untrusted
  certificate) rather than needing a DB match, so it also covers a
  certificate that failed chain/validity-period validation outright,
  not only the already-known-agent case.
  `ICertificateRejectionService.GetRecentByHostnameAsync` resolves the
  most recent rejection per hostname within the same 24h window the
  warning banner uses; `AgentService` now depends on it for both the
  list and detail queries.

## [0.22.0] - 2026-09-08

### Added

- Rejected agent client certificates (invalid or expired) are now
  immediately visible in the admin UI (a red warning banner, polled
  every 15s, shown on every page once logged in) and logged at
  Warning level, in addition to an audit log entry — high-priority/
  security-relevant per this project's own requirements, closing a
  gap where any failed agent mTLS handshake was previously silent:
  neither logged, audited, nor surfaced anywhere. Covers both a
  certificate that fails chain/validity-period validation outright
  (`OnAuthenticationFailed`, classified as `Expired`/`NotYetValid`/
  `NotTrusted` from the certificate's own dates) and a
  cryptographically valid, CA-signed certificate that just doesn't
  match a known/approved agent (`ICertificateValidator`'s existing
  failure path, now also reported — `UnknownAgent`/`AgentNotApproved`).
  Reuses the existing audit log table rather than a new one; the
  admin-facing status (`GET /api/admin/certificate-rejections`) looks
  back 24 hours, the same "no dismiss button, it just ages out"
  approach the existing SMTP warning banner already uses.

### Fixed

- A CI-only flaky test in `AgentUpdateCheckWorkerTests` (found by the
  `v0.21.0` tag's own CI run, not locally): `BackgroundService.StartAsync`
  only schedules `ExecuteAsync`, it doesn't wait for that task to
  actually get CPU time, so a short fixed `Task.Delay` before asserting
  could elapse before the loop's first iteration had run at all under
  contention on a loaded runner. Fixed by polling for the actual
  condition instead of sleeping a fixed duration, across all four tests
  in that class. No production code changed.

### Note

- Building this surfaced three separate, previously-undiscovered EF
  Core 10.0.11-on-SQLite query-translation gaps — no earlier query in
  this codebase filtered or ordered by a `DateTimeOffset` column, so
  none of this had shown up before: a plain `string.StartsWith` in a
  LINQ predicate doesn't translate (worked around with
  `EF.Functions.Like`); no `DateTimeOffset` comparison operator
  (`>=`/`<`/...) translates either; and neither does an `OrderBy` on a
  `DateTimeOffset` column. All three were found by actually running the
  query, not by reasoning about it. Worked around by pushing only the
  translatable prefix filter to SQL and doing the recency-window
  filter/ordering client-side on the already-narrowed result set — a
  pattern worth reusing (or revisiting once the provider improves) if
  a future query needs to filter/order by a timestamp column.

## [0.21.0] - 2026-09-08

### Added

- The agent detail view now shows which internal CA root
  (`Agent.IssuingRootThumbprint`) actually signed that agent's current
  client certificate, alongside the existing SHA-256/SHA-1 leaf
  thumbprints — an admin can compare it against Administration →
  Certificates' current/previous root thumbprints to see whether a
  specific agent has renewed past a CA root rotation yet, rather than
  only the fleet-wide aggregate count already shown there. Shows "—"
  for a certificate issued before this was tracked, or no certificate
  at all.

## [0.20.0] - 2026-09-08

### Added

- Global update filters: an admin can maintain a named list of regular-
  expression filters (Administration → Update filters —
  `GET`/`POST /api/admin/update-filters`, `PUT`/`DELETE .../{id}`), each
  matched case-insensitively against an update's title. Any update
  matching any active filter is excluded from the pending-updates
  count and list everywhere they're shown (`AgentsListPage`,
  `AgentDetailPage`) — evaluated live against the current filter list
  on every read, not cached at report time, so adding, editing, or
  deleting a filter changes what's displayed immediately, with no new
  agent report or restart needed. Ships with one default filter,
  "Security Intelligence-Update für Microsoft Defender Antivirus",
  seeded once at startup if the filter table is completely empty and
  never re-seeded afterward. The exclusion logic (`UpdateFilterMatcher`)
  is deliberately the one shared decision point both the pending-
  updates display and, later, the threshold-crossing email
  notification (not yet implemented) are meant to call into.

### Changed

- DB schema bumped to `0.12.0` for the new `UpdateFilters` table.

## [0.19.0] - 2026-09-08

### Changed

- Default ports renumbered from 8080/8443 to 8795/8796 project-wide
  (`Kestrel:HttpPort`/`Kestrel:AgentPort` defaults, the Dockerfile's
  `EXPOSE`/healthcheck, `docker-compose.yml`, both `.env.example`
  files, README examples) — 8080/8443 are common enough to collide
  with something else already running on a host; both remain fully
  overridable. An already-deployed instance relying on the old
  defaults needs its external port mapping updated on the next
  redeploy.

### Fixed

- CI: `Version_returns_all_four_version_numbers` asserted the server
  version against a hardcoded literal, so every routine version bump
  broke the test even though nothing about the bump itself needed the
  test file touched. Now asserts against `AppVersion.Current`/
  `ProtocolVersion.Current`/`SchemaVersion.Current` directly.

## [0.18.2] - 2026-09-08

### Added

- `AgentsListPage`/`AgentDetailPage` now auto-refresh every 5 seconds
  (`setInterval`, cleared on unmount) instead of only re-fetching once
  on a button click — watching an agent finish onboarding (approval
  settling, a certificate arriving, updates list changing) no longer
  requires repeatedly reloading the browser by hand. A background poll
  failure deliberately does not flip the page into its hard error/
  not-found state; only the very first load failing does that, so a
  transient network hiccup doesn't blank out an already-rendered
  list/agent.

## [0.18.1] - 2026-09-08

### Added

- A "Check now" button next to the agent-auto-update status display
  lets an admin force an immediate GitHub check
  (`POST /api/admin/agent-update-status/check`) rather than waiting for
  `AgentUpdateCheckWorker`'s own interval — safe to call anytime, since
  the underlying check is already a no-op when nothing changed on
  GitHub. Audit-logged separately (`agent-update.manual-check`) from
  the periodic worker's own entries.

### Changed

- README rewritten with badges, a feature overview grouped by area, an
  explicit project-status section calling out which pieces aren't yet
  live-verified, and a full German translation (`README.de.md`).

## [0.18.0] - 2026-09-07

### Added

- Agents can now be permanently deleted
  (`DELETE /api/agents/{hostname}`, plus a "Delete agent" button on
  `AgentDetailPage`), for decommissioned machines or mistaken/test
  registrations. Takes effect immediately, even against a still-
  cryptographically-valid certificate — `CertificateValidator`
  resolves a presented client certificate to an agent via a DB lookup,
  so a deleted row's certificate simply stops authenticating on its
  next request, no separate revocation-list mechanism needed. A
  re-registration under the same hostname starts over as a brand-new,
  unapproved agent. `UpdateItem` rows cascade-delete via the existing
  FK.

## [0.17.0] - 2026-09-07

### Added

- CA root rotation now tracks which root actually signed each agent's
  leaf (`Agent.IssuingRootThumbprint`, captured at issuance) and
  compares it against the CA's current root on every heartbeat,
  surfacing a new additive `certificateRotationPending` field on the
  `alive` response when they differ — closing the gap where rotating
  the CA never rotated an already-onboarded agent's own leaf, only the
  server's. The admin UI's Certificates tab now shows a real
  `stillOnPreviousRootCount`/`stillOnPreviousRootHostnames` (plus a
  separate `unknownRootAgentCount` "can't verify" bucket for
  certificates issued before this tracking existed) instead of only
  generic warning text before Retire Previous Root, and the confirm
  dialog is interpolated with the live count.

### Changed

- Protocol version bumped to `0.8.0` for the new
  `certificateRotationPending` field. DB schema bumped to `0.11.0` for
  the new nullable `Agent.IssuingRootThumbprint` column (left `null`
  for a certificate issued before this shipped, never backfilled by
  guessing).

## [0.16.0] - 2026-09-06

### Fixed

- `AgentUpdateService.CheckForUpdatesAsync` now also verifies its
  recorded release assets are still present in `AgentUpdates:Path` on
  every check, not only when GitHub reports a version change —
  closing the gap where losing that storage directory without an
  intervening GitHub release left a stale `AgentUpdateState` row
  404ing every agent's download indefinitely, with nothing to notice
  or self-heal it. A missing asset now triggers a re-download
  (outcome `Redownloaded`, audit-logged as
  `agent-update.assets-redownloaded`, distinct from a genuine new
  release's `agent-update.detected`).

## [0.15.0] - 2026-09-06

### Added

- The agent-update GitHub check interval is now admin-configurable
  (`AgentAutoUpdateCheckIntervalHours`, default 6 — matching the prior
  hardcoded value), live-reloaded on every `AgentUpdateCheckWorker`
  loop iteration rather than only at startup.

## [0.14.0] - 2026-09-06

### Added

- `AgentUpdates/AgentUpdateCheckWorker` — this project's first
  server-side `BackgroundService` — checks GitHub's Releases API every
  6 hours and downloads a newer release's `.exe`/`.deb`/`.rpm` assets
  into a new `AgentUpdates:Path`-configured storage directory,
  recording each asset's filename/SHA-256/size. `alive` now surfaces
  an `agentUpdateAvailable` offer when it differs from the requesting
  agent's own reported version (`updatewatch2-server#14`); no agent
  build acts on it yet (`updatewatch2-agent#14`, tracked separately).
  Agents fetch the actual bytes from this server
  (`GET /api/agent/updates/{fileName}`, mTLS-gated) and never from
  GitHub directly, a design decision pinned before implementation
  started. Admin-configurable on/off toggle (`AgentAutoUpdateEnabled`,
  default on) and an optional GitHub personal access token
  (`GitHubToken`, raises the anonymous rate limit), plus the
  env-var-only master kill switch `UPDATEWATCH2_AUTOUPDATE=false`.

### Changed

- Protocol version bumped to `0.7.0`. DB schema bumped to `0.9.0` for
  the new `AgentUpdateState` table.

### Fixed

- None of the `WebApplicationFactory<Program>`-based integration test
  classes ever had a reason to stop a real `BackgroundService` before
  this feature added the first one — a routine `dotnet test` was
  silently hitting the live GitHub API and writing real multi-megabyte
  release assets to the repo's working directory. Fixed with a shared
  `WithoutBackgroundWorkers()` test extension, applied across all
  affected test classes.

## [0.13.0] - 2026-09-06

### Changed

- The agent certificate detail view now shows both the SHA-256 and
  SHA-1 thumbprints side by side, replacing the previous labeled-
  field-plus-hint approach — an admin can compare against whichever
  value their local tool (Certificate Manager, PowerShell, `certutil`,
  plain `openssl x509 -fingerprint`) happens to print, with no hint
  text needed. `Agent.ClientCertificateThumbprintSha1` is
  display-only; every internal lookup/comparison still uses SHA-256
  exclusively.
- DB schema bumped to `0.8.0` for the new
  `Agent.ClientCertificateThumbprintSha1` column.

### Fixed

- CA-rotation expiry dates now format via the app's own active UI
  language (`i18n.language`) instead of the runtime's default locale
  — fixes a CI failure where `ubuntu-latest`'s `en` locale formatted
  dates differently than the locale the feature was authored/tested
  against, which had silently stopped the Docker-publish CI job's
  image build from running since CA rotation shipped.

## [0.12.0] - 2026-09-05

### Added

- Internal CA root rotation (`updatewatch2-server#6`), the one gap
  deliberately left open when mutual-TLS agent authentication first
  shipped. Three explicit admin actions, not a single "rotate now"
  button:
  - `POST /api/admin/certificate-authority/prepare` generates a new root
    without using it for anything yet.
  - `POST .../activate` promotes it to current, demotes the previous
    root to "still trusted but no longer used to sign anything new" (so
    an already-issued, not-yet-renewed agent certificate keeps
    validating), and re-issues the server's own agent-facing TLS leaf
    under the new root immediately — no restart, via Kestrel's
    `ServerCertificateSelector` reading the CA's current leaf on every
    new connection rather than a value captured once at startup.
  - `POST .../retire-previous` drops the superseded root once an admin
    is satisfied every agent has renewed past it.
  - `GET .../` (status) reports current/previous/pending thumbprints and
    expiries.
- A new agent-facing `GET /api/agent/ca-certificates` (plural) endpoint
  publishes every root the CA currently knows about — current, previous,
  and a prepared-but-not-yet-active pending one — as a PKCS7 bundle, so
  an already-onboarded agent can pre-trust an upcoming root on its own
  heartbeat cadence, ahead of an admin activating it. The original
  singular `GET /api/agent/ca-certificate` (current root only, raw DER)
  is unchanged, for bootstrap trust-on-first-use.
- A minimal admin UI panel (Administration → Certificates tab) for the
  three actions above plus the status display, with confirmation prompts
  before activating or retiring since both are one-way and can affect
  live agent connectivity if done before agents have caught up.

### Fixed

- `CertificateRequest.Create`-signed leaves carried no
  `AuthorityKeyIdentifier` extension binding them to the specific
  issuing root's key — harmless with only ever one root in existence,
  but the moment a second one could exist (rotation), two roots sharing
  a look-alike Subject let `X509Chain.Build()` pick the wrong candidate
  to verify a leaf's signature against, failing with "certificate
  signature failure". Reproduced live, not just reasoned about — fixed
  by adding `X509AuthorityKeyIdentifierExtension` to every issued leaf,
  plus giving each generated root a unique Subject (a timestamp suffix)
  as a belt-and-suspenders second fix.

### Changed

- Protocol version bumped to `0.6.0` for the new `ca-certificates`
  endpoint.

## [0.11.0] - 2026-09-05

### Added

- The `alive` heartbeat now accepts an optional body carrying an agent's
  current `DnsName`/`OperatingSystem`/`IpAddress`/`AgentVersion`
  (`updatewatch2-agent#6`) and refreshes the stored `Agent` row from it.
  Closes the gap left by registration alone: once an agent is
  certified, `AgentRegistrationService.RegisterAsync` never runs again
  for it, so none of this self-reported metadata was ever updated after
  onboarding — DHCP lease renewals, OS upgrades, hostname changes, and
  agent version upgrades all went unreflected in the admin overview.
  Backward compatible: the body is optional, so an agent build older
  than this still heartbeats successfully with no metadata refresh.

### Changed

- Protocol version bumped to `0.5.0` — the `alive` request body's shape
  changed (additive; a pre-existing agent's bodyless heartbeat is
  unaffected).

## [0.10.0] - 2026-09-05

### Added

- Remote-triggered installs are now actually delivered to the agent
  (`updatewatch2-server#10`): a pending install request is surfaced to
  the agent via its existing `alive` heartbeat response and acknowledged
  through a new `POST /api/agents/{hostname}/install-ack` endpoint.
- The agent overview's trigger-install button now reflects a pending
  state, re-fetches after being clicked, and shows the last install
  outcome — previously pure fire-and-forget.

### Changed

- Protocol version bumped to `0.4.0`: the `alive` endpoint's response
  changed from a bare `204 No Content` to `200` with a JSON body.

## [0.9.0] - 2026-09-04

### Added

- Agent client certificate validity period is now admin-configurable.
- Applied the Discord-derived design system from `DESIGN.md` to the
  admin UI.

### Changed

- Licensed the project under AGPL-3.0-or-later.

### Fixed

- Corrected the copyright holder name in the README.

## [0.8.0] - 2026-09-04

### Added

- Proactive agent client certificate renewal before expiry, and
  admin-mediated certificate re-issuance for a lost or wiped agent
  certificate (including a one-time-token display in the admin UI).

## [0.7.0] - 2026-09-04

### Added

- `UPDATEWATCH2_DEMOMODE`-gated dummy-data seeder, so an otherwise-empty
  instance is demonstrable.

### Fixed

- The server no longer crashes on startup after upgrading a database
  that predates Active Directory login support.

## [0.6.0] - 2026-09-04

### Added

- Certificate-based mutual TLS agent registration and `alive` endpoints,
  backed by a new internal certificate authority (self-signed root, with
  per-agent leaf issuance on approval) — the security backbone for all
  agent-server communication (`updatewatch2-server#1`).
- A dedicated agent-facing TLS port, exposed and documented in the
  Docker image/docs.

### Changed

- Protocol version bumped to `0.2.0`.

## [0.5.1] - 2026-09-04

### Fixed

- Active Directory login accepted any username with an empty password —
  an authentication bypass (RFC 4513's unauthenticated-bind behavior).
  Empty passwords are now rejected before the directory bind is even
  attempted.

## [0.5.0] - 2026-09-04

### Added

- Active Directory login: an LDAP bind against a configurable directory,
  gated on membership in one configured group.

## [0.4.1] - 2026-09-04

### Added

- A `HEALTHCHECK` to the Docker image.

### Fixed

- Login over plain HTTP silently bounced back to the login page — the
  auth cookie's `Secure` flag now tracks the request scheme
  (`SameAsRequest`) rather than being forced on unconditionally.

## [0.4.0] - 2026-09-03

### Added

- The server now builds and publishes a Docker image (API + built web
  UI, one container) to GHCR via CI.

## [0.3.0] - 2026-09-03

### Added

- Admin settings persistence (`PUT /api/admin/settings`, live-applied).
- Generated branding assets.

## [0.2.0] - 2026-09-03

### Added

- The login page is wired up to a real authentication endpoint.

## [0.1.0] - 2026-09-03

### Added

- Initial scaffold: the ASP.NET Core server (database, agents, updates,
  auth, notifications) and the admin web UI (TypeScript + React SPA).
