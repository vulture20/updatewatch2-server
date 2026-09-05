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
