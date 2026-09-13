# Deployment runbook (server)

Step-by-step for standing up a real UpdateWatch2 server instance and
getting a first agent connected to it. This is a procedural companion to
the README — the README's "Installation & configuration" section is the
quick-reference version of the same steps; this document walks through
the reasoning and the decisions you'll actually have to make along the
way, plus what to check at each step.

The agent-side half of this runbook (installing and configuring an
agent against the server you stand up here) is `../../agent/docs/deployment.md`
in the agent repo.

## 1. Decide the externally-reachable hostname

Every agent validates the server's identity by checking the SAN on the
certificate presented on the agent-facing port — not just that it chains
to the pinned CA. That SAN is set from `UPDATEWATCH2_SERVER_HOSTNAME`,
and it **must** exactly match the address you're going to tell agents to
dial. Decide this before your first `docker run` — changing it later
regenerates the server's own leaf (harmless), but every already-deployed
agent's local config also needs updating to match, or its connections
start failing certificate validation.

This is a plain hostname or IP, not a URL — e.g. `updatewatch2.example.com`,
never `https://updatewatch2.example.com:8796`.

## 2. Run the container

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

All three volumes matter and none are optional in practice:

| Volume | Losing it means |
|---|---|
| `/app/data` | A fresh admin password and empty database on next restart. Also where Data Protection keys live — losing it invalidates every logged-in admin session too. |
| `/app/certs` | The internal CA and the server's own agent-facing leaf are regenerated — every already-approved agent's certificate stops being trusted. |
| `/app/agent-updates` | Self-healing (the server re-downloads on the next check), so the least catastrophic of the three, but still avoid it — a stale offer can 404 an agent's download until that self-heal catches it. |

Prefer `docker compose up --build` from `docker/` for local development —
`docker/docker-compose.yml` has the same flags pre-filled with comments on
every optional environment variable.

Port `8795` (admin UI + its API) is plain HTTP, meant to sit behind a
TLS-terminating reverse proxy in production — don't expose it directly to
the internet without one in front. Port `8796` (agent-facing) terminates
TLS itself; nothing needs to sit in front of it, and nothing should — a
reverse proxy in front of mutual-TLS agent traffic would need to pass
client certificates through untouched, which is more fragile than not
having one there at all.

## 3. Log in and change the admin password

```bash
docker logs updatewatch2-server 2>&1 | grep -i "admin"
```

The first-start password is printed once, at container startup — copy it
before it scrolls off, or grep the log again (it's logged every time the
`admin` account is freshly seeded, which only happens once per fresh
`/app/data` volume). Log in at `http://<host>:8795`, then change the
password from the UI immediately.

If you've genuinely lost it and there's no other way in, set
`UPDATEWATCH2_RESET_ADMIN_PASSWORD` to a policy-valid value (≥16 chars,
mixed case + digit + symbol) and restart the container — safe to leave
set afterward, since the same value only ever applies once.

## 4. Decide on Active Directory (optional)

If you want AD-backed login instead of (or alongside) the local `admin`
account: Settings → Active Directory. You'll need a service account DN
with read access (not admin rights) to bind and search with, a base DN
to search under, and — the part most worth getting right the first
time — a login group DN. Only members of that group's own `member`
attribute get login access; everyone else in the directory is
authenticated but not authorized. Test with a real user before relying on
it: a wrong search filter or base DN usually fails closed (nobody can log
in), which is safer than failing open, but still worth confirming
deliberately rather than discovering it during an incident.

## 5. Configure notifications (optional but recommended)

Settings → Notifications: SMTP host/port/credentials, a "from" address,
and a **notification recipient** — a separate field from the "from"
address, since one is this server's own identity as a sender and the
other is where alerts actually go. Nothing below fires without a
recipient configured, though the underlying checks keep running either
way:

- Certificate expiry warnings (CA root and the server's own leaf).
- The updates-per-machine / affected-machines threshold notification.
- Agent offline / back-online notifications.

Use "Send test email" on the same tab before relying on any of the above
— it exercises the exact same SMTP path.

## 6. Set the offline-detection threshold (optional)

Settings → General → "Offline detection": how many minutes since an
agent's last heartbeat before it's flagged offline in the UI and,
depending on the checkboxes on the Notifications tab, emailed about.
Default 15 minutes — lower it for a fleet you expect to notice gaps in
quickly, raise it for one with agents on flaky or metered connections
where a 15-minute silence is unremarkable.

## 7. Connect a first agent

At this point, switch to the agent repo's own
`docs/deployment.md` to install and configure an agent pointed at this
server's hostname and port `8796`. Once it registers, it shows up in the
agent overview list as pending approval — approve it there (individually,
or via bulk-approve if you're onboarding several at once) to issue its
certificate.

**Optional, but worth doing before onboarding agents at scale**: close
the trust-on-first-use (TOFU) window by downloading the CA root ahead of
time (Settings → Certificates → "Download CA root certificate", or
`GET /api/admin/certificate-authority/download`, both session-authenticated)
and handing it to each installer via `/CACERT=` (Windows) or by placing it
at `/etc/updatewatch2/ca.pem` before the first `systemctl start` (Linux) —
see the agent repo's own deployment doc for the exact steps. Without this,
a genuinely fresh agent trusts whatever CA the server hands it on its
very first, unauthenticated contact; a network attacker present at
exactly that moment could intercept it.

## 8. Updating the server itself

```bash
docker pull ghcr.io/vulture20/updatewatch2-server:latest
docker stop updatewatch2-server && docker rm updatewatch2-server
# re-run the same docker run command from step 2
```

Or, with compose: `docker compose up -d --pull always`. Pending EF Core
migrations apply automatically on startup — no manual migration step.

## 9. Health check

`GET /api/version` (anonymous, port 8795) returns the server/protocol/DB
schema versions — useful for confirming a deploy actually took effect,
and for a load balancer's own health probe.

## What to check if something's wrong

| Symptom | Likely cause |
|---|---|
| Every agent connection fails cert validation | `UPDATEWATCH2_SERVER_HOSTNAME` doesn't match what agents are configured to dial. |
| Admin session logs out on every restart | `/app/data` isn't actually persisted (Data Protection keys live there). |
| An already-approved agent stops being trusted after a restart | `/app/certs` isn't actually persisted. |
| `docker logs` shows almost nothing after changing the log level | Should self-correct immediately (server v0.30.2) — if not, confirm you're on a build newer than that. |
| An admin's SMTP fix "didn't take effect" in the UI | Should update the moment the save succeeds, no reload needed (server v0.29.1) — if the banner is still stale, hard-refresh once to rule out a cached client bundle. |
