# UpdateWatch2 Web

Admin UI for UpdateWatch2 — a TypeScript + React SPA (Vite), per the tech-stack recommendation in the project's CLAUDE.md. Lives inside the server repo rather than a separate one; see `updatewatch2-server#5` for that placement decision.

## Development

```
npm install
cp .env.example .env.local   # point VITE_API_BASE_URL at a running server
npm run dev
```

```
npm run build   # type-check + production build to dist/
npm test        # vitest
```

## What's here

- `src/api/` — typed client for the server's HTTP API (`client.ts` fetch wrapper, `endpoints.ts`, `types.ts` mirroring the server's C# DTOs by hand)
- `src/auth/` — `AuthContext`/`AuthProvider` (session state via `/api/auth/me`, `login`/`logout`), `RequireAuth` route guard
- `src/i18n/` — DE/EN translations (react-i18next), browser-language default with a `localStorage` override
- `src/theme/` — light/dark theme via CSS custom properties (`tokens.css`, kept in sync by hand with the server's `Resources/Themes/*.json`), system-preference default with a `localStorage` override
- `src/pages/` — `LoginPage` (wired to `/api/auth/login`), `AgentsListPage` (with bulk approve), `AgentDetailPage`, `AdminPage` (a real editable form, wired to `PUT /api/admin/settings`)
- `src/components/` — shared bits: `ThemeToggle`, `LanguageSwitcher`, `SmtpWarningBanner` (shown in the authenticated app shell, not on the login form — the mail-server warning is meant for logged-in admins)

## Known gaps

- Only the local `admin` account can log in — AD-authenticated login (`updatewatch2-server#2`) is a separate, not-yet-implemented path. The AD-connection tab from CLAUDE.md section 6.1 has no UI yet either.
- No test-mail button — `IEmailNotificationService.SendTestEmailAsync` exists server-side but isn't exposed via an endpoint yet.
- `SmtpWarningBanner` reflects `smtpConfigured` (is SMTP set up at all), not live reachability — the reachability check already exists server-side (`IEmailNotificationService.IsHealthyAsync`) but isn't exposed via the settings endpoint yet.
- Changing the log level from `AdminPage` persists immediately but only takes effect on the server's next restart — there's no hot-reload of the running logger's minimum level (see `Program.cs`'s comment on this).
- No "forgot password" flow — if the admin loses the auto-generated password without having changed it via `/api/auth/password` first, recovery means resetting the `AdminAccounts` row directly in the database.
- `npm audit` reports one moderate advisory in the `vite`/`esbuild` dev-server chain (GHSA-67mh-4wv8-2f99); the fix requires an early/prerelease Vite 8, which drags in an unrelated, currently broken `vitest` peer-dependency graph. Left unpatched deliberately — it only affects `npm run dev`, not the production build, and the dev server already binds to localhost only. Revisit once the ecosystem catches up.
