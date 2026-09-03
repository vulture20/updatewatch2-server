# UpdateWatch2 Server

Central management and distribution component of UpdateWatch2 — a system for centrally distributing, monitoring, and remotely triggering software/OS updates on managed endpoints.

- Runs in a Docker container.
- Persists all state in a SQLite database.
- Authenticates agents via mutual certificates; new agents require manual (or bulk) admin approval before receiving a client certificate.
- Exposes an HTTPS API that agents use to report alive-status, updates found, and reboot-required state, and through which the admin can remote-trigger update installs.
- Administration area covers Active Directory integration, logging, email notifications, and user-customizable language (DE/EN) and theme (light/dark).

This repository is in the pre-implementation / planning stage — see the project CLAUDE.md for the full architectural briefing, module layout, and configurable-behavior contract this repo is expected to implement.

Companion repository: `updatewatch2-agent`.
