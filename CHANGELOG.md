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
