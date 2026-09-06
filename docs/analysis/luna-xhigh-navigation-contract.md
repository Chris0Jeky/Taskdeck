# Navigation Contract: Paper, Legacy, Modes, and Route Access

Status: source-to-test analysis for #1589  
Last verified: 2026-09-06  
Base inspected: `origin/main` at `16963a79b1357e284fd4e403e70f833925b3105e`

## Purpose and boundary

This report records the current navigation contract before the user-facing manual is
aligned. It distinguishes four different facts that are easy to conflate:

1. a control is visible in a shell surface;
2. a destination is present in that shell's command palette/catalog;
3. a destination is linked from the current workflow context; and
4. the router allows the destination for the current authentication and feature-flag state.

This is an analysis-only slice. It does not change routes, feature flags, shell catalogs,
mode persistence, review-first agent policy, or product-facing documentation. The later
#1589 implementation slice should use this report to update the user manual, Start Here
chapters, and Help/FAQ without weakening route guards or review semantics.

## Canonical shell and workspace state

- Paper is the default shell. `paperThemeStore.ts` defines the persisted Paper modes and
  falls back to `paper`; the old deliberate `off` value is migrated to Paper.
- `AppShell.vue` selects the Paper sidebar/top bar/palette/shortcuts when Paper is on and
  the Legacy equivalents when it is off. The two shell trees are mutually exclusive.
- Workspace mode is a persisted preference with the values `guided`, `workbench`, and
  `agent`. A new local workspace defaults to `guided`; mode is not a route namespace.
- Both shell palettes are assembled from the active sidebar's available navigation items,
  plus `New Capture`. Palette reachability therefore differs from the visible primary
  sidebar and does not imply that a route is currently allowed by the router.

Sources:

- `frontend/taskdeck-web/src/store/paperThemeStore.ts`
- `frontend/taskdeck-web/src/components/shell/AppShell.vue`
- `frontend/taskdeck-web/src/store/workspaceStore.ts`
- `frontend/taskdeck-web/src/types/workspace.ts`

## Surface contract

The table uses these abbreviations:

- `P-G`: Paper in guided mode
- `P-W/A`: Paper in Workbench or Agent mode
- `L-G`: Legacy in guided mode
- `L-W/A`: Legacy in Workbench or Agent mode
- `palette`: present in the shell navigation catalog used by the command palette
- `context`: reached from a workflow link rather than a general navigation catalog item
- `flag`: the route's relevant feature flag, when any

“Visible” means a normal desktop shell surface unless the phone/tablet qualification is
called out separately. A visible item can still redirect when its route flag is disabled.

| Destination | Route / guard | P-G | P-W/A | L-G | L-W/A | Palette / context |
| --- | --- | --- | --- | --- | --- | --- |
| Home | `/workspace/home` | primary | primary | palette-only | palette-only | palette |
| Today | `/workspace/today` | primary | primary | primary | primary | palette |
| Inbox | `/workspace/inbox` | primary | primary | primary | primary | palette |
| Review | `/workspace/review`, `newAutomation` | primary | primary | primary | primary | palette |
| Boards | `/workspace/boards` | primary | primary | primary | primary | palette |
| Views | `/workspace/views` | workbench group | workbench group | workbench group | workbench group | palette |
| Notifications | `/workspace/notifications` | workbench group | workbench group | secondary/catalog | secondary/catalog | palette |
| Chat | `/workspace/automations/chat`, `newAutomation` | workbench group | workbench group | secondary/catalog | secondary/catalog | palette |
| Calendar | `/workspace/calendar` | workbench group | workbench group | secondary/catalog | secondary/catalog | palette |
| Metrics | `/workspace/metrics` | guided Advanced | workbench group | guided Advanced | catalog | palette |
| Integrations | `/workspace/integrations` | guided Advanced | workbench group | guided Advanced | catalog | palette; Export/Import context |
| Activity | `/workspace/activity`, `newActivity` | workbench group | workbench group | secondary/catalog | secondary/catalog | palette |
| Ops | `/workspace/ops/cli`, `newOps` | workbench group | workbench group | guided Advanced/catalog | catalog | palette |
| Settings | `/workspace/settings/profile`, `newAuth` | meta | meta | footer | footer | palette/catalog |
| API Keys | `/workspace/settings/api-keys` | guided Advanced | meta | guided Advanced | catalog | palette |
| Preferences | `/workspace/settings/preferences` | meta | meta | secondary/catalog | secondary/catalog | palette |
| Appearance | `/workspace/settings/appearance` | meta | meta | footer | footer | palette/catalog |
| Access | `/workspace/settings/access/:boardId?`, `newAccess` | command-only | command-only | catalog | catalog | palette/catalog |
| Archive | `/workspace/archive`, `newArchive` | command-only | command-only | catalog | catalog | palette/catalog |
| Agents | `/workspace/agents` | guided Advanced | command-only | guided Advanced | agent/catalog | palette |
| Runs | `/workspace/agents/:agentId/runs` | command-only/direct | command-only/direct | catalog/direct | catalog/direct | agent profile context |
| Run detail | `/workspace/agents/:agentId/runs/:runId` | command-only/direct | command-only/direct | catalog/direct | catalog/direct | run context |
| Cohorts | `/workspace/metrics/cohorts`, `newAutomation` | guided-only Advanced | command-only/direct | guided-only Advanced | catalog/direct | palette/catalog |
| Endpoints | `/workspace/ops/endpoints`, `newOps` | guided-only Advanced | command-only/direct | guided-only Advanced | catalog/direct | palette/catalog |
| Logs | `/workspace/ops/logs`, `newOps` | guided-only Advanced | command-only/direct | guided-only Advanced | catalog/direct | palette/catalog |
| Dev Tools | `/workspace/dev-tools`, `devTools` | guided-only Advanced | command-only/direct | guided-only Advanced | catalog/direct | palette/catalog |
| Queue | `/workspace/automations/queue`, `newAutomation` | not a shell item | not a shell item | not a shell item | not a shell item | Review/Chat context |
| Export & Import | `/workspace/settings/export-import` | not a shell item | not a shell item | not a shell item | not a shell item | Integrations context/direct URL |
| Knowledge | no route | absent | absent | absent | absent | not available |

The exact group and visibility labels above are derived from the shell catalogs, not from
the route table alone. In particular, the Legacy catalog exposes a larger searchable
catalog than its reduced primary sidebar, while Paper keeps several destinations
command-only or Advanced-only in guided mode.

## Shell-specific qualifications

### Paper

Paper desktop and tablet primary navigation is ordered:

`Home`, `Today`, `Inbox`, `Review`, `Boards`.

The Paper phone bottom bar intentionally contains only `Home`, `Today`, `Inbox`, and
`Review`, followed by `More`. The phone More drawer renders workbench, Advanced, and meta
groups, but does not render `visiblePrimary`; consequently Boards is not exposed by the
phone bottom bar or More drawer. It remains reachable from the palette/direct route. This
is an important implementation choice to document or change deliberately; the current
focused Paper sidebar tests do not assert the Boards phone behavior.

Paper's guided Advanced disclosure presents the ordered list:

`Agents`, `Metrics`, `Cohorts`, `Integrations`, `Ops`, `Endpoints`, `Logs`, `API Keys`,
`Dev Tools`.

In Workbench and Agent modes there is no guided Advanced disclosure. The existing
Advanced-capable destinations move into their broader workbench/meta/catalog groups, and
the command catalog remains complete.

### Legacy

Legacy's reduced visible primary sidebar is:

`Today`, `Review`, `Boards`, `Inbox`.

Home is catalog/palette-only. Settings and Appearance have separate footer treatment.
The guided Advanced disclosure presents the same nine-item ordered list as Paper. Legacy
Workbench and Agent modes do not show the guided disclosure; the catalog remains available
for those modes.

### Mode and flag interaction

The shell catalogs mark several flagged items as `workbenchBypassesFlag`, so Workbench can
surface them even when the presentation flag is off. The router guard does not implement
the same bypass: a direct route with a disabled `requiresFlag` redirects to
`/workspace/home`. This is a deliberate distinction between presentation and route
access, not evidence that the route flag is ignored.

Examples include Review, Chat, Activity, Ops, Settings, Access, and Archive. The report
does not change either side of this contract. A later implementation should add a
source-backed explanation and focused real-router coverage so a visible item cannot be
mistaken for guaranteed route access.

## Context-linked destinations

### Queue

Queue is a route under the automation surface and is guarded by `newAutomation`. It is
not an item in either shell's general navigation catalog. Review links to Queue from its
header, and Automation Chat also opens Queue. Both shell implementations treat the Queue
route as active Review context. Documentation should call Queue a Review/automation
context destination rather than a general Ctrl+K destination.

### Export & Import

Export & Import is the route `/workspace/settings/export-import`. The verified product
link is from Integrations, whose copy directs the user to Settings → Export & Import for
JSON export/import and Markdown/Web Clip import into the Capture Inbox. No source link
was found from the capture composer itself, and neither shell catalog contains an
`export-import` nav item. Documentation should preserve the Integrations context and
should not claim that capture opens Export & Import.

## Agent, Runs, and Knowledge availability

- Agents, Runs, and Run Detail are live routes without a feature flag. `AgentsView` is
  the API-created profile surface; selecting a profile opens its Runs route, and selecting
  a run opens Run Detail. Run Detail can return to Review with proposal context.
- Knowledge has no `/workspace/knowledge` route. A direct request falls through to the
  shell's not-found route. Knowledge should remain documented as unavailable/future.
- `docs/product/HELP_AND_FAQ.md` currently says that standalone Agents, Runs, and
  Knowledge pages are not already available. That statement is stale for Agents and Runs
  and must be narrowed to Knowledge in the user-facing documentation update.
- `docs/manual/01_start_here.md` currently describes Agents as future/unshipped and
  Integrations as planned. Those claims conflict with the live routes and current
  `docs/USER_MANUAL.md`/`docs/START_HERE.md` wording and must be reconciled in the later
  documentation slice.

## Source-to-test map

| Contract area | Primary source | Existing proving coverage / limitation |
| --- | --- | --- |
| Paper vs Legacy shell selection | `components/shell/AppShell.vue`; `store/paperThemeStore.ts` | `AppShell.paperVariant.spec.ts` proves mutual exclusivity and palette/viewport behavior |
| Legacy catalog, reduced sidebar, modes, flags | `components/shell/ShellSidebar.vue` | `ShellSidebar.spec.ts` covers primary IA, palette catalog, Advanced disclosure, mode persistence behavior, and Workbench presentation bypass |
| Paper groups, phone/tablet surfaces, modes, flags | `components/paper/PaperSidebar.vue` | `PaperSidebar.spec.ts` covers group order, Advanced disclosure, flag bypass, phone bottom bar, and tablet rail; it does not assert the phone Boards omission |
| Command palette filtering and activation | `components/shell/ShellCommandPalette.vue`; `components/paper/PaperCommandPalette.vue` | `ShellCommandPalette.spec.ts` and `PaperCommandPalette.spec.ts` cover filtering, keyboard, board/card results, and route activation |
| Workspace mode default/persistence | `store/workspaceStore.ts` | `router/workspaceRouteStability.spec.ts` covers guided default and persisted Workbench/Agent mode |
| Route table and auth/flag redirect | `router/index.ts` | `routerIntegration.spec.ts`, `featureFlagGuard.spec.ts`, and `workspaceRouteStability.spec.ts` cover representative redirects and route stability; the integration test uses a production-shaped subset |
| Named-route affordance inventory | `guards/routeAffordanceCoverage.spec.ts`; `tests/e2e/support/routeAffordanceInventory.ts` | Ensures named routes have an affordance row; explicitly does not prove sidebar/catalog semantics |
| Queue context link | `components/review/ReviewHeader.vue`; `views/AutomationChatView.vue` | Route and component behavior are covered by automation/review tests; no general palette-catalog assertion exists because Queue is intentionally context-linked |
| Export/Import context link | `views/IntegrationsView.vue`; `views/ExportImportView.vue` | Route stability covers the route; source mapping is the current evidence for Integrations context, with no capture-composer link found |
| Agents/Runs availability | `views/AgentsView.vue`; `views/AgentRunsView.vue`; `views/AgentRunDetailView.vue` | Route and view tests cover the API-created flow; no Knowledge route exists |

## Follow-up implementation and verification

The full #1589 documentation implementation should:

1. update `docs/USER_MANUAL.md`, `docs/START_HERE.md`, `docs/manual/01_start_here.md`,
   and `docs/product/HELP_AND_FAQ.md` from this contract;
2. add or strengthen a real-router test proving the difference between Workbench
   presentation bypass and route-level feature-flag redirect;
3. decide and test whether the Paper phone omission of Boards is intentional; and
4. keep Queue as Review/Chat context, Export & Import as Integrations context, and
   Knowledge as unavailable until a route ships.

For the current analysis-only slice, the required checks are documentation governance,
golden-principles, and diff whitespace validation. The focused source tests listed above
are the representative proving map for the later user-facing documentation change.

## Verification performed

- `node scripts/check-docs-governance.mjs` — passed.
- `node scripts/check-golden-principles.mjs` — passed.
- `git diff --check` — passed.
- From `frontend/taskdeck-web`, the representative Vitest command covering the nine files
  listed above — 9 test files passed, 201 tests passed.

The frontend install used Node `v24.13.1` and completed successfully. npm reported the
repository's existing engine warnings for `abbrev`/`nopt` and two moderate audit findings;
dependency remediation is outside this report's scope.
