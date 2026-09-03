<img src="docs/logo.png" alt="UpdateWatch2" width="96" height="96" />

# UpdateWatch2 Server

Central management and distribution component of UpdateWatch2 — a system for centrally distributing, monitoring, and remotely triggering software/OS updates on managed endpoints.

- Runs in a Docker container.
- Persists all state in a SQLite database.
- Authenticates agents via mutual certificates; new agents require manual (or bulk) admin approval before receiving a client certificate.
- Exposes an HTTPS API that agents use to report alive-status, updates found, and reboot-required state, and through which the admin can remote-trigger update installs.
- Local `admin` account login (cookie session, brute-force protection) is implemented; AD-authenticated login is not yet.
- Administration area covers Active Directory integration, logging, email notifications, and user-customizable language (DE/EN) and theme (light/dark).
- `web/` holds the admin UI (TypeScript + React SPA).

See the project CLAUDE.md for the full architectural briefing, module layout, and configurable-behavior contract, and this repo's own open issues for what's still outstanding.

## Running

The image serves both the API and the built admin UI (`web/`) from one container.

```
docker run -d -p 8080:8080 \
  -v uw2-data:/app/data -v uw2-certs:/app/certs \
  ghcr.io/vulture20/updatewatch2-server:latest
```

The generated `admin` password is printed to the container's log on first start (`docker logs <container>`). See `docker/docker-compose.yml` for a ready-to-edit local setup, and `.env.example` for the environment variables. Mounting `/app/data` (SQLite database + Data Protection keys, so admin sessions survive a restart) is required for anything beyond a throwaway test; `/app/certs` isn't used yet (mutual-TLS agent auth — see `updatewatch2-server#1` — isn't implemented).

The image has a `HEALTHCHECK` (unauthenticated `GET /api/health`, checked every 30s) — `docker ps` shows `(healthy)`/`(unhealthy)`, and `docker inspect --format='{{json .State.Health}}' <container>` gives the check history. It only confirms the process is up and serving requests, not that the database is reachable, so an orchestrator won't restart-loop the container over a transient SQLite hiccup.

Images are built and published to `ghcr.io/vulture20/updatewatch2-server` by `.github/workflows/docker-publish.yml` on every push to `main` and on `v*.*.*` tags, tagged `latest`, `v<VERSION file contents>`, `sha-<short sha>`, and (for tag pushes) the tag itself. Pull requests build the image without pushing, gated on `dotnet test` and `npm test` both passing first.

Companion repository: `updatewatch2-agent`.
