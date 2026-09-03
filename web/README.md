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
- `src/i18n/` — DE/EN translations (react-i18next), browser-language default with a `localStorage` override
- `src/theme/` — light/dark theme via CSS custom properties (`tokens.css`, kept in sync by hand with the server's `Resources/Themes/*.json`), system-preference default with a `localStorage` override
- `src/pages/` — `LoginPage`, `AgentsListPage` (with bulk approve), `AgentDetailPage`, `AdminPage`
- `src/components/` — shared bits: `ThemeToggle`, `LanguageSwitcher`, `SmtpWarningBanner`

## Known gaps

- No login is wired up — the server has no session/auth endpoint yet (`updatewatch2-server#2`); `LoginPage` is a static, non-functional form.
- `/api/admin/settings` is read-only server-side, so `AdminPage` only displays values (`updatewatch2-server#4`).
- `SmtpWarningBanner` reflects `smtpConfigured` (is SMTP set up at all), not live reachability — the reachability check already exists server-side (`IEmailNotificationService.IsHealthyAsync`) but isn't exposed via the settings endpoint yet.
- `npm audit` reports one moderate advisory in the `vite`/`esbuild` dev-server chain (GHSA-67mh-4wv8-2f99); the fix requires an early/prerelease Vite 8, which drags in an unrelated, currently broken `vitest` peer-dependency graph. Left unpatched deliberately — it only affects `npm run dev`, not the production build, and the dev server already binds to localhost only. Revisit once the ecosystem catches up.
