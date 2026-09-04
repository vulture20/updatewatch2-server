<img src="docs/logo.png" alt="UpdateWatch2" width="96" height="96" />

# UpdateWatch2 Server

Central management and distribution component of UpdateWatch2 — a system for centrally distributing, monitoring, and remotely triggering software/OS updates on managed endpoints.

- Runs in a Docker container.
- Persists all state in a SQLite database.
- Authenticates agents via mutual certificates; new agents require manual (or bulk) admin approval before receiving a client certificate.
- Exposes an HTTPS API that agents use to report alive-status, updates found, and reboot-required state, and through which the admin can remote-trigger update installs.
- Login is either the local `admin` account or an Active Directory user in a configured group (both cookie-session, brute-force protection — two independent paths, tried in that order).
- Administration area covers the Active Directory connection, logging, email notifications, and user-customizable language (DE/EN) and theme (light/dark).
- `web/` holds the admin UI (TypeScript + React SPA).

See the project CLAUDE.md for the full architectural briefing, module layout, and configurable-behavior contract, and this repo's own open issues for what's still outstanding.

## Running

The image serves both the API and the built admin UI (`web/`) from one container.

```
docker run -d -p 8080:8080 -p 8443:8443 \
  -e UPDATEWATCH2_SERVER_HOSTNAME=updatewatch2.example.com \
  -v uw2-data:/app/data -v uw2-certs:/app/certs \
  ghcr.io/vulture20/updatewatch2-server:latest
```

The generated `admin` password is printed to the container's log on first start (`docker logs <container>`). See `docker/docker-compose.yml` for a ready-to-edit local setup, and `.env.example` for the environment variables. Mounting `/app/data` (SQLite database + Data Protection keys, so admin sessions survive a restart) and `/app/certs` (the internal CA + server certificate mutual-TLS agent auth generates on first run — see `updatewatch2-server#1`) are both required for anything beyond a throwaway test — losing `/app/certs` invalidates every already-approved agent's certificate on the next restart, the same way losing `/app/data` invalidates every admin session.

Two ports: `8080` is plain HTTP for the admin UI/API, meant to sit behind a TLS-terminating reverse proxy (see the Data-Protection-cookie note above and `.env.example`'s CORS settings). `8443` is agent-only, Kestrel-terminated TLS with mutual-certificate authentication — no reverse proxy in front of it. `UPDATEWATCH2_SERVER_HOSTNAME` sets the SAN on the certificate Kestrel presents there; it must match whatever `ServerAddress` agents are configured to dial, since an agent pins and validates that SAN, not just that the certificate chains to the server's internal CA.

The image has a `HEALTHCHECK` (unauthenticated `GET /api/health`, checked every 30s) — `docker ps` shows `(healthy)`/`(unhealthy)`, and `docker inspect --format='{{json .State.Health}}' <container>` gives the check history. It only confirms the process is up and serving requests, not that the database is reachable, so an orchestrator won't restart-loop the container over a transient SQLite hiccup.

Images are built and published to `ghcr.io/vulture20/updatewatch2-server` by `.github/workflows/docker-publish.yml` on every push to `main` and on `v*.*.*` tags, tagged `latest`, `v<VERSION file contents>`, `sha-<short sha>`, and (for tag pushes) the tag itself. Pull requests build the image without pushing, gated on `dotnet test` and `npm test` both passing first.

Companion repository: `updatewatch2-agent`.
