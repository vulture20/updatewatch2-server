<img src="docs/logo.png" alt="UpdateWatch2" width="96" height="96" />

# UpdateWatch2 Server

**Author:** Thorsten Schröpel · [🇩🇪 Deutsche Version](README.de.md)

[![Docker Image](https://img.shields.io/badge/ghcr.io-vulture20%2Fupdatewatch2--server-2496ED?logo=docker&logoColor=white)](https://github.com/vulture20/updatewatch2-server/pkgs/container/updatewatch2-server)
[![Docker Pulls](https://img.shields.io/badge/dynamic/json?url=https%3A%2F%2Fghcr-badge.elias.eu.org%2Fapi%2Fvulture20%2Fupdatewatch2-server%2Fupdatewatch2-server&query=downloadCount&label=Docker%20Pulls&color=2496ED&logo=docker&logoColor=white)](https://github.com/vulture20/updatewatch2-server/pkgs/container/updatewatch2-server)
[![Docker Image Build](https://github.com/vulture20/updatewatch2-server/actions/workflows/docker-publish.yml/badge.svg)](https://github.com/vulture20/updatewatch2-server/actions/workflows/docker-publish.yml)
[![Status](https://img.shields.io/badge/status-beta-orange)](#-project-status)
[![License: AGPL v3](https://img.shields.io/badge/license-AGPL--3.0-blue.svg)](LICENSE)

UpdateWatch2 Server is the self-hosted, single-container management hub for **UpdateWatch2** — a system for centrally distributing, monitoring, and remotely triggering software/OS updates on Windows and Linux endpoints. Agents register, get approved once, and from then on report their update status over mutual TLS; the admin decides who's approved, when to install, and when to reboot.

> ⚠️ **Beta.** UpdateWatch2 is under active development. The certificate-based security backbone, agent onboarding, and the admin UI are implemented and covered by an automated test suite, but several pieces (real Windows Update installation, the RPM/dnf update path, the Windows installer's install/uninstall behavior) have not yet been verified against a real target host. See [Project status](#-project-status) below before relying on this in production.

Companion repository: [updatewatch2-agent](https://github.com/vulture20/updatewatch2-agent) — the Windows/Linux service this server manages.

## ✨ Features

### 🔐 Mutual-TLS security backbone
- Self-signed internal certificate authority; every request is authenticated with both a server and a client certificate.
- A new agent self-registers but stays unconfirmed until an admin approves it — individually or in bulk.
- Certificates renew themselves proactively before expiry, and an admin can re-issue one on demand (lost/wiped agent) or permanently delete an agent, revoking its access immediately.
- Internal CA root rotation with a transition window: an agent whose certificate hasn't caught up yet keeps working, the server tracks and shows exactly which agents still need to renew before you retire the old root, and an agent that falls behind renews eagerly the moment it notices instead of waiting out its normal schedule.

### 🖥️ Fleet management
- Agents identified by hostname; overview list plus a detail view per agent (OS, IP, version, last-alive, certificate status, pending updates).
- Approve one or many agents at once; remotely trigger an install; permanently delete a decommissioned or mistaken registration.
- Update installation never triggers a reboot itself — "reboot required" is always reported and decided on separately.

### 📦 Update distribution & agent auto-update
- Real Windows Update API (WUApiLib) and Linux `apt`/`dnf` integration for actual update detection and installation, not a placeholder.
- The server itself checks GitHub for new agent releases, downloads them once, and re-serves them to agents — agents never need their own internet access, and every download is SHA-256-verified before an agent applies it.

### 🔑 Login & access
- Local `admin` account (random strong password on first start, changeable afterward) and optional Active Directory login (LDAP bind, gated on group membership) — both cookie-session based, with configurable brute-force protection and a trusted-IP exemption.

### 📧 Notifications & administration
- Email alerts on an OR-threshold (too many updates on one machine, or too many machines affected), with a test-mail function and a live reachability warning.
- Bilingual admin UI (German/English) with light and dark themes, fully responsive down to mobile.
- Nearly every setting is admin-configurable and live-applied — no restart needed.

### 🎭 Demo mode
- `UPDATEWATCH2_DEMOMODE=true` seeds a handful of realistic dummy agents and updates on an otherwise-empty instance — idempotent, env-var only, never for production.

## 🚧 Project status

UpdateWatch2 was built with **vibe coding**: implemented and iterated on with [Claude Code](https://claude.com/claude-code) (Anthropic) in conversation, rather than hand-written line by line, driven by a human-authored architecture brief. Code has been built, tested, and repeatedly run live at each step — not just compiled — and the mutual-TLS security backbone (registration, approval, renewal, re-issuance, root rotation), the admin UI, and the agent-server protocol are implemented end to end and covered by an automated test suite (server: xUnit; UI: Vitest). A few pieces are explicitly **not yet live-verified against a real target host**, and are called out as such in code comments: the Windows Update API (WUApiLib COM) integration, the Linux `dnf`/`yum` update path (only `apt` was verified against a real package cache), and the NSIS Windows installer's actual install/uninstall run through a package manager. Treat this as a well-researched, actively-tested implementation to build on — not yet battle-tested production software. Keep backups of the `/app/data` and `/app/certs` volumes.

## 🐳 Installation & configuration (Docker)

UpdateWatch2 Server ships as a single, self-contained Docker image — the API and the built admin UI in one container, no separate build step or database container required:

```bash
docker run -d \
  --name updatewatch2-server \
  -p 8795:8795 \
  -p 8796:8796 \
  -e UPDATEWATCH2_SERVER_HOSTNAME=updatewatch2.example.com \
  -v uw2-data:/app/data \
  -v uw2-certs:/app/certs \
  -v uw2-agent-updates:/app/agent-updates \
  --restart unless-stopped \
  ghcr.io/vulture20/updatewatch2-server:latest
```

Then open **http://localhost:8795** and log in as `admin` — the randomly generated first-start password is printed to the container's log (`docker logs updatewatch2-server`); change it from the UI afterward.

**Ports:** `8795` is plain HTTP for the admin UI and its API, meant to sit behind a TLS-terminating reverse proxy. `8796` is agent-only — Kestrel terminates TLS directly there with mutual-certificate authentication, no reverse proxy in front. `UPDATEWATCH2_SERVER_HOSTNAME` becomes the SAN on the certificate presented on `8796` and **must match** the `ServerAddress` agents are configured to dial, or every agent connection fails certificate validation.

**Volumes — always mount these, or you lose everything on the next restart:**
- `/app/data` — the SQLite database and the Data Protection keys that sign admin session cookies. Losing it means an empty database and every admin logged out.
- `/app/certs` — the internal CA plus the server's own TLS leaf. Losing it invalidates every already-approved agent's certificate.
- `/app/agent-updates` — cached downloads of the newest agent release, when agent auto-update is enabled. Losing it just triggers a one-time re-download, nothing destructive.

**Image tags:** `:latest` tracks the newest push to `main`; `:v<version>` (e.g. `:v0.18.0`, matching this repo's own [`VERSION`](VERSION) file) pins a specific release; `:sha-<short-sha>` pins an exact commit. Images are built and published by [`docker-publish.yml`](.github/workflows/docker-publish.yml) on every push to `main` and on `v*.*.*` tags, gated on `dotnet test` and `npm test` both passing first — a pull request builds the image without pushing it.

### Key environment variables

| Variable | Required | Default | What it does |
|---|---|---|---|
| `UPDATEWATCH2_SERVER_HOSTNAME` | recommended | container's own hostname | The externally-reachable hostname this server is addressed by — see Ports above. Almost never correct left at its default for a real deployment. |
| `UPDATEWATCH2_LOGLEVEL` | | `INFO` | `DEBUG`/`INFO`/`WARNING`/`ERROR` — changeable later from the admin UI without a restart. |
| `UPDATEWATCH2_TRUSTEDIP` | | — | IP or CIDR range exempt from the login brute-force lockout. |
| `UPDATEWATCH2_AUTOUPDATE` | | (admin-UI toggle, on by default) | Set to `false` to force-disable checking GitHub for new agent releases entirely, overriding the admin-UI toggle. |
| `UPDATEWATCH2_DEMOMODE` | | — | Set to `true` to seed dummy agents/updates for a demo. Never set this in production. |

See [`.env.example`](.env.example) for a ready-to-copy file, and [`docker/docker-compose.yml`](docker/docker-compose.yml) for a ready-made local setup (`cd docker && docker compose up --build`).

### Updating

Pull the new image and recreate the container (same `docker run` flags, or `docker compose up -d --pull always`) — pending EF Core database migrations apply automatically on start.

### Health check

The image has a `HEALTHCHECK` (unauthenticated `GET /api/health`, every 30s) — `docker ps` shows `(healthy)`/`(unhealthy)`, and `docker inspect --format='{{json .State.Health}}' <container>` gives the check history. It only confirms the process is up and serving requests, not that the database is reachable, so a transient SQLite hiccup won't trigger a restart loop.

## 🧱 Tech stack

- **Backend:** ASP.NET Core (.NET 10), EF Core + SQLite, Swashbuckle for the OpenAPI/Swagger UI. `System.DirectoryServices.Protocols` for cross-platform LDAP (Active Directory login) — the server doesn't need to run on Windows to talk to a directory.
- **Frontend:** React + TypeScript SPA (`web/`), built with Vite, `react-i18next` for the bundled DE/EN UI.
- **Deployment:** one Docker image (`docker/Dockerfile`) builds `web/` and the .NET API into a single `mcr.microsoft.com/dotnet/aspnet` runtime image.

## 📁 Repository layout

```
src/UpdateWatch2.Server/   The API project — Agents/, Certificates/, Auth/, Admin/, AgentUpdates/, Api/Controllers/, Db/
tests/                     xUnit unit + WebApplicationFactory-based integration tests
web/                       The admin UI — a separate npm package, not part of the .NET build
docker/                    Dockerfile, docker-compose.yml
config/, docs/             Deployment scaffolding, this README's logo
certs/, data/, logs/, agent-updates/   Runtime-only, gitignored — never commit these
```

## 🛠️ Local development

Requires the .NET 10 SDK and Node 22.

```bash
# API — http://localhost:8795 by default
dotnet build
dotnet test
dotnet run --project src/UpdateWatch2.Server   # SQLite db created/migrated at server/data/updatewatch2.sqlite

# Admin UI — http://localhost:5173
cd web
npm install
cp .env.example .env.local   # point VITE_API_BASE_URL at a running server
npm run dev
```

Frontend build / type-check / tests:

```bash
cd web
npm run build   # tsc -b type-check, then production build to dist/
npm test        # vitest run
```

Building the Docker image locally instead of pulling from `ghcr.io`: `docker build -f docker/Dockerfile -t updatewatch2-server .` (build context must be the repo root, not `docker/`).

## 📜 Changelog

See [`CHANGELOG.md`](CHANGELOG.md) for a version-by-version history of notable changes.

## ⚖️ License

Copyright (C) 2026 Thorsten Schröpel.

UpdateWatch2 Server is free software: you can redistribute it and/or modify it under the terms of the [GNU Affero General Public License](LICENSE) as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version. See [LICENSE](LICENSE), or <https://www.gnu.org/licenses/agpl-3.0.html> for the full text.

In plain terms: you're free to run, modify, and self-host UpdateWatch2. The one obligation AGPL adds on top of a regular GPL license is that if you run a **modified** version and let other users interact with it over a network, you must also offer those users access to your modified source code — not just people you hand a copy of the software to directly. Running an unmodified copy for yourself carries no extra obligation beyond the standard copyleft terms.
