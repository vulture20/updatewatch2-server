# Changelog

All notable changes to the UpdateWatch2 Server are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and versioning follows [SemVer](https://semver.org/), starting at `0.x.x`
(beta) per the project's CLAUDE.md and reaching `1.0.0` — ending the beta
phase — at the user's explicit request. This file tracks the **server**
version specifically — one of CLAUDE.md's four independent version
numbers (server, agent, transfer protocol, DB schema), which evolve on
their own schedules; a protocol or schema bump is called out inline
below where a change caused one, but this changelog isn't those
changelogs.

## [1.7.0] - 2026-09-23

### Added

- **Automatic agent registration is now admin-configurable, at the user's explicit request** — "Die automatische Registrierung von Agents soll abschaltbar gemacht werden. Damit soll u. a. ein permanentes Neuregistrieren von Agents verhindert werden. Sei es durch einen Fehler oder mit böser Absicht." New `AdminSettings.AutoRegistrationEnabled` (Settings → General, DB schema `1.2.5`, default `true` — today's existing behavior, unchanged until an admin explicitly turns it off), with a checkbox plus explanatory text.
  - Gates exactly one branch of `AgentRegistrationService.RegisterAsync`'s state machine: a brand-new hostname's very first contact (no token, no existing `Agent` row) — turning it off makes that call `Rejected` before any row is ever created, and records an audit-log entry (`agent.register.auto-registration-disabled`) so an admin has visibility into an actual flood/abuse attempt while the switch is off. Deliberately scoped no wider than that: an already-approved/certified agent's heartbeat, certificate renewal/self-heal, install/reboot delivery, and an agent already mid-onboarding (an existing row polling with its own already-issued token, or one an admin is in the middle of approving) are all completely unaffected, since none of those go through this branch.
  - Backed by a real column (`AddAutoRegistrationEnabled` migration) rather than an env-var-only kill switch like `UPDATEWATCH2_DEMOMODE`/`UPDATEWATCH2_TRUSTEDIP`, since this is meant to be an easily reversible, UI-driven response to an ongoing abuse situation, not a deploy-time choice.
  - Test coverage at the service layer (`AgentRegistrationServiceTests`): a brand-new hostname is rejected with no row created while disabled; an already-pending agent polling with its own valid token still succeeds; an already-approved agent still receives its certificate — all while the toggle is off, proving the gate's scope is exactly as narrow as intended.

## [1.6.2] - 2026-09-23

### Fixed

A code review of the newly-shipped 1.6.1 pagination feature (not a user report) found and fixed several real gaps, plus one bug reported directly by the user:

- **`AgentsListPage`/`SchedulesListPage` never reset back to page 1 when a filter/search/sort changed.** `usePageSlice`'s own clamp only fires once `page > totalPages`, so changing a filter while on a later page could silently render an unrelated slice of the new result set rather than the top of it. Fixed with a `useEffect` in `AgentsListPage` keyed on `filters`/`statFilter`/`sort` only — deliberately not `agents` itself, so a background poll tick never resets the page an admin is currently browsing.
- **`AuditLogTab` could strand itself on an out-of-range page when `AuditLogItemsPerPage` changed live** (the Settings tab stays mounted alongside it via `AdminPage`'s `hidden`-tab pattern) — the resulting empty response rendered "No matching entries" with no `<Pagination>` to click back out of, since that's only shown for a non-empty result. Fixed by adjusting `page` back to 1 directly during render (not in a `useEffect`, which would still let one stale fetch through first) whenever `itemsPerPage` changes.
- **`usePageSlice`'s own out-of-range clamp ran in a post-render `useEffect`**, so the render that shrank `totalPages` below the current page still computed `pageItems` from the stale, now out-of-range page first — a real, if brief, empty-table flash before the effect corrected it. Fixed the same way as the `AuditLogTab` case above: the clamp now happens directly during render, with `pageItems` derived from `Math.min(page, totalPages)` so even the triggering render itself is never wrong.
- **`AuditLogService.GetPageAsync`'s "unlimited" branch (an admin explicitly opting out of pagination) had no upper bound at all** — a long-lived instance with unlimited `AuditLogRetentionDays` and hundreds of thousands of rows could return the entire table in one HTTP response. Fixed with a `MaxUnlimitedRows` cap (10,000; `TotalCount` still reports the real total so a caller can tell the cap was hit).
- **The "unlimited" concept was encoded as a magic negative `pageSize` value with a different meaning at each of three layers** (`AdminSettings.ItemsPerPage`: `0` = unlimited; the query parameter: `0` = unspecified/default; `GetPageAsync`: negative = unlimited) — a real ambiguity, not just a style nit: a caller reasonably assuming `pageSize=0` meant "no limit" (the convention used everywhere else in this feature) silently got the 50-row default instead, with no error. Replaced with one unambiguous representation: `AuditLogController` now takes an explicit `unlimited` query flag, and `IAuditLogService.GetPageAsync`'s `pageSize` parameter is `int?`, where `null` is the only "no limit" value — every other value, including a stray negative one, is just an ordinary page size clamped to `[1, 200]`.
- **`useItemsPerPage` always rendered once with its hardcoded placeholder default (50) before the real admin-configured value loaded.** Cosmetic on `AgentsListPage`/`SchedulesListPage` (purely client-side pagination), but a genuine duplicate HTTP request on `AuditLogTab`, whose fetch effect depends on `itemsPerPage`. Fixed with a new `useItemsPerPageState` (exposing a `loaded` flag) that `AuditLogTab` now waits on before firing its first fetch.
- **`AgentsListPage`'s toolbar count showed e.g. "81 of 81 agents" even when the list was paginated and only 50 agents were actually visible — reported directly by the user.** It used `filteredAndSorted.length` (everything matching the active filter, across every page) for both halves of the count whenever no filter was active. Fixed to show `pageItems.length` (what's actually on screen right now) "of" `filteredAndSorted.length` (how many match the active filter) — the existing "Total" stat card already covers the grand unfiltered total separately.
- Two dead i18n keys (`auditLog.previousPage`/`nextPage`/`pageIndicator`, left over from the 1.6.1 switch to the shared `Pagination` component's own `pagination.*` keys) removed from both locale files.

## [1.6.1] - 2026-09-19

### Added

- **Pagination, previously only on the Audit Log, now also exists on the Agent overview and Zeitpläne (Schedules) lists — at the user's explicit request ("Im Audit-Log gibt es ja Pagination. Ist das in der Agent-Übersicht und bei den Zeitplänen ebenfalls so? Falls nicht, bitte dort auch umsetzen. Außerdem sollte das Pagination so umgebaut werden, dass man auch direkt die Seitenzahl anspringen kann... In den Einstellungen unter 'Allgemein' sollte die Anzahl der angezeigten Einträge pro Seite einstellbar sein.").** Both new lists paginate client-side, over the same already-loaded, already-filtered/sorted array they always fetched in full (`GET /api/agents`/`GET /api/schedules` stay completely unpaged — no backend change to either controller/service), via a new shared `usePageSlice` hook. On `AgentsListPage`, the header "select all" checkbox now selects/deselects only the current page's rows rather than every filtered row across every page — a deliberate, user-confirmed change from its previous "every filtered row" behavior, now that a filtered result can span multiple pages.
- **A shared `Pagination` component (`web/src/components/Pagination.tsx`) replaces the Audit Log's previously bespoke Previous/Next-only pager and now backs all three lists**, adding direct page-number jump buttons alongside Previous/Next everywhere — with smart ellipsis truncation (`buildPageNumbers`, independently unit-tested) so a long list doesn't render one button per page.
- **New global admin setting, `AdminSettings.ItemsPerPage`** (Settings → General, DB schema `1.2.3`) — a fixed dropdown (10/25/50/100/200, default 50) plus an explicit "unlimited" option (sentinel `0`, mirroring `AuditLogRetentionDays`'s own unlimited convention) that shows every row on one page, fetched via a new `useItemsPerPage` hook (mirroring `SmtpWarningBanner`'s fetch-once-plus-live-update-via-`onAdminSettingsSaved` pattern).
  - For the Audit Log specifically (the one list that's genuinely server-paginated, unlike the other two), "unlimited" needed a real backend change: `AuditLogService.GetPageAsync` now treats a negative `pageSize` (the frontend sends exactly `-1`) as "no limit — return every matching row in one response, no `Skip`/`Take`" — a distinct sentinel from the query parameter's own pre-existing `pageSize=0` ("caller didn't specify one, default to 50"), since `AdminSettings.ItemsPerPage`'s own `0` "unlimited" value is a different, unrelated context that the frontend translates before it ever reaches this endpoint. A deliberate, admin-opted-into exception to this method's usual "never pull an unbounded table into memory" discipline — only taken when an admin explicitly chose unlimited.
  - `AgentsListPage`/`SchedulesListPage`'s own client-side pagination handle `ItemsPerPage=0` far more simply, since the full list was already loaded either way: it just means one page containing everything, same as today's pre-pagination behavior.
- **Follow-up the same day, at the user's explicit request: the Audit Log's own page size is now independently configurable, not governed by the shared `ItemsPerPage` setting.** New `AdminSettings.AuditLogItemsPerPage` (DB schema `1.2.4`) — a genuinely separate setting, not an override, validated against the identical fixed set of steps (10/25/50/100/200, plus `0` for unlimited). `useItemsPerPage` gained a `kind` parameter (`'default'` | `'auditLog'`) selecting which of the two underlying fields to read/react to, so `AuditLogTab` now calls `useItemsPerPage('auditLog')` while `AgentsListPage`/`SchedulesListPage` keep using the shared default. The Settings → General "Pagination" card now shows both fields side by side, each with its own hint text.

### Fixed

- **The Settings → Info tab still showed "v1.6.0" after this very version's own work shipped — reported by the user directly.** The same class of gap this file already documents once before (`AppVersion.cs`'s own doc comment says "keep in sync with the repository root `VERSION` file; bump both together", nothing enforces it, and nothing did): `VERSION` was bumped to `1.6.1` for the pagination work above, but `AppVersion.cs`'s `Current` constant — the one thing `GET /api/version`/the Info tab actually reads — was never touched, so the running server kept reporting the previous version regardless of what `VERSION` said. Fixed by bumping `AppVersion.Current` to `1.6.1` too. A repo-wide grep confirmed no other hardcoded copy of the server version exists — `UpdateWatch2.Server.csproj`'s `<Version>` already reads the `VERSION` file directly at build time rather than duplicating it, and `web/package.json`'s own `"version": "0.0.0"` is deliberately unrelated (this project's own established convention, not a bug).

### Changed

- `Db/SchemaVersion.cs` bumped to `1.2.4` (`1.2.3` for `AdminSettings.ItemsPerPage`, then `1.2.4` the same day for `AdminSettings.AuditLogItemsPerPage`). No protocol bump either time — this is a purely admin-session-gated UI/settings change with no agent-facing wire effect.

## [1.6.0] - 2026-09-19

### Fixed

- **Recurring/Cron schedules could silently fire at the wrong wall-clock time, inconsistently by season — reported by the user directly ("Die Zeitpläne werden mit CEST, aber später mit lokaler Zeit angezeigt. Das ist ziemlich verwirrend. Die eingestellte Zeit soll bitte ebenfalls als lokale Zeit behandelt werden.").** Root cause, confirmed by inspecting the actual running container before writing any code: `ScheduleRecurrenceCalculator` interpreted a Recurring/Cron schedule's bare time-of-day using `TimeZoneInfo.Local` — the server *process's own OS time zone*, which is UTC by default in a Docker container unless an admin explicitly sets a `TZ` environment variable (confirmed the real container in question was in fact running UTC while its host was CEST/UTC+2). A `Once` schedule was never affected, since its date/time is already converted to an unambiguous UTC instant in the browser before being sent; only `Recurring`/`Cron`, whose wall-clock time carries no time zone of its own, silently drifted from what was actually typed by a DST-dependent amount (2h in summer, 1h in winter for a Europe/Berlin admin against a UTC container) — the drift itself changing with the seasons is exactly what made this look like "sometimes CEST, sometimes local time" rather than a simple, obviously-wrong offset.
  - Asked directly how to fix it — a container `TZ` env var (zero code, but easy to forget, which is exactly what had happened) versus a real admin-configurable setting — the user chose the latter.
  - New `AdminSettings.TimeZoneId` (Settings → General, default "UTC", validated as a real IANA time zone identifier via `TimeZoneInfo.FindSystemTimeZoneById`) replaces `TimeZoneInfo.Local` throughout `ScheduleRecurrenceCalculator`, which now takes a `TimeZoneInfo` parameter instead of reading the OS zone implicitly — resolved once per call by a new `ScheduleService.ResolveTimeZone()` (falling back to UTC if the stored value somehow doesn't resolve on this machine). DB schema bumped to `1.2.2`.
  - The admin UI's new "Zeitzone"/"Time zone" card has a plain `<select>` populated via `Intl.supportedValuesOf('timeZone')` plus a "Use browser's time zone" convenience button (`Intl.DateTimeFormat().resolvedOptions().timeZone`) that fills the field with the browser's own detected zone — the exact value an admin actually wants in the overwhelmingly common case where they're configuring this from their own machine.
  - `ScheduleDialog`'s time-of-day and cron-expression fields now show a live hint naming the currently configured zone (fetched once on open via the existing `GET /api/admin/settings`), so an admin sees up front what "14:00" actually means instead of only discovering a mismatch later from an unexpected next-run time.
  - Live-verified end to end against a real isolated server instance with its OS time zone deliberately forced to UTC (reproducing the exact reported bug): confirmed the default `TimeZoneId` is "UTC", set it to "Europe/Berlin" via the real UI and confirmed it persisted, then created a real Recurring schedule for "14:00" and confirmed via the real API that its computed `NextRunAt` was `...T14:00:00+02:00` (correct Berlin/CEST offset) — never `14:00Z`, which is what the old bug would have produced from a UTC-zoned container.

### Added

- **`ScheduleRecurrenceCalculator` unit tests were rewritten to actually prove time-zone correctness, not just "does it work when the parameter happens to be UTC."** Every Weekly/IntervalDays test now uses a fixed, no-DST custom `TimeZoneInfo` distinct from both UTC and the test machine's own local zone, and a new dedicated Cron test exercises a real `Europe/Berlin` `TimeZoneInfo` across both a winter and a summer date, asserting the identical wall-clock schedule ("04:30 daily") resolves to two different UTC instants (03:30 UTC in winter/CET, 02:30 UTC in summer/CEST) — the exact DST-awareness this whole fix depends on.

## [1.5.1] - 2026-09-19

### Added

- **`AgentsListPage` now flags an outdated agent with an icon to the left of the hostname, mirroring the existing offline/certificate-rejection icons.** Raised as an exploratory question first ("Wenn der jeweilige Agent veraltet ist... sollte das in der Agent-Ansicht angezeigt werden. Am besten wie auch die anderen Icons links neben dem Hostname. Mein Vorschlag wäre ein Pfeil nach unten. Was wäre dein Vorschlag?"), answered with a recommendation (an up arrow — the software-industry-standard "update available" symbol app stores/package managers consistently use, versus a down arrow which reads more as "download"/"demote"), then implemented exactly as proposed once confirmed ("Setze das bitte genau so mit deinem Vorschlag um."). No DB schema/protocol bump — purely a new computed DTO field plus a new icon component.
  - `AgentListItemDto` gained `AgentVersion` (the same self-reported string `AgentDetailDto` already exposed, not previously surfaced on the list) and `IsOutdated` (computed live on every request, never a stored flag — same discipline `IsOffline` already follows) — independent of whether the agent-auto-update feature itself is enabled, since this is purely informational.
  - New shared `AgentUpdates.AgentVersionComparer.IsOlderThan` is the one decision point for "is this agent version older than a reference version", now used both by `AgentService.GetAllAsync` (the new icon) and `AgentUpdateService`'s own self-update-offer check (refactored to call the shared helper instead of duplicating the comparison) — so the two can never disagree about what counts as outdated. Missing/unparsable versions on either side err toward "not outdated", matching the self-update offer's own long-standing reasoning for the identical comparison.
  - New `web/src/components/OutdatedIcon.tsx` (an accent-purple circle with a dark up-arrow, matching `WarningTriangleIcon`/`OfflineIcon`'s exact SVG/tooltip conventions), wired into `AgentsListPage`'s hostname cell next to the existing offline/certificate-rejection icons, with a bilingual tooltip naming the agent's current version (`agents.outdatedIcon`).
  - Live-verified end to end against a real running server, not just `dotnet test`/`npm test`: registered two scratch agents self-reporting `1.0.16` and `1.0.20`, manually uploaded a fake `1.0.20` release to seed `AgentUpdateState.LatestVersion`, confirmed the real `GET /api/agents` response computed `isOutdated: true`/`false` correctly for each, and confirmed in a real headless-Chromium screenshot that only the `1.0.16` agent's row shows the new purple up-arrow icon, with the correct German tooltip text.

## [1.5.0] - 2026-09-18

### Added

- **A third schedule type, "Cron", plus a per-schedule failure-notification email toggle, both at the user's explicit request** ("Baue noch zusätzlich zu den beiden Möglichkeiten 'Einmalig' und 'Wiederkehrend' auch 'Cron' ein... Außerdem soll es eine Checkbox geben, womit man eine Mailbenachrichtigung bei fehlgeschlagenen Updates oder generell Fehlern ein- und ausschalten kann (Standard an)."). DB schema bumped to `1.2.1`; no protocol bump — this is pure server-side orchestration, the agent is unaware schedules exist at all, exactly like the rest of this feature.
  - **Cron**: `ScheduleType` gained a third value alongside `Once`/`Recurring`, backed by a new `Schedule.CronExpression` (standard 5-field format) and the [Cronos](https://github.com/HangfireIO/Cronos) NuGet package for parsing/next-occurrence computation — evaluated in the server's own local time zone via `TimeZoneInfo.Local`, the same no-per-schedule-time-zone reasoning `ScheduleRecurrenceCalculator`'s own doc comment already gives for `Recurring`. `ScheduleService.ValidateAsync` rejects an empty or unparseable expression with new `ScheduleCronExpressionRequired`/`ScheduleCronExpressionInvalid` API error codes, the latter carrying Cronos' own parse-error message as `errorDetail` for the admin UI to display. `ScheduleDialog` gained a third "Cron" radio option next to "Einmalig"/"Wiederkehrend", showing a plain text input (with a placeholder and a format hint) in place of the pattern/weekday/interval fields when selected.
  - **NotifyOnFailure** (default true): a new `Schedule.NotifyOnFailure` checkbox, shown in `ScheduleDialog` next to the deadline-hours field. Reuses the exact bilingual `IEmailNotificationService.SendNotificationAsync` primitive and `IsConfigured`-plus-recipient guard every other automated notification in this codebase already establishes (`UpdateThresholdNotificationWorker`, `CertificateExpiryWorker`), via a new shared `Schedules.ScheduleFailureNotifier` static helper — the one decision point for all three places a schedule-originated action can end up Failed/Missed, so they can never disagree about the guard or the audit-log/email pairing: `ScheduleService.ExpireMissedAsync` (one aggregated email per schedule-run batch of newly-Missed items, reusing the exact per-run grouping its own audit-log entry already uses — a merely `Skipped` conditional reboot, never confirmed necessary, is not a failure and is never emailed about), `UpdateService.AcknowledgeInstallAsync` (one email per Failed install acknowledgement), and `AgentService.AcknowledgeRebootAsync` (the same for a Failed reboot acknowledgement) — the latter two required adding `IAdminSettingsStore`/`IEmailNotificationService` (and, for `UpdateService`, an `ILogger`) as new constructor dependencies. Audit logging of the underlying Failed/Missed transition itself stays unconditional regardless of this toggle, matching this codebase's established `CertificateExpiryNotificationsEnabled`-style convention — only the email attempt is gated.
  - The scaffolded EF Core migration hit this project's own documented `dotnet ef` gotcha again — `NotifyOnFailure`'s `AddColumn` defaulted to `false` instead of the entity's real `true` initializer — hand-corrected before committing, per CLAUDE.md's own standing note to check for this on every migration adding a non-zero-default column.
  - Live-verified end to end in a real browser (Playwright against a real running server, not just `dotnet test`/`npm test`): all three schedule-type radios render, selecting "Cron" swaps in the expression field, the NotifyOnFailure checkbox defaults checked and toggles correctly, saving a `*/15 * * * *` cron schedule persists `scheduleType: "Cron"`/`cronExpression`/`notifyOnFailure` correctly via the real API and computes the correct next 15-minute-boundary `nextRunAt`, and the schedules list correctly labels the row "Cron".

## [1.4.3] - 2026-09-18

### Added

- **`ScheduleDialog`'s agent picker gained a "select all visible" checkbox, at the user's explicit request** ("Gibt es die Möglichkeit bei der Agent-Auswahl des Zeitplans alle gerade sichtbaren Agents auszuwählen/anzuhaken?"). Mirrors `AgentsListPage`'s own long-standing select-all-visible convention exactly, not a new pattern: it only selects/deselects agents currently matching the search filter, never the schedule's whole existing membership, and shows an indeterminate state when some but not all visible agents are already checked. Live-verified in a real browser (Playwright, same setup as the v1.4.2 fix): filtered the agent list down to two hostnames, checked "select all visible," confirmed only those two got checked and the count updated; cleared the filter and confirmed the previously out-of-view agents were untouched and the checkbox correctly showed its indeterminate (dash) state; checking it again from there selected every now-visible agent including the previously-hidden ones. Real unit test coverage added alongside (`SchedulesListPage.test.tsx`).

## [1.4.2] - 2026-09-18

### Fixed

- **`ScheduleDialog`'s agent picker was still completely obscured after v1.4.1's fix, reported by the user directly with a screenshot: "man kann durchscrollen, aber man sieht immer maximal eine einzige Zeile."** v1.4.1 fixed the radio-button/fieldset sizing (confirmed correct this time via a real headless-Chromium screenshot, not just reasoning — Playwright was installed for this specific verification, logged into a real running server, and screenshotted the actual rendered dialog) but left the real cause of the second bug untouched: the agent-picker `.card`'s default `flex-shrink: 1` as a flex child of `.dialog` (itself `display: flex; flex-direction: column`). Once `.dialog`'s total content exceeded its own 600px cap, its flex layout compressed every shrinkable child to fit — form-field `<label>`s resisted shrinking (fixed `min-height` on their inputs), so the one plain, freely-shrinkable `.card` absorbed nearly the entire deficit, rendering at ~17px (one row) instead of its intended 180px regardless of scroll. Measured directly in the real browser before and after: `clientHeight` was 17px against a `scrollHeight` of 321px beforehand (matching the "can scroll but only ever see one row" report exactly), and a clean 180px after adding `flexShrink: 0` to the card's inline style so `.dialog`'s own shrink pass never touches it. Confirmed visually afterward: the agent list now shows five full rows within its own scrollable box, selecting agents updates "N Agent(s) ausgewählt" below it with no overlap, and Save/Cancel are reachable. Both this and the v1.4.1 fixes are now real, screenshot-confirmed fixes rather than reasoned-through-CSS guesses — worth remembering for any future report of a flex child not respecting its own `max-height`: check `flex-shrink` on that item before assuming the `max-height`/`overflow` combination itself is wrong.

## [1.4.1] - 2026-09-18

### Fixed

- **Two `ScheduleDialog` layout bugs reported by the user directly after trying the new schedules feature: the "Once"/"Recurring" radio options rendered extremely large, and the "N agent(s) selected" hint text overlapped the agent picker list.** Root cause for the first: `index.css`'s generic `input, select { width: 100%; min-height: 36px; ...bordered... }` rule applies to every `<input>` by default, and while there's a long-standing `input[type='checkbox']` override sizing it down to a plain 16×16 control, no equivalent existed for `input[type='radio']` — the new schedule-type/pattern radios (this app's first use of radio inputs at all) inherited the full-width, bordered, 36px-tall text-input styling instead. Fixed by extending both the sizing override and the `label:has(...)` row-layout selector to also match `input[type='radio']`. `ScheduleDialog` is also this app's first use of `<fieldset>`/`<legend>` (for the schedule-type/pattern/weekday/action groups), which had no styling at all and so rendered with the browser's default grooved-border-plus-straddling-legend look — visibly out of place against Nocturne's borderless, card-based chrome and a further contributor to the "far too large" impression; added a plain, bordered-less `fieldset` reset with the `legend` styled like this app's existing field-caption `label` convention. Root cause for the second: the per-agent checkbox `<label>` in the scrollable agent picker had an inline `style={{ display: 'block' }}` overriding this app's own established `label:has(> input[type='checkbox'])` row-layout rule, which every other checkbox in the app already relies on with no such override — removed, letting each agent row use the same tested layout as everywhere else. Not visually verified in a real browser — no browser automation available in this sandbox session; re-check after rebuilding that both are actually resolved.

## [1.4.0] - 2026-09-18

### Added

- **Scheduled maintenance windows ("Zeitpläne") — closes #25, at the user's explicit request, from a concept the user asked to be written up and then implemented directly.** A named `Schedule` targets a fixed, admin-picked list of agents and fires — once at a specific date/time, or recurring on selected weekdays or every N days — an install and/or a reboot, reusing the exact same delivery mechanism the admin UI's own manual bulk-action buttons already use (`Agent.PendingInstallRequestedAt`/`PendingRebootRequestedAt`, delivered on the agent's next heartbeat, acknowledged via the existing `install-ack`/`reboot-ack` endpoints). Deliberately a pure server-side orchestration layer: **no protocol bump, no agent-side code change, no new agent build required** — a scheduled trigger is indistinguishable from a manual one as far as the agent is concerned. New DB schema (`1.2.0`): `Schedule`, `ScheduleAgent` (the fixed n:m agent list — no dynamic/criteria-based group, per the design decision on #25), `ScheduleRun`/`ScheduleRunAgent` (per-firing history, needed to show "who got this run, who missed it" and to let a schedule-originated pending trigger expire on its own deadline without touching a manually-triggered one), and three new nullable `Agent` columns correlating a live pending trigger back to the run that set it.
  - **Combined "install, then reboot only if required"** is the one genuinely non-trivial case: the reboot need can only be known once the install actually completes, so the schedule doesn't decide at fire time — it marks the agent as watched (`ScheduleRunActionStatus.AwaitingInstallResult`) and `AgentRegistrationService.RecordAliveAsync` promotes it to a real, delivered reboot trigger the moment that agent's own next heartbeat reports `RebootRequired: true`, or lets it expire as `Skipped` if the run's own deadline passes first with no reboot ever confirmed necessary. A reboot-only schedule with "only if required" checked, by contrast, is decided once, at fire time, against the agent's already-known `RebootRequired` value.
  - **A new eighth server-side `BackgroundService`, `ScheduleWorker`** (after `AgentUpdateCheckWorker`/`AuditLogRetentionWorker`/`CertificateExpiryWorker`/`DatabaseVacuumWorker`/`UpdateThresholdNotificationWorker`/`AgentOfflineNotificationWorker`/`SmtpHealthCheckWorker`), ticking every minute (deliberately much finer than every other worker's 6h/24h housekeeping cadence, since real wall-clock times need to be hit reasonably precisely) — fires whatever's due via `ScheduleService.FireDueSchedulesAsync`, then expires whatever's overdue via `ExpireMissedAsync`.
  - **A missed-deadline sweep, not indefinite pending** — the one deliberate behavioral difference from a manual trigger: if an agent hasn't picked up a schedule-originated install/reboot within a configurable per-schedule deadline (default 4 hours), it's abandoned and logged as `Missed` instead of staying pending forever; a manually-triggered install/reboot is completely unaffected by this and keeps its existing unbounded-pending behavior.
  - **Deleting a schedule cancels its own still-outstanding pending action(s)** rather than leaving a not-yet-checked-in agent to act on a trigger from a schedule that no longer exists — live-verified against a real running server, not just `dotnet test`.
  - New `SchedulesController` (`api/schedules`, admin-session gated, matching `AgentsController`'s own top-level-resource convention rather than living under `api/admin/...`) for CRUD, run history (`GET .../runs`), and a manual "run now" (`POST .../run-now`, independent of and never disturbing the regular recurrence — useful for testing a freshly created schedule). New admin UI top-level nav item "Schedules"/"Zeitpläne" (`SchedulesListPage`, `ScheduleDialog` for create/edit including an agent picker, `ScheduleRunsDialog` for history) — bilingual DE/EN throughout, matching CLAUDE.md's own UI requirement.
  - `IUpdateService.TriggerInstallManyAsync`/`IAgentService.TriggerRebootManyAsync` both gained a new optional `scheduleRunId` parameter (default `null`, so every existing caller — including the admin UI's own manual bulk-install/-reboot buttons — is unaffected) that's all `ScheduleService` needs to hook into the existing bulk-trigger machinery rather than duplicating it; `AcknowledgeInstallAsync`/`AcknowledgeRebootAsync` were extended symmetrically to record the outcome on the originating `ScheduleRunAgent` row when one is set.
  - Two real EF-Core-on-SQLite `DateTimeOffset`-comparison-in-a-LINQ-predicate translation failures were caught immediately by the new test suite (`ScheduleService.FireDueSchedulesAsync`'s "is it due" check, `ExpireMissedAsync`'s "is it overdue" check) — the same confirmed, repeated gap this codebase already documents for `AuditLogService.PurgeOlderThanAsync`/`CertificateRejectionService.GetStatusAsync`; fixed with the identical established workaround (project down to plain columns, decide client-side, then load the full entities for just the resulting ids).
  - Live-verified end to end against a real running server, not just `dotnet test`: logged in over the real HTTP API, created a schedule for a seeded agent, confirmed the returned JSON's enum fields serialize as strings (not raw numbers — the same `JsonStringEnumConverter` discipline this codebase already applies everywhere else, decorated on the four new enums), triggered it via `run-now`, confirmed a real `ScheduleRun`/`ScheduleRunAgent` row was created with status `Pending` and the target agent's real `PendingInstallRequestedAt` was set, then deleted the schedule and confirmed that pending trigger was cancelled.

## [1.3.26] - 2026-09-18

### Changed

- **`Microsoft.Extensions.Http`'s own automatic HTTP-client logging on the GitHub release client is now Debug-only, at the user's explicit request after reviewing `log-level-audit.md`.** `AddHttpClient<IGitHubReleaseClient, GitHubReleaseClient>` wraps every outbound GitHub API call in two library-provided logging handlers whose "Start/End processing HTTP request", "Sending HTTP request"/"Received HTTP response headers", and — on a genuine transport failure — "HTTP request failed" (with the full exception/stack trace) messages are all hardcoded at Information severity by that NuGet package itself; they showed up on every single `AgentUpdateCheckWorker`/`AgentUpdateService` check against GitHub already at this project's own default log level, redundant to the shorter result/error lines this codebase's own `AgentUpdateService` already logs for the same calls. A plain `Logging:LogLevel:Default` write can only ever raise or lower the *minimum* level a category logs at — it can't change an individual message's own baked-in severity — so making an Information-level library message behave like a Debug-level one needed a category-specific override instead: new `LogLevelMapper.ToHttpClientLoggingCategoryValue` maps the effective LogLevel onto "Debug" (letting the category's own Information messages through, same as everything else at that setting) when it's genuinely DEBUG, and "Warning" (a complete mute — neither category ever logs above Information) at every other selectable level. Both `Program.cs`'s pre-`Build()` resolution and `AdminSettingsStore.Apply`'s live re-application now write this alongside `Logging:LogLevel:Default`, so a LogLevel change reaches these two categories with the same "no restart required" guarantee the Default value itself already has. The fifth message this category actually has — "HTTP request failed after {ElapsedMilliseconds}ms" (EventId 104, also Information, fires only on a real connect/transport failure) — was found only while implementing this, not in the original audit pass; the user chose to demote it alongside the other four rather than leave it at Information or promote it to Warning, since this codebase's own explicit Warning/Error logging already reports the same failure more concisely. Live-verified against a throwaway harness referencing this project's own compiled `LogLevelMapper`, with a real `AddHttpClient` client pointed at a deliberately unreachable host: at "INFO", none of the five lines appear even on a genuine connection-refused error with a full stack trace; at "DEBUG", all five appear exactly as before. See `log-level-audit.md` (rows 176–181, 244–245) for the full audit this originated from.

## [1.3.25] - 2026-09-18

### Fixed

- **Closes #24 — self-update was completely broken for every agent below v1.0.20, not just unable to receive the multi-arch fix.** Reported directly by the user right after v1.3.24/agent v1.0.20 shipped, on both Windows and Linux: `Agent release 1.0.20 has no asset for this platform (WindowsInstaller) — nothing to self-update to yet.` Confirmed root cause from the log format itself (the single-placeholder `({AssetKind})` shape is the *old*, pre-v1.0.20 log line — the current code logs `({AssetKind}, {AssetArch})`): v1.3.24 renamed `AgentUpdateOffer`'s three wire fields (`windowsInstaller`/`linuxDeb`/`linuxRpm`) to six new architecture-specific ones outright, instead of adding the new fields alongside the old — so any agent build older than v1.0.20 (still deserializing via its own three-field record) found none of its expected property names in the response, read every slot as `null`, and concluded there was nothing to update to. Since self-update is the *only* automatic delivery mechanism, this wasn't the "safe degradation" it was reasoned to be at the time — it was terminal: an already-stuck fleet had no path back without a manual reinstall on every machine. Fixed by adding `WindowsInstaller`/`LinuxDeb`/`LinuxRpm` back to `AgentUpdateOffer` as plain computed properties aliasing the corresponding `*X64` field (not extra constructor parameters — one source of truth, no way for an alias to drift out of sync) — a pre-v1.0.20 agent can now read a real x64 asset again through the field names it still knows, self-update at least once, and only then start benefiting from correct architecture-aware selection on the next release. No new agent build is required for this fix to take effect on an already-stuck fleet. Protocol bumped to `1.7.0` for the additive field restoration; `docs/protocol.md` (both repos' copies) updated to show the full nine-field shape and explain the aliasing. New `AgentUpdateOfferBackwardCompatibilityTests` actually deserializes a freshly serialized server response into a hand-written stand-in for the old, pre-v1.0.20 agent-side type — the kind of test that would have caught this the first time, unlike every existing test, which only ever round-tripped through the same, already-renamed type on both ends. A companion finding was also added to `updatewatch2-agent#24` (the separate, longer-standing Linux self-update-never-completes report): the user confirmed the underlying `dpkg` failure there is an architecture mismatch (an amd64 host being offered the arm64 `.deb`) — almost certainly the original, pre-v1.3.24 manifestation of the exact same no-arch-awareness bug this whole area of work has been chasing, which this fix should also resolve for that stuck agent once it can read the corrected offer again.

## [1.3.24] - 2026-09-18

### Fixed

- **Agent self-update never accounted for the new multi-arch releases (updatewatch2-agent#22/#23) — a real gap found by a direct user question ("wurde beim Selfupdate berücksichtigt, dass es jetzt zusätzliche Releases gibt, und ist sichergestellt, dass immer die richtige Version installiert wird?"), not a live incident.** The answer, investigated directly rather than assumed: it hadn't been considered, and no, the right version was not guaranteed. `AgentUpdateAssetKind`/`AgentUpdateAssetClassifier.Classify` (and the `AgentUpdateState` singleton row / `AgentUpdateOffer` wire DTO built from it) only ever had one slot per package kind (Windows installer/`.deb`/`.rpm`) — with every release now publishing two architectures per kind, both classified identically, so `AgentUpdateService.DownloadAssetsAsync` would silently let the second asset of a kind (whichever GitHub happened to list last) overwrite the first in that kind's single slot, leaving roughly half of a fleet's agents (by architecture) offered the wrong platform's binary to self-update to. Fixed by making the whole pipeline architecture-aware: `Classify` now returns `(AgentUpdateAssetKind, AgentUpdateAssetArch)` (detected from each filename's own architecture suffix — `-x64.exe`/`-arm64.exe`, `_amd64.deb`/`_arm64.deb`, `.x86_64.rpm`/`.aarch64.rpm` — every historical release asset already carried one, so this is fully backward compatible), `AgentUpdateState` gained six asset slots instead of three (DB schema `1.1.7`), and `AgentUpdateOffer` gained six wire fields instead of three (protocol `1.6.0`) — deliberately a *new*, separate `AgentUpdateAssetArch` enum rather than expanding `AgentUpdateAssetKind` itself to six values, since kind alone still fully determines the package format/command several call sites (agent-side `LinuxPackageApplier`, this project's own `IPlatformUpdateApplier`/`ILinuxUpdateSession` DI selection) only ever needed to know, with no reason to make them learn about architecture too. **A second, related bug found and fixed in the same pass**: `AgentUpdatesController.Upload`'s duplicate-asset check was keyed by kind alone, so uploading both architectures of the same kind in one request — the normal, expected admin workflow now that every kind has two — was wrongly rejected as a "duplicate" of itself; now keyed by the full (kind, architecture) pair. `MaxUploadBytes` doubled (200 MB → 400 MB) to keep headroom now that a full manual upload is six files, not three. **A third bug, this one in the tooling rather than the application code, caught before it ever touched a real database**: `dotnet ef migrations add`'s initial scaffold for the new `AgentUpdateState` columns matched old-to-new columns by type alone (all nullable TEXT/INTEGER), not by actual meaning — it would have renamed `LinuxRpmFileName` into `WindowsInstallerArm64FileName` and `LinuxDebFileName` into `LinuxRpmX64FileName` on any real upgrading deployment, silently relabeling an already-downloaded release's recorded asset filenames under the wrong slot (the file on disk keeps its real name either way, so this wouldn't even fail loudly — it would just offer the wrong file under the wrong label). Caught by reading the scaffolded migration before trusting it (the same "never trust a scaffolded migration's exact column-default/mapping choices" discipline this file already documents for a different EF Core gotcha) and hand-corrected so every pre-existing column renames to its own true X64 counterpart with no data loss — verified for real with a genuine upgrade-path test (a scratch DB seeded with three distinct sentinel filenames at the prior migration, then upgraded, confirming each landed in its correct new column, not scrambled). Real test coverage added throughout: a new `AgentUpdateAssetClassifierTests` covering every kind/architecture combination directly, a new `AgentUpdateServiceTests` case confirming both architectures of every kind survive a single release download side by side (the actual regression this whole fix targets), and two new `AgentUpdatesControllerTests` cases for the upload duplicate-detection fix.

## [1.3.23] - 2026-09-18

### Added

- **The published Docker image (`ghcr.io/vulture20/updatewatch2-server`) is now multi-arch (`linux/amd64` + `linux/arm64`), closing #23** — raised after a direct user question ("Wäre auch ein arm64-Release denkbar?"), whose own codebase-survey analysis found no application-level blocker: no pinned .NET `RuntimeIdentifier` anywhere in `docker/Dockerfile` (a plain, portable `dotnet publish`), and every base image already publishes an official `linux/arm64` manifest (`node:22-alpine`, `mcr.microsoft.com/dotnet/sdk:10.0`, `mcr.microsoft.com/dotnet/aspnet:10.0`), including the SQLite native binary EF Core's `SQLitePCLRaw.bundle_e_sqlite3` bundles for `linux-arm64`. `.github/workflows/docker-publish.yml`'s single `build-and-push` job was replaced with a `build` matrix job (native `ubuntu-latest` for `linux/amd64`, native `ubuntu-24.04-arm` for `linux/arm64` — both free for this public repo, and per the issue's own analysis meaningfully faster/more reliable for a .NET SDK build than `docker/setup-qemu-action` emulation) plus a `merge` job, following Docker's own documented multi-platform-images pattern: each matrix leg builds and pushes its own image *by digest only* (`push-by-digest=true`, no tag), uploads that digest as a build artifact, and the `merge` job downloads both digests and combines them into the real tags (`latest`/`vX.Y.Z`/`sha-...`) via `docker buildx imagetools create` — the one and only place any of this repo's tags actually get written, avoiding a last-matrix-leg-wins race that pushing a tag directly from each per-arch leg would otherwise create. `cache-from`/`cache-to` are scoped per platform (`scope=build-linux-amd64`/`scope=build-linux-arm64`) so the two architectures' GitHub Actions caches don't overwrite each other. The `release` job (creates the matching GitHub Release on a tag push) now depends on `merge` instead of the old `build-and-push`. Not live-verified against a real arm64 host pulling and running the image — same standing honesty caveat this project applies to every not-yet-hands-on-confirmed change; confirmed only that the workflow YAML parses and that the Dockerfile itself has no architecture-specific assumption to work around.

## [1.3.22] - 2026-09-17

### Added

- **The Info tab gained an "About UpdateWatch2" card at the very bottom, with the author, license, and links to both GitHub repositories — at the user's explicit request** ("Weitere Table in den Einstellungen unter Info ganz unten mit einem Hinweis auf den Autoren und die Lizenz. Außerdem ein Link zu beiden GitHub-Repositories."). Follows the exact same `.card`/`<dl>` layout the existing certificate cards on this tab already use — Author (Thorsten Schröpel), License (AGPL-3.0-or-later, linking to the canonical gnu.org license text), and GitHub Repositories (Server/Agent, each linking to its own `github.com/vulture20/updatewatch2-{server,agent}` repository) — matching this project's own README "License" section and the copyright line already embedded in both `.csproj` files. Purely a static informational addition — no new API field, no settings to persist.

## [1.3.21] - 2026-09-17

### Added

- **Pre-downloading pending updates is now implemented for Linux too, alongside the existing Windows toggle — at the user's explicit request** ("Setze den Pre-Download auch für Linux um. Füge dazu unter Einstellungen eine weitere Checkbox unter 'Windows-Updates vorab herunterladen' hinzu und gestalte es dort analog zur Windows-Variante."), protocol `1.5.0`, DB schema `1.1.6`. A new `AdminSettings.PreDownloadLinuxUpdatesEnabled` (default true) is a genuinely independent fleet-wide toggle, not derived from the Windows one — a fleet can run either OS, both, or neither, and an admin may want pre-downloading on one platform without the other. **UI layout was a deliberate clarifying question, not assumed**: asked whether the new Linux checkbox should live in its own card mirroring the Windows one, or share the existing card (renamed OS-neutral); the user chose the shared card. `AdminPage`'s "Pre-download Windows updates" card is now "Pre-download updates" (`admin.preDownloadUpdates.*`, replacing `admin.preDownloadWindowsUpdates.*`) with two independent checkboxes, "Proactively download pending Windows updates" and "Proactively download pending Linux updates", each submitting its own field. Every heartbeat's `alive` response now carries both `preDownloadWindowsUpdatesEnabled` and `preDownloadLinuxUpdatesEnabled` regardless of the requesting agent's own platform — the same "extra field, simply unused on the other platform" pattern the original Windows-only field already established for a Linux agent, just now symmetric in both directions.

## [1.3.20] - 2026-09-17

### Added

- **The per-agent settings dialog gained a fourth pushed setting, the alive-heartbeat interval, and the overview list gained a bulk-push settings dialog — both at the user's explicit request** ("Mache bitte auch die Client-Einstellungen für den Alive-Intervall in dem Agent-Einstellungsdialog verfügbar. Es fehlt außerdem die Möglichkeit Agent-Einstellungen bulk zu pushen. Dies könnte in der Agent-Übersicht geschehen und über ein Fenster, wie bei den Agent-Einstellungen, gelöst werden. Das Fenster könnte sich bei einem Klick auf einen Button 'Einstellungen' (neben Bestätigen, Neustart, Updates installieren,...) öffnen."), protocol `1.4.0`, DB schema `1.1.5`. `Agent.DesiredAliveIntervalMinutes`/`ActualAliveIntervalMinutes` follow the exact same bidirectionally-synced-value pattern as the three existing pushed settings — `AgentRegistrationService.ReconcilePushedSettings` now converges/adopts across all four fields under the same shared `PendingSettingsPush` flag, and `AgentSettingsDialog` gained a fourth field (defaulting to `5`, matching `AgentOptions.AliveIntervalMinutes`'s own default, with a tighter anti-typo bound — 1 to 1440 minutes — than the update-check interval's, since the heartbeat cadence itself gates install/reboot delivery, self-update offers, and certificate renewal/rotation checks, not just how often updates are searched for). **Before implementing the bulk-push dialog, the user was asked a clarifying design question** (per their own explicit request — "Stelle dazu bitte auch Rückfragen, bevor du dich an die Umsetzung machst"): whether saving should always push all four settings with identical values to every selected agent (mirroring the single-agent dialog's always-full-replace shape), or whether each field should have its own opt-in checkbox so an admin can push just one setting without forcibly overwriting the others' individually-tuned values. The user chose the per-field opt-in. This needed a genuinely different wire shape from the single-agent `PUT /api/agents/{hostname}/settings` (whose four fields stay required, unchanged): a new `POST /api/agents/settings` (`BulkUpdateAgentSettingsRequest`/`AgentService.UpdateSettingsManyAsync`) where every settings field is independently nullable — null means "leave this one untouched on every selected agent" — validated by a new `AgentSettingsValidator.IsValidBulkRequest` (rejects a request with every field null, since that would be a no-op push that still marks every selected agent pending for nothing; any field that IS provided still goes through the same per-field range checks the single-agent request uses). The new `web/src/components/AgentBulkSettingsDialog.tsx`, opened via a new "Settings" button in `AgentsListPage`'s multi-select toolbar (alongside Approve/Reboot/Install/Delete), shows all four settings with a checkbox each, starting unchecked/disabled with no "current value" hint text (unlike the single-agent dialog — the selected agents can easily already differ, so showing any one of their values next to a shared input would be misleading); Save is disabled until at least one field is checked and every checked field's value is valid. `PendingSettingsPush`'s reconciliation logic needed no special-casing for a partial push: a field the bulk push left untouched already has `Desired* == Actual*` in steady state, so it can never be what keeps the shared flag from clearing — only a field that was actually just pushed can, exactly the same convergence check the single-agent path already relied on.

## [1.3.19] - 2026-09-17

### Changed

- **`AgentsListPage`'s bulk-action buttons renamed, at the user's explicit request** ("Der Button 'Auswahl neu starten' sollte zu 'Neustart', 'Auswahl bestätigen' zu 'Bestätigen' und 'Auswahl löschen' zu 'Löschen' umbenannt werden."): DE "Auswahl bestätigen"/"Auswahl neu starten"/"Auswahl löschen" become "Bestätigen"/"Neustart"/"Löschen"; the matching EN labels ("Approve selected"/"Reboot selected"/"Delete selected") were shortened the same way ("Approve"/"Reboot"/"Delete") for consistency, since v1.3.15 already moved the selected-count out of these buttons into its own "N selected" label next to the filtered/total count — "selected" in the button text itself has been redundant since then. `installSelected`'s "Updates installieren"/"Install updates" is unchanged; the user didn't ask for it and it was never phrased with "Auswahl"/"selected" to begin with.

## [1.3.18] - 2026-09-17

### Fixed

- **Found the real reason pushed per-agent LogLevel/update-check settings never actually reached an agent's registry/config file, reported by the user directly: "Außerdem werden die Änderungen aktuell nicht in die Registry geschrieben. Egal, was ausgewählt oder eingetragen wird."** `AgentProtocolController.Alive` built its response as an inline anonymous object listing every field by hand — when `DesiredLogLevel`/`DesiredUpdateCheckIntervalMinutes`/`DesiredUpdateCheckJitterSeconds` were added to `AliveRecordResult` (v1.3.14), they were never added to that list, so they silently never appeared in the actual JSON sent to the agent at all, across three releases (v1.3.14/1.3.16/1.3.17) — `AgentRegistrationService.RecordAliveAsync`'s own logic was always correct, the value just never left the server. Confirmed the deployed agent was already on v1.0.13+ before investigating further (the version that first shipped the agent-side apply/persist logic), which ruled out the obvious "not yet upgraded" explanation and narrowed this to a real code bug. Nothing in this codebase's test suite could have caught it — `WebApplicationFactory`'s in-memory `TestServer` can't present a client certificate to reach this mTLS-gated controller's success path at all (the same limitation `UpdatesEndpointTests` already documents), and the existing service-layer tests for `RecordAliveAsync` never touched the controller's own separate response-shape mapping. Fixed by replacing the anonymous object with a new named, independently testable `Agents.AliveResponseDto` (`AliveResponseDto.FromResult`), with two new regression tests — one asserting every field maps across, and one asserting the *literal serialized JSON* contains the right property names, which is the one that would have caught this exact bug. No protocol bump — the wire shape itself was already correct on paper (`AliveRecordResult`/agent-side `AliveResponseBody`), this was purely the controller never actually emitting what it claimed to.
- **Themed the `.dialog` scrollbar added in v1.3.17 to match Nocturne instead of leaving the OS/browser default, reported by the user directly ("Jetzt wirkt er wie ein Fremdkörper.").** `scrollbar-width`/`scrollbar-color` (Firefox) plus the `::-webkit-scrollbar` family (Chromium/WebKit — neither engine honors the other's mechanism, both are needed) now style the thumb with the same tint `--color-divider` is itself built from (text color at low opacity via `color-mix`), track transparent, a visible-on-hover state — deliberately not the static `--color-neutral-*` palette, which isn't redefined under the light theme and would look wrong there.

## [1.3.17] - 2026-09-17

### Fixed

- **`AgentSettingsDialog` could overflow a short browser viewport with no way to scroll to the hidden content, making the Save button unreachable, reported by the user directly** ("Wenn der Einstellungs-Dialog des Clients nicht auf den Bildschirm passt, sind manche Einstellungen nicht sichtbar und es kann nicht gespeichert werden."). The shared `.dialog` class (used by both `AgentSettingsDialog` and `OneTimeSecretDialog`) had no height cap at all — once the Settings dialog grew a real form (Reissue/Delete plus three fields, each with its own hint text, server v1.3.14/v1.3.16), a short viewport could no longer fit the whole box, and the centered dialog simply overflowed past the top/bottom of the screen with nothing to scroll. Fixed with `max-height: min(600px, calc(100vh - 2 * var(--space-4)))` plus `overflow-y: auto` on `.dialog` — the box now caps its own height and scrolls its content internally instead of overflowing the viewport, benefiting both dialogs that use this class. Not visually verified in a real browser this session (no browser automation available in this sandbox) — this is a standard, well-established CSS pattern for a height-capped scrollable modal, but worth a real visual check on an actual small viewport before fully trusting it.

## [1.3.16] - 2026-09-17

### Fixed

- **The per-agent LogLevel/update-check settings pushed in v1.3.14 were not correctly implemented, reported by the user directly: "Die Einstellungen des Agents in Bezug auf LogLevel und dem Update-Check sind nicht korrekt umgesetzt."** Two real problems, both fixed. First, the Settings dialog's fields were initialized from `desiredLogLevel`/etc alone, which stayed blank for any agent that had never had an explicit override set — even though the agent had a perfectly real current value, just never shown ("Der aktuelle Wert soll immer im Auswahl- bzw. Textfeld stehen"). Second, and more fundamentally, the sync was strictly one-directional: a manual edit to the agent's own registry/config file was never reflected back into the server's stored value at all, so it could never surface in the dialog — the feature only ever pushed server→agent, never the reverse ("Änderungen sollen auf beiden Seiten möglich sein und direkt auf die Gegenseite gespiegelt werden").
- **Reframed the whole feature from "optional per-agent override" to "one bidirectionally-synced current value per setting"**, at the user's explicit request, confirmed via a clarifying question about exactly how to distinguish a genuine local edit from a not-yet-delivered server push before implementing (recommended approach chosen: a pending-flag, not a naive "always adopt immediately"). New `Agent.PendingSettingsPush` (bool, one shared flag for all three settings, since the Settings dialog always saves them together): set whenever an admin saves via `PUT /api/agents/{hostname}/settings`; a new `AgentRegistrationService.ReconcilePushedSettings`, run on every heartbeat, only adopts a divergent `Actual*` into `Desired*` when this flag is **false** — while `true`, a heartbeat reporting a still-different actual value is never adopted (it just keeps getting told to apply `Desired*` again, exactly "the server always wins on a race condition"), and the flag clears the moment `Actual*` matches `Desired*` for all three fields. Without this flag, a heartbeat already in flight the instant an admin saves a change (still carrying the agent's stale pre-push actual value) would otherwise immediately overwrite that very save — not a rare race, but a reliably reproducible bug on any reasonably active agent. Once not pending, any future divergent `Actual*` — including one caused by a manual registry/config-file edit — is adopted into `Desired*` immediately, which is what makes it show up in the dialog the next time it's opened. `Desired*` is also now bootstrapped from `Actual*` the very first time an agent ever reports one, so the field is never blank for an agent that has ever heartbeated in.
- The admin UI's LogLevel/interval/jitter fields no longer have a "no override" blank state at all — there's nothing left to represent, since every field always shows a real value (falling back through `desired → actual → the agent's own hardcoded default` only for a brand new agent that hasn't reported anything yet). `PUT /api/agents/{hostname}/settings`'s three fields are now required, non-nullable — `AgentSettingsValidator` updated to match. DB schema bumped to `1.1.4` for the new `PendingSettingsPush` column; no protocol bump (the agent-facing wire shapes were already correct and are unchanged — this was purely a server-side reconciliation-logic and admin-UI fix).

## [1.3.15] - 2026-09-17

### Fixed

- **`AgentDetailPage`'s "Uptime" field used the plural unit form even for a count of exactly one ("seit 1 Tagen"/"1 days" — the plural string was used unconditionally, `{{count}}` never actually chosen between forms), reported by the user directly.** `agentDetail.uptimeSince.{minute,hour,day,month,year}` now use real i18next plural keys (`_one`/`_other`) instead of one fixed string per unit — `t()`'s existing `{ count: elapsed.value }` call already resolves to the correct form automatically once the suffixed keys exist, no code change needed beyond the locale files. `formatRelativeTime`/`AgentsListPage`'s "last seen" column were already safe (backed by `Intl.RelativeTimeFormat`, which pluralizes correctly on its own) — this was specifically an `agentDetail.uptimeSince` bug, not a codebase-wide one; a full audit of every other `{{count}}` interpolation in both locale files found the rest all use this project's existing, deliberate "(s)"-suffix convention for ambiguous-count English/German sentences (e.g. "Agent(s) haben...", "ausgewählte(n) Agent(s)"), which is a stylistic compromise, not a grammar error, and was left unchanged.

### Changed

- **The agent overview's bulk-action buttons no longer show the selected count inline (e.g. "Approve selected (3)") — that count now appears once, next to the filtered/total agent count, at the user's explicit request** ("die Anzahl der markierten Einträge aus den Buttons herausgezogen und als zusätzliche Angabe neben der (angezeigten) Anzahl der Agents platziert"). `AgentsListPage`'s toolbar now reads e.g. "2 of 2 agents · 3 selected" (the "· N selected" part only rendered once at least one row is selected), and all four bulk buttons (Approve/Reboot/Install/Delete) keep a fixed label.
- **Removed the "Save" button at the bottom of Administration → Update filters — it wasn't necessary and only caused confusion, at the user's explicit request** ("Ist der Speichern-Button bei den Filtern überhaupt nötig? Falls nicht, sollte er entfernt werden, weil er nur unnötig verwirrt."). Investigation confirmed the button genuinely did nothing useful there: every field on that tab (add/edit/delete a filter) already persists immediately via its own direct API call, and `UpdateFilter`/`UpsertUpdateFilter` are a wholly separate REST resource from `AdminSettingsDto` — the removed button was `type="submit"` on the same shared `<form>` every other tab's real "Save" button submits, so clicking it on this tab actually re-submitted the unrelated general admin settings (already saved) and showed a "Saved" confirmation that had nothing to do with whatever filter action the admin had just taken. The per-row inline "Save"/"Cancel" buttons shown while editing one filter are unaffected — those are the button that actually persists a filter edit.

## [1.3.14] - 2026-09-17

### Added

- **The server can now push a per-agent LogLevel/update-check-interval/jitter override, enforced by the agent unconditionally on every heartbeat, and configurable in the agent detail page's new Settings dialog — closing a gap standing since `AgentOptions.LogLevel`'s own doc comment first admitted "pushed centrally from the server UI (not implemented yet)", and agent issue #21 — at the user's explicit request** ("LogLevel des Agents über den Server setzen - steht im Konzept - wurde aber nie umgesetzt... Änderungen am Server sollen auch auf die Agents zurückgespiegelt (Registry bzw. Configfile) werden. Änderungen an Registry bzw. Configfile sollen wiederum am Server zu sehen sein. Diese Logik soll für alle (auch spätere) Einstellungen am Server für den Agent gelten. Bei Konflikten siegt immer der Server."). Per-setting `Desired*`/`Actual*` column pairs on `Agent` (`DesiredLogLevel`/`ActualLogLevel`, `DesiredUpdateCheckIntervalMinutes`/`ActualUpdateCheckIntervalMinutes`, `DesiredUpdateCheckJitterSeconds`/`ActualUpdateCheckJitterSeconds`, DB schema `1.1.3`) — `Desired* = null` means no override, in which case the agent's own local registry/config file stays authoritative; `Actual*` is purely informational, self-reported on every heartbeat regardless of whether an override is active, so an admin can see what's really running even after a manual local edit. New `PUT /api/agents/{hostname}/settings` (`Agents.UpdateAgentSettingsRequest`, a full replace like `PUT /api/admin/settings`, validated by a new `AgentSettingsValidator`) lets an admin set or clear an override; delivery reuses the existing additive-heartbeat-field pattern (`AgentAliveRequest`/`AliveRecordResult`, protocol bumped to `1.3.0`) with no separate acknowledgement call needed, since the agent's own next heartbeat reporting a matching `Actual*` value is confirmation enough. "The server always wins on conflict" is implemented by unconditional enforcement, not a timestamp comparison — the agent overwrites its local value every heartbeat a non-null override differs from what it last reported, including a value a human just hand-edited locally.
- **The agent overview's Settings dialog (added in v1.3.13 for Reissue certificate/Delete agent) now has the actual form for this** — a LogLevel dropdown and two number fields (interval minutes, jitter seconds), each showing the agent's current actual value alongside the editable override, and a Save button (`components/AgentSettingsDialog.tsx`, now translating its own labels directly via `useTranslation` instead of taking them all as props, and taking the whole `AgentDetail`/a save callback rather than the growing list of individual primitives it would otherwise need). Leaving a field blank clears that setting's override.

## [1.3.13] - 2026-09-17

### Changed

- **`AgentDetailPage`'s "Zertifikat neu ausstellen"/"Reissue certificate" header button is now a "Einstellungen"/"Settings" button that opens a new overlay dialog, at the user's explicit request ("Kannst du in den Agent-Details den Button 'Zertifikat neu ausstellen' durch 'Einstellungen' ersetzen und beim Klick auf 'Einstellungen' ein Fenster öffnen... dort den Button 'Zertifikat neu ausstellen' mit einer kleinen Erklärung wieder einbauen... auch den Button 'Agent löschen' verschieben. Ebenfalls mit einer kleinen Erklärung.").** New `components/AgentSettingsDialog.tsx` reuses the `.dialog-backdrop`/`.dialog` overlay pattern `OneTimeSecretDialog` already established (backdrop/Escape to close) — this app's second modal, still no shared library or hook, since the Escape-handling effect is only a few lines. Both "Zertifikat neu ausstellen" and "Agent löschen" moved out of the header's own button row into this dialog, each now with a short explanatory sentence (`.field-hint`) describing what it does and when to use it. The "Settings" button itself is always shown, regardless of approval status — unlike the old reissue button (approved agents only), since Delete must stay reachable for an unapproved agent too, and it's now the dialog's only entry point; inside the dialog, the reissue section itself still only renders for an approved agent. Confirming a reissue closes the settings dialog first, rather than stacking it underneath the existing one-time-token dialog. Purely a web-side restructuring — no new/changed API endpoint, no protocol/schema bump. This dialog is deliberately meant to grow: the user's own framing ("Hier sollen später auch weitere Einstellungen wie der Alive-Intervall konfigurierbar sein") marks it as the future home for a per-agent settings surface, not just these two relocated actions — nothing beyond Reissue/Delete is implemented yet.

## [1.3.12] - 2026-09-17

### Added

- **A "select all" checkbox in the agent overview table's header selects/deselects every currently visible (filtered) row at once, at the user's explicit request ("Es fehlt noch ein Feld um alle Zeilen in der Agents-Übersicht auszuwählen.").** Mirrors `AgentDetailPage`'s own existing `selectAllUpdates` behavior for its updates table: acting only on `filteredAndSorted` (the rows an active filter is currently showing), not the whole fleet, so narrowing the list first and then selecting all only touches what's visible — a subsequently cleared filter leaves a previously-hidden row's own selection state untouched. Shows an indeterminate (dash) state when some but not all visible rows are selected, matching standard checkbox convention.

### Changed

- **The three new bulk-action buttons added in v1.3.11 (Reboot/Install/Delete) now use the same accent-outline styling as "Auswahl bestätigen"/"Approve selected", at the user's explicit request ("Die neuen Buttons in der Agent-Übersicht sehen anders als der Button 'Auswahl bestätigen' aus. Bitte an 'Auswahl bestätigen' angleichen.").** All four bulk-action buttons in `AgentsListPage`'s toolbar now share `.btn-accent` — a deliberate exception to Nocturne's own general "`.btn-accent` reserved for the single highest-intent action on a page" framing (see CLAUDE.md's design-system paragraph), scoped specifically to this one row of mutually-exclusive bulk actions on a multi-select, not a change to that framing anywhere else in the app (Login's Sign in, a single-agent Approve, etc. are unaffected).

## [1.3.11] - 2026-09-17

### Added

- **The agent overview list can now bulk-delete, bulk-install-updates, and bulk-reboot several selected agents at once, alongside the existing bulk-approve, at the user's explicit request ("In der Agent-Übersicht hätte ich gern zusätzlich zur Bulk-Bestätigung auch die Möglichkeit mehrere Agents zu löschen, Updates zu installieren und sie neu zu starten. Dazu bitte auch entsprechende Buttons neben 'Auswahl bestätigen' ergänzen.").** Three new sibling routes on `AgentsController` mirror the existing `POST /api/agents/approve`: `POST /api/agents/delete` (`Agents.BulkDeleteRequest`/`BulkDeleteResult`, delegating to a new `IAgentService.DeleteManyAsync`), `POST /api/agents/install` (`Updates.BulkInstallRequest`/`BulkInstallResult`, delegating to a new `IUpdateService.TriggerInstallManyAsync` — `AgentsController` now also injects `IUpdateService` purely for this one route, since install itself is otherwise owned by `UpdatesController`), and `POST /api/agents/reboot` (`Agents.BulkRebootRequest`/`BulkRebootResult`, delegating to a new `IAgentService.TriggerRebootManyAsync`). Each reuses the exact per-agent mechanism its single-agent counterpart already uses (immediate row removal + cascade for delete; `PendingRebootRequestedAt`/`PendingInstallRequestedAt` set for pickup on the agent's next heartbeat for reboot/install) in a loop over the selected hostnames, and writes one audit log entry per bulk call listing every affected hostname, matching `ApproveManyAsync`'s existing convention — no new wire field reaches any agent and no protocol/DB-schema bump was needed, since every underlying delivery mechanism already existed. Bulk install always installs everything currently pending for each selected agent — there is no cross-agent equivalent of the single-agent selective-install checkboxes, since that selection is inherently server-only `UpdateItem` primary keys with no meaning across different agents' own update lists. Web: three new buttons next to "Auswahl bestätigen"/"Approve selected" in `AgentsListPage`'s toolbar (Reboot, Install, Delete, in that order, matching `AgentDetailPage`'s own single-agent button ordering) — all disabled until at least one agent is selected, install fires with no confirmation dialog (matching the single-agent trigger), delete/reboot both confirm via the native `window.confirm()` dialog first (matching `AgentDetailPage`'s own destructive-action precedent, including this project's established "no `.btn-danger` treatment, plain secondary button relying on the confirm dialog for friction" Nocturne convention).

## [1.3.10] - 2026-09-17

### Added

- **`Agent.RebootRequired` now also updates from every agent heartbeat (~5 min default), not only from the coarser periodic full-update-check report (~4h default) — the server-side half of the agent's new, more-frequent reboot-required check, at the user's explicit request.** `Agents/AgentRegistrationDtos.AgentAliveRequest` gains an additive `RebootRequired` field (protocol bumped to `1.2.0`); `AgentRegistrationService.RecordAliveAsync` sets `agent.RebootRequired = request.RebootRequired ?? agent.RebootRequired`, the same null-coalescing-fallback shape every other self-reported-metadata field on that request already uses — so a null (the agent's own check failed or hasn't run this tick) never overwrites the last-known-good value with a false negative. No DB schema change: this is a second writer to the already-existing `Agent.RebootRequired` column, not a new one. The existing, full-update-check-driven write path (`Updates/UpdateService.ReportUpdatesAsync`) is unchanged and runs alongside this unmodified — both read the same real OS state on the agent, so they can only ever report different snapshots in time, not disagree in any meaningful sense.

## [1.3.9] - 2026-09-16

### Added

- **A new admin setting, "Pre-download Windows updates" (Settings → General), lets a Windows agent proactively download pending Windows Updates ahead of an actual install trigger — at the user's explicit request ("Gibt es die Möglichkeit die Windows-Updates im Vorfeld schon herunterladen zu lassen? Am besten über eine Option in den Einstellungen ein- und ausschaltbar machen.").** `AdminSettings.PreDownloadWindowsUpdatesEnabled` (default true), surfaced to agents as an additive `preDownloadWindowsUpdatesEnabled` field on the `alive` heartbeat response — the same pattern `installRequested`/`certificateRotationPending`/`rebootRequested` already established, chosen after confirming this codebase has no existing mechanism to push a raw admin-settings *value* down to a running agent (only a "server computes an offer, agent reacts to presence/absence" pattern, e.g. `agentUpdateAvailable`). Protocol version bumped to `1.1.0`, DB schema to `1.1.2`. Windows-only for its first version, per explicit user decision — a Linux agent receives the same field but its own pre-download step is currently a no-op (see the agent repo's CHANGELOG for the agent-side half of this feature).

## [1.3.8] - 2026-09-16

### Added

- **The agents overview list now has a live, type-as-you-go hostname search, at the user's explicit request ("Es fehlt noch eine Suchfunktion für Agents... Diese sollte bereits beim Tippen die dort angezeigten Clients filtern.").** A new `Filters['search']` field, filtered case-insensitively against `agent.hostname`, applied purely client-side against the already-loaded agent list — no round trip per keystroke, consistent with every other filter already on this page (OS type, OS, status, reboot, pending updates, certificate warning, online status), and included automatically by the existing generic "Clear filters" logic and active-filter indicator.

## [1.3.7] - 2026-09-16 — DB schema `1.1.1`

### Added

- **The agent detail page's Identity card now shows when this agent last actually reported the result of an update check, at the user's explicit request ("Bei den Client-Details sollte unter 'Identität' noch festgehalten werden, wann zuletzt nach Updates gesucht wurde.").** New `Agent.LastUpdateCheckAt` (DB schema `1.1.1`), set by `UpdateService.ReportUpdatesAsync` on every successful report — its own timestamp, not derived from the reported items, so an agent with genuinely zero pending updates still updates it (an empty report is still a real check that just happened). Deliberately distinct from `LastAliveAt`: a heartbeat happens on its own, much shorter cadence and carries no update information at all, while this only moves on the separate, jittered update-check cadence. Surfaced via `AgentDetailDto.LastUpdateCheckAt`, shown on the Identity card right below "Last alive", formatted identically (localized date/time, or "Never").

## [1.3.6] - 2026-09-16

### Added

- **New regression test confirming behavior a user asked about directly: does an update immediately removed by a successful install-ack (v1.3.4) get re-added if a later real report still finds it pending?** Yes — `RemoveJustInstalledItemsAsync`'s removal has no way to distinguish "genuinely gone" from "not yet confirmed gone", so the next real `ReportUpdatesAsync` call is what's actually authoritative; not finding a matching existing row (since it was just deleted) means the update is added back exactly like a freshly-discovered one, with `DetectedAt` reset to the re-detection time rather than the original one. No behavior change — this confirms the already-intended self-correcting design with an actual test rather than only a doc comment.

## [1.3.5] - 2026-09-16

### Fixed

- **The agents overview list's "Status" filter dropdown still offered the old "Approved"/"Pending" options after the status badge itself was changed to show "Unapproved"/"Updates"/"Reboot" instead — an oversight from that change, caught by the user directly.** The filter now offers the same three states the badge can actually show (`Filters['status']`: `'unapproved' | 'installing' | 'rebooting'`, replacing `'approved' | 'pending'`), filtering on the same `pendingInstallRequestedAt`/`pendingRebootRequestedAt` fields the badge itself reads rather than a since-removed notion of "approved" as a filterable status.

## [1.3.4] - 2026-09-16

### Added

- **Audit log entries for `admin.settings.updated` now carry a field-level "before -> after" diff, at the user's explicit request ("Im Audit-Log steht oft nur 'admin.settings.updated' und weitere Details fehlen. Die könnten noch den Unterschied von vorher zu nachher widerspiegeln.").** New `AdminSettingsDiffFormatter.Format`, reflection-based over `AdminSettingsDto` so a newly added settings field is covered automatically with no matching edit needed here. `AdminController.Update` now captures the settings snapshot before calling `IAdminSettingsStore.UpdateAsync` and passes the diff as the audit entry's `Details`; null (no entry text) when a save genuinely changed nothing. Safe to log every differing field's before/after value as-is — `AdminSettingsDto` never carries a raw secret value in the first place (`SmtpPassword`/`AdBindPassword`/`GitHubToken` are represented only as `*Set` booleans on this DTO), so there is nothing here that needed redacting.
- **The agents overview list's status column no longer shows a redundant "Approved" badge for an approved, idle agent, and now shows an "Updates"/"Reboot" activity badge while an admin-triggered install or reboot is pending — at the user's explicit request ("Der Status 'Bestätigt' ist eigentlich unnötig. Hier sollte nur 'Unbestätigt' stehen und am besten noch der Status, ob gerade Updates installiert ('Updates') oder ein Neustart durchgeführt wird ('Neustart').").** `AgentListItemDto` gained `PendingInstallRequestedAt`/`PendingRebootRequestedAt` (mirroring the fields `AgentDetailDto` already had) so the overview list can show this without a per-agent round trip. An unapproved agent still shows "Unapproved"/"Unbestätigt"; an approved agent shows "Reboot" (taking priority, since a reboot ends the process a pending install's own follow-up report would otherwise still be running in) or "Updates" while one of those is pending, and no badge at all once idle.

## [1.3.3] - 2026-09-16

### Fixed

- **A successful install used to leave the just-installed updates showing as still pending until the agent's own next report caught up — reported by the user directly ("Wenn die Updates erfolgreich installiert wurden, sollten sie umgehend aus der Liste der anstehenden Updates entfernt werden").** `UpdateService.AcknowledgeInstallAsync` only ever cleared `Agent.PendingInstallRequestedAt`/`PendingInstallUpdateIds` and recorded the outcome — it relied entirely on the agent's own immediate `CheckAndReportNowAsync` follow-up call (agent v0.13.1) to notice the install and re-report a shorter pending list, which can be delayed by a transient network failure on that call or by the OS-level update checker not yet reflecting the just-finished install at that exact moment. A Succeeded ack now also removes the just-installed `UpdateItem` rows immediately and deterministically, reading `PendingInstallUpdateIds` before clearing it (a null value — "everything pending was requested" — removes every one of that agent's items; a stored PackageId list removes only those). Purely a fast, best-effort layer on top of the agent's own subsequent real report, not a replacement for it — if this ever removes something that turns out to still genuinely be pending, that next report re-adds it, the same self-correcting pattern already used elsewhere in this codebase (e.g. `certificateRotationPending`). A Failed outcome is unaffected — nothing is removed, matching the existing "no update was actually installed" behavior.
- **The login page didn't focus the username field on load, at the user's explicit request.** Added `autoFocus` to `LoginPage`'s username input.
- **The agents overview table's "last seen" column could wrap onto a second line for a relative-time value like "in dieser Minute"/"this minute" (longer than most of the column's other values), growing that row's height and shifting the whole table — reported by the user directly.** Fixed with a new `.nowrap` utility class (`white-space: nowrap`) applied to that cell.
- **The README/README.de "Project status" badge and callout still said "v1.0" — at the user's explicit request, both now say "Stable"/"Stabil"** (mirrored in the agent repo's own README pair too, which carries the identical badge).

### Added

- **New unauthenticated `GET /api/update-count` endpoint, at the user's explicit request, for displaying the fleet-wide pending-update count on an external device with no session of its own (e.g. a Stream Deck button).** Deliberately anonymous (no `[Authorize]`, matching `HealthController`'s model) and deliberately not marked `[AllowedOnAgentPort]`, so it's reachable only on the browser-facing port (8795), never the agent-facing mTLS port (8796). Returns just the one aggregate number (`{"pendingUpdateCount": N}`), never a per-agent breakdown, so an unauthenticated caller can't learn anything about individual agents. Reuses the exact same filtered-count logic `AgentService`'s per-agent counts already use (`UpdateFilterMatcher.IsExcluded` against the live admin filter list, not the raw unfiltered per-report column) via a new `IAgentService.GetTotalPendingUpdateCountAsync`, so this number always agrees with what the admin UI itself shows as pending, filters included.

## [1.3.1] - 2026-09-13

### Fixed

- **A full, non-diff-scoped security review found and fixed a real resource-exhaustion gap in anonymous agent registration.** `POST /api/agents/{hostname}/register` is deliberately anonymous (an agent has no client certificate yet at first contact) — `HostnameValidator` already bounds `hostname` itself (253 chars, RFC 1123), but the body's `DnsName`/`OperatingSystem`/`IpAddress`/`AgentVersion` fields had no length limit anywhere (not the DTO, not the `Agent` DB column, not a route-specific request-size cap), the only ceiling being Kestrel's default ~28.6 MB request body size. An unauthenticated network caller reaching the agent-facing port could register unboundedly many distinct hostnames, each carrying near-that-limit text fields, growing the database and flooding the admin's pending-approval queue with no rate limit. Fixed with a new `Agents/AgentMetadataValidator` — registration rejects the whole call outright when a field exceeds a generous-but-finite cap (mirroring `HostnameValidator`'s own treatment, since this is the one anonymous entry point); the equivalent fields on the `alive` heartbeat (`AgentRegistrationService.RecordAliveAsync`) are truncated rather than rejected, since that path already requires an approved, mTLS-authenticated agent — a much smaller, already-trusted population — and this is purely display metadata.

### Changed

- Also reviewed, and deliberately not changed: `AgentUpdates/GitHubReleaseClient.DownloadAssetAsync` fetches a release asset's `browser_download_url` verbatim from the GitHub API with no host allowlist. Exploitability requires compromising the pinned upstream repository (`vulture20/updatewatch2-agent`) itself, at which point an attacker already controls the release content an allowlist wouldn't meaningfully constrain — no user-supplied input reaches this URL, so this is an accepted, already-implicit trust boundary rather than an actionable finding.

## [1.3.0] - 2026-09-13

### Added

- **The SMTP warning banner now reflects real mail-server reachability, not just whether it's configured — closes updatewatch2-server#12 ("wire the SMTP warning banner to the real reachability check, not just 'is it configured'").** CLAUDE.md's own requirement ("a red warning is shown to logged-in admins if the mail server is unreachable or misconfigured") only had its "misconfigured" half implemented before this; `IEmailNotificationService.IsHealthyAsync` already existed but nothing exposed its result to the frontend. A new `SmtpHealthCheckWorker` (this project's seventh server-side `BackgroundService`) refreshes a small in-memory `ISmtpHealthCache` from `IsHealthyAsync` every 5 minutes, and a new `GET /api/admin/notifications/smtp-health` (`NotificationsController`) reads that cache rather than ever performing the live TCP probe itself. Deliberately cached, not checked on every request — the explicit trade-off the issue itself called out as "worth deciding", chosen here over adding a real SMTP round trip to every settings-page load/poll. `web/`'s `SmtpWarningBanner` now combines two independent signals: `smtpConfigured` (from `GET /api/admin/settings`, still updated instantly by `AdminPage`'s own settings-save event — server v0.29.1's fix keeps working unchanged) and the new `smtpHealthy` (from the cached endpoint above, polled independently every 15 seconds, the same cadence `CertificateRejectionBanner`/`AgentUpdateErrorBanner` already use) — shown whenever *either* says something's wrong, matching CLAUDE.md's wording exactly rather than only the first half of it. The independent 15-second poll (rather than only reacting to a settings save) is what lets an outage that starts *while* an admin is already logged in and looking at another page become visible without a manual reload. No protocol or DB schema bump — purely an admin-facing addition, nothing on the agent-facing wire changed.

## [1.2.0] - 2026-09-13

### Added

- **Admin-facing API error messages are now translatable, closing updatewatch2-server#17 ("Admin-facing API error messages are free-text English with no translation mechanism").** Every static, human-authored admin-facing failure response — form validation on `PUT /api/admin/settings`, login/password-change failures, the manual agent-update upload's validation, update-filter validation, CA-rotation guard errors — now carries an additive `errorCode` (single-message responses) or a structured `errors: [{ code, message, detail? }]` list (validation responses, replacing what used to be a bare `string[]`) alongside the exact free-text `message` this API always returned. `errorCode`/`errors[].code` is one of a new `Api.ApiErrorCode` enum (serialized as its name, matching every other enum-like wire value in this codebase), and `web/src/api/client.ts` translates it via react-i18next's `t()` against new `errors.*` keys in both locale files, falling back to the raw server-supplied text — unchanged from before this existed — for any code an older or newer frontend build doesn't recognize. Deliberately scoped to static failure reasons only, per the issue's own "worth deciding" note: two genuinely dynamic-content cases (a live SMTP exception's own message; a regex engine's own parse-error message for whatever pattern an admin just typed) keep a code but carry the dynamic fragment in a new, additive `errorDetail`/`detail` field for `{{detail}}`-style interpolation instead — translating an arbitrary upstream library's own English text would be its own, out-of-scope project. An agent-facing route (mutual-TLS, e.g. `AgentProtocolController.Register`/`.Renew`) is out of scope entirely — its failure text is read by the agent's own logs, never by a browser, so there's nothing for react-i18next to translate there. No protocol or DB schema bump — this only changes the admin-facing HTTP API's error bodies, never anything on the agent-facing wire.

## [1.1.0] - 2026-09-13

### Added

- **Agent offline detection, at the user's explicit request ("Warnschwelle festlegen, ab wann ein Client als offline gilt und diesen dann markieren und eventuell per Mail informieren.").** A new admin-configurable offline threshold (`AgentOfflineOptions.ThresholdMinutes`, Settings → General → Offline detection, default 15 minutes) marks an agent as offline once its last heartbeat is older than that — computed live on every `GET /api/agents`/`GET /api/agents/{hostname}` call (`AgentService.IsOffline`), never a stored/stale flag, the same "never trust a periodically-updated flag for display" precedent this project already applies to `PendingUpdateCount`'s filtered view. `AgentListItemDto`/`AgentDetailDto` both gained `IsOffline`; the admin UI marks a flagged hostname with a small grey `OfflineIcon` next to its name (both the agent list and detail page), and the agent list gained a new "Online status" filter (all/online/offline) alongside the existing OS/status/reboot/updates/certificate-warning filters.
- A new `AgentOfflineNotificationWorker` (this project's sixth server-side `BackgroundService`) emails about the transition itself — "went offline" and "back online" — each independently toggleable (`AgentOfflineOptions.OfflineNotificationEnabled`/`OnlineRecoveryNotificationEnabled`, Settings → Notifications, both default **on**, per the user's explicit request for the recovery email to have its own separate checkbox: "Auch [die Wiederherstellungs-Mail], aber über eine Extra-Checkbox ebenfalls schaltbar machen"). Edge-triggered per agent (`Agent.OfflineCrossed`/`OfflineNotifiedAt`, mirroring `UpdateThresholdNotificationState`'s own `*Crossed` pattern) — fires at most once per episode, retries on the next check (every 5 minutes by default) if sending fails, and is skipped (but still audit-logged as `agent.offline.detected`/`agent.offline.recovered`) when the relevant checkbox is off or no notification recipient is configured. An agent that has never sent a single heartbeat is excluded from email consideration (nothing to notify about — it never was "online" to begin with) but still shows the offline icon in the UI. Bilingual (EN/DE) HTML email via the existing `EmailNotificationService.SendNotificationAsync` primitive, matching every other automated notification this project sends.
- **A real bug in this worker's first draft, caught by its own test suite before ever shipping**: gating "is a notification pending" on `Agent.OfflineNotifiedAt` being null made a brand-new agent that had never gone offline at all indistinguishable from one genuinely pending a "back online" notification (both start with `OfflineCrossed = false`, `OfflineNotifiedAt = null`) — every healthy, never-offline agent got a bogus "back online" email on the very first check. Fixed by comparing the freshly computed live offline state directly against `OfflineCrossed` (which now means "the last state this worker actually finished handling", not merely "is offline right now") — a mismatch is exactly what "pending" means, and it persists untouched across ticks until a send attempt actually succeeds or is skipped, which is also what makes a failed send retry cleanly on the next tick. `OfflineNotifiedAt` is now purely informational and gates nothing.
- DB schema bumped to `1.1.0`: `Agents.OfflineCrossed`/`OfflineNotifiedAt` (worker-internal bookkeeping only, never read for display) and `AdminSettings.AgentOfflineThresholdMinutes`/`AgentOfflineNotificationEnabled`/`AgentOnlineRecoveryNotificationEnabled` — the migration's scaffolded `AddColumn` defaults were hand-fixed to match `AgentOfflineOptions`' real defaults (15/true/true), the same recurring `dotnet ef` gap CLAUDE.md already documents.

## [1.0.0] - 2026-09-13

### Changed

- **Ends the beta phase, at the user's explicit request ("Ich würde die Beta-Phase gern beenden. Kannst du alle Versionen auf v1.0.0 setzen?").** All four of this project's independent version numbers move to `1.0.0` together as a deliberate, one-time milestone — server version (this file, `VERSION`, `AppVersion.cs`), transfer-protocol version (`Protocol/ProtocolVersion.cs`), and DB schema version (`Db/SchemaVersion.cs`, no accompanying migration — no schema actually changed, this is purely the version label) — matched by the agent repo's own agent and protocol versions moving to `1.0.0` too. This deliberately overrides CLAUDE.md's normal "these evolve independently, a server release doesn't imply a protocol or schema bump" rule for this one occasion.
- README.md/README.de.md (this repo and the agent repo) no longer describe the project as "Beta" — the status badge and callout now read `v1.0`/`✅`. The substantive caveats those callouts already carried (real Windows Update installation, the RPM/dnf update path, and the Windows installer's install/uninstall behavior not yet verified against a real target host) are unchanged and still called out explicitly — reaching `1.0.0` is a versioning/maturity milestone, not a claim that those specific, honestly-flagged gaps have been closed.

## [0.30.20] - 2026-09-13

### Fixed

**A full, non-diff-scoped security review (both this repo and the agent repo) surfaced four real, previously-unfixed vulnerabilities, all closed in this release. Every finding was independently re-verified by reading the actual code before being fixed (one initial candidate — insufficient LDAPS/StartTLS certificate validation — was investigated and NOT included here, since its real-world exploitability turned out to depend on the host's own OpenLDAP defaults rather than being confidently exploitable on its own).**

- **Path traversal in the agent auto-update download path (High) — a malicious/compromised release on the pinned upstream GitHub repo could write files outside the intended storage directory, both on this server and on every connected agent.** `AgentUpdates/AgentUpdateService.DownloadAssetsAsync` combined a GitHub release asset's raw, unvalidated `name` field directly into a filesystem path (`Path.Combine(storage.Path, asset.Name)`) — unlike the manual-upload path, which already sanitized via `Path.GetFileName`. Fixed by applying the identical sanitization there too, rejecting (with a warning log) any asset whose name isn't already a bare filename. The agent side of the same chain (`SelfUpdate/AgentSelfUpdateService.ApplyAsync` in the agent repo) had its own independent bug compounding this: `Uri.UnescapeDataString(asset.DownloadUrl.Split('/').Last())` ran the split *before* decoding, so a percent-encoded `/`/`..` inside a filename survived the split and only became a real path separator afterward, and `Path.Combine` discards the staging directory entirely when the result turns out to be rooted — fixed by sanitizing the decoded filename with `Path.GetFileName` and verifying the resolved path stays inside the staging directory before ever downloading to it. Also added, defense in depth: the agent now refuses a `DownloadUrl` naming a foreign host (a legitimate offer is always a same-server-relative path) — distinguishing that from .NET's own quirk of parsing a bare `/api/...` path as an absolute `file://` URI with an empty `Host`, which a naive `Uri.IsAbsoluteUri` check would have wrongly rejected.
- **RDN injection into an issued agent certificate's Subject via an unvalidated hostname (Medium).** The anonymous, unauthenticated `POST /api/agents/{hostname}/register` endpoint had no format validation on `hostname` at all, and `Certificates/InternalCertificateAuthority.CreateLeaf` built the issued leaf's Subject via naive string interpolation (`$"CN={hostname}"`) into an `X500DistinguishedName`, which parses its input per RFC 2253 — a hostname containing a comma or equals sign (both legal, unencoded, in a URL path segment) gets parsed as additional RDNs rather than literal CN text. Not an authentication bypass (agent identity is keyed by DB-stored SHA-256 thumbprint, never a parsed certificate field), but it let an anonymous caller inject arbitrary attribute/value pairs into a certificate this server's own CA vouches for, corrupting displayed certificate metadata. Fixed with a new `Agents/HostnameValidator` (RFC 1123 hostname/FQDN pattern), rejecting anything else in `AgentRegistrationService.RegisterAsync` before an `Agent` row is ever created — `Hostname` is set exactly once, at that point, so validating only there is sufficient.
- **`X-Forwarded-For` spoofing bypassed the brute-force lockout's `UPDATEWATCH2_TRUSTEDIP` exemption (Medium).** `ForwardedHeadersOptions` deliberately clears `KnownProxies`/`KnownIPNetworks` so `X-Forwarded-Proto` works out of the box behind any reverse proxy with no per-deployment configuration (see that block's own long-standing comment) — but ASP.NET Core ties both `X-Forwarded-For` and `X-Forwarded-Proto` to the same allow-list, so clearing it for Proto's sake also let any directly-connecting caller set `X-Forwarded-For` to whatever address they wanted, including one inside the configured trusted range, bypassing account lockout on brute-forced login attempts entirely. Fixed with a new `Auth/RealRemoteIpAccessor`: a middleware registered *before* `UseForwardedHeaders()` captures the genuine, un-spoofable TCP peer address into `HttpContext.Items`, and `AuthController.Login` now keys the brute-force/trusted-IP decision off that captured value instead of the (forwarding-processed) `Connection.RemoteIpAddress` — audit-log entries still use the normal, forwarded value, since which IP a log line displays isn't a security decision the way the lockout exemption is. Live-verified with a real end-to-end test (not just reasoning about it): confirmed a bug in the first version of this fix, where the accessor incorrectly fell back to reading `Connection.RemoteIpAddress`'s *current* (already-forwarded) value whenever the pre-forwarding capture happened to be null — which it always is under `WebApplicationFactory`'s in-memory `TestServer`, so the very regression test written for this fix caught it immediately by still failing after the "fix" was first applied.
- **No server-side revocation of the admin cookie session (High).** Authentication was a pure, stateless, Data-Protection-encrypted cookie ticket with no server-side revocation at all: `POST /api/auth/logout` only ever cleared the *calling* browser's own cookie (a copy of the same raw cookie value used elsewhere — a stolen cookie, a leaked browser profile — kept authenticating indefinitely, and with `SlidingExpiration=true` a periodically-reused stolen ticket never truly expired), and changing the local admin password didn't invalidate any ticket already issued under the old one either. Fixed with a new per-username `Db.Entities.SessionInvalidation` table (DB schema `0.16.3`) and `Auth/ISessionInvalidationService`: `AuthController.Login` now embeds an issued-at claim in every ticket, `Program.cs`'s `CookieAuthenticationEvents.OnValidatePrincipal` checks it against the table on every request (rejecting and signing out anything issued before the account's last logout/password-change — including a ticket with no such claim at all, treated as invalid rather than exempt), and both `AuthController.Logout` and `Auth/AdminAccountService.ChangePasswordAsync`/`ResetPasswordFromEnvironmentIfConfiguredAsync` now call `InvalidateAsync` on success. A password change now also ends the session that made the change itself — the same "you'll need to log in again" expectation most change-password flows set, not just other, stale sessions. Live-verified with two real end-to-end tests: log in, copy the raw `Set-Cookie` value into a second, independent `HttpClient` (simulating a stolen cookie used elsewhere), confirm it authenticates, then confirm it stops authenticating immediately after logout (one test) or a password change (another) from the *original* client — proving the revocation is genuinely server-side, not just clearing the original browser's own cookie.

## [0.30.19] - 2026-09-12

### Changed

- **Every automated notification email (both `CertificateExpiryWorker` emails and both `UpdateThresholdNotificationWorker` emails) is now a branded, bilingual HTML email in the app's own "Nocturne" design, with the UpdateWatch2 logo embedded inline and, when configured, a link back to the instance — at the user's explicit request, refined over two follow-ups ("Sie sollen als HTML-Mails und im aktuellen App-Design verschickt werden. Auch das App-Logo sollte für den Wiedererkennungswert zu sehen sein." then "Füge in jede Mail bitte noch einen Link zu der Instanz hinzu. Außerdem sollten die Mails auf Deutsch und Englisch sein.").**
- New `Notifications/EmailTemplate.BuildHtml` renders a single-column, table-based, all-inline-styles HTML document (the format email clients actually render reliably) hand-mirroring `web/src/theme/tokens.css`'s real color tokens (`--color-bg`/`--color-surface`/`--color-text`/`--color-accent`/`--color-divider`) rather than trying to share CSS with the frontend build, which isn't practical for an inbox. Both `color-scheme`/`supported-color-schemes` meta tags are set to opt the message out of Gmail's/Outlook.com's automatic dark-mode re-coloring — every color here is authored explicitly and deliberately dark, not left to a client's own dark-mode heuristics to get right.
- The logo is the exact same `web/src/assets/logo.svg` the in-app header already uses, rendered once via `rsvg-convert` (matching this repo's own documented "not ImageMagick's SVG delegate — it flattens gradients to grayscale" rule for asset regeneration) to a 128×128 PNG, embedded into the compiled server binary as an `<EmbeddedResource>` (`Resources/Email/logo.png`, loaded via `Assembly.GetManifestResourceStream` — guarantees it's present regardless of the container's working directory, the same class of gap `Certs:Path`/`AgentUpdates:Path` already document elsewhere) and attached to each outgoing message as a `cid:`-referenced `LinkedResource`, not a remote-hosted `<img>` — so it still renders for a recipient whose mail client blocks remote images by default (the common case for a first-time sender).
- Every message keeps the original plain-text body as `MailMessage.Body` (now carrying both languages plus the instance link, see below) alongside the new HTML `AlternateView`, so a client with no HTML support still gets useful text. `EmailNotificationService.BuildMessage` — the one place both `SendTestEmailAsync` and `SendNotificationAsync` now build the actual message — is `public static` specifically so it can be unit-tested without a real SMTP server; the test-mail button got the same branded treatment for consistency, since it exercises the identical send path.
- **Found and fixed a pre-existing, unrelated flaky test while verifying this change didn't break anything**: `CertificateExpiryWorkerTests.Sends_no_email_when_CertificateExpiryNotificationsEnabled_is_off_even_with_a_recipient_configured` polled `RenewCallCount` (incremented synchronously, well before the tick's own async audit-log writes necessarily complete) and then immediately called `StopAsync`, racing the still-in-flight audit-log write against `StopAsync`'s own cancellation of the shared token its `SaveChangesAsync` call observes — reliably reproducible running the test in isolation (deterministic failure, not occasional), even though it happened to pass as part of a full suite run's greater thread-pool contention. Fixed by polling for the actual audit log entries instead, the identical fix this same session also applied to `UpdateThresholdNotificationWorkerTests` for the same race shape.

### Added

- New `AdminSettings.InstanceUrl` (DB schema `0.16.2`, Notifications tab, validated as an absolute `http://`/`https://` URL) — the externally-reachable base URL of this instance. `EmailTemplate.BuildHtml` renders it as an outlined accent button ("Open UpdateWatch2 · UpdateWatch2 öffnen →", matching the in-app `.btn-accent` outline style) below the email's body, omitted entirely (not a dead link) when unset — the same "unset means the feature just doesn't add anything, everything else still works" precedent `NotificationRecipientAddress` already established. Deliberately a separate admin-entered setting rather than guessed from `UPDATEWATCH2_SERVER_HOSTNAME`/`Kestrel:HttpPort`: this codebase already documents that it can't know whether a reverse proxy in front terminates TLS, so guessing a scheme/hostname could easily produce a broken link.
- Every automated notification (both `CertificateExpiryWorker` emails, both `UpdateThresholdNotificationWorker` emails, and the test-mail button) is now bilingual — `IEmailNotificationService.SendNotificationAsync` gained `subjectDe`/`bodyDe` parameters alongside the existing English ones, with German translations added at each of the four real call sites. There's no per-recipient language preference to read for a single configured mailbox, so rather than guessing, `EmailTemplate.BuildHtml` renders both languages stacked, each under a small "EN"/"DE" label, separated by a divider — the same "show both, don't pick one" reasoning the app's own bilingual UI already follows. The mail's actual `Subject` header stays the English text unchanged (existing tests already assert against that exact English substring); German only ever appears as that language section's own heading inside the body. The plain-text `MailMessage.Body` fallback also gained both languages (English, a `---` rule, German, then the instance URL if configured) for a client with no HTML support.
- Switched the HTML template's own text encoding from `System.Net.WebUtility.HtmlEncode` to a small custom encoder that only escapes the five HTML-significant characters (`&`, `<`, `>`, `"`). Found while adding the German text: `WebUtility.HtmlEncode` also converts every non-ASCII character to a numeric character reference (confirmed by hand — "Ü" becomes `&#220;`), which is harmless to a real mail client but made the generated HTML source needlessly hard to read/test for zero rendering benefit, given the document already declares `<meta charset="utf-8">` and is sent as UTF-8.
- Real test coverage in `EmailTemplateTests`/`EmailNotificationServiceTests`: the plain-text fallback body, the single HTML `AlternateView` with the logo as a linked resource, HTML-encoding of caller-supplied text (so a notification body can never break out of the template markup), the `cid:` logo reference (not a remote URL), the dark-mode opt-out meta tags, paragraph/line-break rendering, the embedded logo resource actually loading and being a real PNG, both language sections rendering, the link button appearing/being omitted based on `InstanceUrl`, and the instance URL being HTML-encoded in the `href` attribute; a new `AdminPage` web test for editing/saving the instance URL field.

## [0.30.17] - 2026-09-12

### Added

- **Implemented the update-notification thresholds (updatewatch2-server#18) — CLAUDE.md had long flagged this as "not yet implemented": nothing periodically checked the admin-configured thresholds and sent a real notification.** New `Notifications/UpdateThresholdNotificationWorker` — this project's fifth server-side `BackgroundService`, after `AgentUpdateCheckWorker`/`AuditLogRetentionWorker`/`CertificateExpiryWorker`/`DatabaseVacuumWorker` — checks immediately on startup and then every hour, evaluating the same OR-combined pair of independent conditions CLAUDE.md already described: the single worst-affected agent's own pending-update count reaching `NotificationUpdatesPerMachineThreshold`, or the number of affected agents reaching `NotificationAffectedMachinesThreshold`. Both counts are computed through the exact same `UpdateFilterMatcher.IsExcluded` shared decision point `Updates.UpdateService`/`Agents.AgentService` already use, so this worker can never disagree with what the admin UI itself displays as pending. Sends via the existing `IEmailNotificationService.SendNotificationAsync` primitive (built for the certificate-expiry feature and always intended to be reused here) to the existing `NotificationRecipientAddress` — no second recipient setting introduced.
- **At the user's explicit request, each of the two thresholds also got its own independent on/off checkbox** (`NotificationUpdatesPerMachineEnabled`/`NotificationAffectedMachinesEnabled`, both default true) — an admin can rely on just one of the two conditions without the other ever firing, not only turn the whole mechanism on or off at once. Surfaced in the Notifications tab directly above each threshold's number field (disabling a checkbox also disables its own number input), and on `AdminSettingsDto`/`UpdateAdminSettingsRequest` alongside the existing threshold fields.
- New `UpdateThresholdNotificationState` (DB schema `0.16.1`) tracks each condition edge-triggered rather than time-windowed, since (unlike a certificate's thumbprint) a threshold condition has no natural identity to key "already warned about" on: a notification fires once when a condition transitions from not-crossed to crossed, never again on a later tick while it stays crossed, and fires again only once the count has genuinely dropped back below the threshold and later crosses it a second time. Follows the same "audit-log and mark handled only once actually handled" rule `CertificateExpiryWorker` established: with no recipient configured, "handled" means immediately (nothing to retry); with one configured, "handled" means the email actually sent — a transient SMTP failure leaves the state untouched so the identical audit-log-plus-email attempt retries whole on the next tick. Each crossing is audit-logged as `notifications.updates-per-machine-threshold.crossed`/`notifications.affected-machines-threshold.crossed` (actor `system`).
- The scaffolded migration's `AddColumn` calls for both new checkbox columns needed the same by-hand `defaultValue` correction CLAUDE.md already documents as a recurring `dotnet ef` gap — it generated `false` for both regardless of the entity's own `= true` initializer; hand-corrected to `true` so an upgrading deployment's existing settings row starts with both checkboxes on, not silently off. `LegacyAdminSettingsMigrationTests` extended to assert this explicitly, matching its existing guard for `AgentCertificateValidityDays`'s equivalent past bug.
- Real test coverage: `UpdateThresholdNotificationWorkerTests` runs against a real SQLite file (not a mocked provider) through the actual EF Core migration chain, covering both thresholds firing independently, filtered-update exclusion, each checkbox suppressing its own condition, the edge-triggered fire-once/re-fire-after-clearing behavior, and retry-after-a-failed-send.

## [0.30.16] - 2026-09-12

### Fixed

- **The admin SPA and the entire cookie-gated admin API were also reachable on the agent-facing mTLS port (8796), not just the intended browser-facing port (8795) — found by a user question ("Verbindung per Browser auf Agent-Port landet in der GUI. Wurde das bewusst so implementiert oder ist das ein Fehler?"), confirmed as a real bug, not by design.** `UseStaticFiles`, `MapFallbackToFile("index.html")`, and every controller route (`MapControllers()`) were registered globally on one pipeline shared by both Kestrel listeners, with nothing anywhere aware of which port a given request actually arrived on. Since a session cookie isn't port-scoped, an admin already logged in via 8795 had their browser's cookie sent to 8796 automatically too — meaning the full admin API (approve/delete agents, change settings, everything) was reachable there as well, undermining any network-segmentation assumption that "8796 is agent-only, safe to expose more broadly than 8795." **Not** a privilege-escalation path for a compromised/stolen *agent* certificate, though — verified directly: bare `[Authorize]` (used on every admin controller) resolves only the Cookie scheme (`AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)`'s default), never the separate Certificate scheme the `AgentCertificatePolicy` explicitly opts into — an agent's own client cert alone grants it nothing on the admin surface, on either port.
- Fixed with a new `[AllowedOnAgentPort]` marker attribute, applied explicitly to exactly the routes a real agent calls over that port (`AgentProtocolController`, `VersionController`, `UpdatesController.ReportUpdates`/`AcknowledgeInstall`) — deliberately an explicit per-endpoint allowlist rather than inferring "agent-facing" from something implicit like "anonymous" (`AuthController`'s login is also anonymous) or "certificate-policy-gated" (would miss `VersionController`, which `HeartbeatWorker` polls anonymously on every tick for protocol-mismatch detection). A new `Program.cs` middleware rejects (404) any request on the agent port whose resolved endpoint doesn't carry it — safe to add anywhere in source order relative to the later `UseStaticFiles`/`MapControllers`/`MapFallbackToFile` calls, since `WebApplication`'s minimal-hosting model runs endpoint routing before any `app.Use(...)` middleware regardless of where it's registered, confirmed live (see below) rather than assumed.
- Live-verified end to end against a real running instance on isolated ports, not just `dotnet test` (`WebApplicationFactory`'s in-memory `TestServer` can't exercise this at all — it never binds a real port, so `HttpContext.Connection.LocalPort` never reflects either configured value, the same standing mTLS-testing limitation this file already documents elsewhere): the agent port's root path and `/api/agents` both now correctly return 404 (previously 200/401), while `/api/version`, `/api/agent/ca-certificate`, `/api/agent/ca-certificates`, and a real `POST .../register` call all remain reachable there exactly as before — and the browser-facing port's behavior (SPA, `/api/agents` 401, `/api/version` 200) is completely unchanged.
- New `AllowedOnAgentPortAttributeTests` — a metadata audit, not an integration test (same `WebApplicationFactory` limitation as above): asserts the exact attribute is present on every agent-facing action, absent from the corresponding admin-facing ones sharing the identical `api/agents/{hostname}/...` route prefix, and absent from every other admin-only controller — real regression protection for the metadata even though the middleware's actual enforcement needed the live run above to prove.

## [0.30.15] - 2026-09-12

### Fixed

- **Four hardcoded English strings in `server/web` bypassed the i18n system entirely — found by an explicit audit at the user's request ("Ziehe alle Texte bis auf die Logmeldungen aus dem Quellcode, übersetze sie und bring sie in den entsprechenden Sprach-Templates unter").** All four now route through `useTranslation()`'s `t()` like every other UI string in this app, with German translations added alongside: `AgentDetailPage`'s "Agent not found." (`agentDetail.notFound` / "Agent nicht gefunden."), `AgentsListPage`'s "Failed to load agents." (`agents.loadError` / "Laden der Agents fehlgeschlagen."), the selection-checkbox column header's `aria-label="select"` (`agents.selectColumn` / "Auswählen"), and `LanguageSwitcher`'s `aria-label="Language"` (`nav.language` / "Sprache") — the last two were screen-reader-only text, easy to miss since nothing renders them visibly. `"Windows"`/`"Linux"` (the OS-filter dropdown's two fixed options) and `"UpdateWatch2"` (the product name, header/login) were deliberately left as plain strings — not translation gaps, since both read identically in either language.
- **A separate, related finding, not acted on**: `server/src/UpdateWatch2.Server/Resources/I18n/{en,de}.json` — mentioned in this repo's own CLAUDE.md as "DE/EN strings... placeholders" — are dead scaffold files nothing in the C# codebase ever loads (confirmed: zero references anywhere in `src/`), and are already stale relative to the real, live translations (e.g. still say "Administration" where the actual UI has said "Settings"/"Einstellungen" since server v0.29.0). The real, working bilingual system is entirely client-side in `server/web/src/i18n/locales/*.json`, which is what this fix updates. Left the dead files alone rather than updating text nothing reads — worth deleting outright in a future pass, or wiring up for real if server-side i18n is ever actually needed.
- **A larger, out-of-scope gap surfaced by the same audit**: many admin-facing error messages (`Conflict(new { message = result.FailureReason })`, `BadRequest(new { errors = [...] })` across `Api/Controllers/`) are free-text English strings generated server-side with no translation mechanism at all — the frontend's `apiClient` just displays them verbatim (see `readErrorMessage` in `web/src/api/client.ts`). Genuinely bilingual error messages would need an error-code-based redesign (server returns a code, client maps it through `t()`), not just new JSON entries — a real architectural decision, so it wasn't attempted here without being asked.

## [0.30.14] - 2026-09-12

### Fixed

- **`AgentDetailPage`'s four status cards (Identity, Certificate, Install status, Reboot status) had no room to stay readable once a fourth card joined the previous three-card layout — reported directly by the user ("Für 4 Tables nebeneinander ist nicht genug Platz, weswegen jetzt kaum noch etwas lesbar ist.").** Install status and Reboot status are now grouped into a `.card-stack` (a vertical pair) that itself counts as a single, fixed-width (320px) flex item alongside Identity and Certificate, which continue to flex-grow and share the remaining row width — `.detail-cards` switched from a 4-column CSS grid to flex-wrap for this. Reboot status sitting directly under Install status is also the layout the user specifically asked for, not just a side effect of the width fix.
- **Uptime showed "vor 2 Stunden"/"2 hours ago" — wrong framing for a duration-since-boot field, reported directly by the user ("Bei Laufzeit ... steht 'vor'. Korrekterweise müsste es 'seit' lauten.") — and asked whether English had the same problem, which it does.** `formatRelativeTime`'s `Intl.RelativeTimeFormat`-based "X ago" phrasing is right for a past-event field like "last seen", but wrong for a duration a machine has been continuously running for. `web/src/utils/relativeTime.ts` gained a new `elapsedSince` export (the same magnitude/unit computation `formatRelativeTime` already did internally, without baking in "ago"/"in" wording), and the Identity card's Uptime row now renders it through new `agentDetail.uptimeSince.*` translation keys — "seit {{count}} Stunden" in German (the word the user asked for), a bare "{{count}} hours" in English (no "since"/"ago" — "since 2 hours" isn't idiomatic English for a duration, unlike German's "seit").
- **The agent detail page never showed the OS-update-pending reboot signal anywhere except a header badge — the user asked for it under Install status too ("Auf der Agent-Detailseite fehlt die Angabe, dass ein Neustart benötigt wird. Das könnte unter Installationsstatus vermerkt werden.").** The Install status card now leads with a "Reboot required" Yes/No row reading `Agent.RebootRequired` (the existing self-reported OS-update-needs-a-reboot flag, CLAUDE.md's "update installation never triggers a reboot itself" signal) — reusing the exact same `agents.rebootRequired`/`agents.yes`/`agents.no` translation keys the header badge already uses, rather than introducing new wording for the identical fact. Deliberately distinct from the Reboot status card right below it, which is about an admin-*triggered* machine reboot, not this self-reported "an installed update wants one" signal.
- Test coverage updated: `AgentDetailPage.test.tsx`'s uptime test now asserts the bare-duration wording (and explicitly asserts the absence of "ago").

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
