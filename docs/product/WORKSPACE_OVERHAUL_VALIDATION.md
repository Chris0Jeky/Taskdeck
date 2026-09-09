# Workspace overhaul validation and follow-through

Date: 2026-09-08 · Delivery tracker: [#2800](https://github.com/Chris0Jeky/Taskdeck/issues/2800)

The [resource map and feature ledger](WORKSPACE_OVERHAUL.md) distinguish supplied design intent from
integrated behavior. All three chats were read; all 20 supplied files have a recorded size, hash and
purpose. Raw chats, screenshots and self-contained HTML were retained locally and not imported as
executable application code or committed as public source material.

## Evidence

Integration checks use an isolated checkout based on `origin/main`, not the maintainer's two unpublished
local commits. Playwright provisions synthetic accounts and a separate SQLite database. No operational
database, live external model, microphone or production deployment is part of this proof.

| Check | Scope |
| --- | --- |
| `npm.cmd run typecheck` | Complete frontend TypeScript/Vue build graph |
| `npx.cmd vitest run --maxWorkers=2` | Complete frontend unit/component/guard suite |
| `npm.cmd run build` | Production frontend bundle |
| `npx.cmd eslint <changed frontend files>` | Changed Vue/TypeScript and browser specifications |
| `dotnet test backend/Taskdeck.sln -c Release -m:1 --no-restore` | Full .NET solution; optional integration skips remain explicit |
| `dotnet test backend/Taskdeck.sln -c Release -m:1 --no-restore --filter 'FullyQualifiedName~Thinking\|FullyQualifiedName~WorkspaceInsight\|FullyQualifiedName~QuietInsight\|FullyQualifiedName~MigrationBootstrap\|FullyQualifiedName~Export\|FullyQualifiedName~ApiControllerBoundaryTests'` | Final source-question, authorization, revision, import/export, migration and API-boundary seams |
| `dotnet ef migrations has-pending-model-changes` | Combined EF model matches the latest additive migration |
| `npx.cmd playwright test tests/e2e/workspace-overhaul.spec.ts --project=chromium` | Real capture → review → explicit apply, shared experience switching, drafts/conflicts, questions/private memory/history/export, board disclosure, Grove Night, 375/768/1440px viewports |
| Axe checks inside the browser specification | New Home and card surface against WCAG A/AA automated rules; not a complete accessibility certification |
| `node scripts/check-doc-links.mjs` and `node scripts/check-docs-governance.mjs` | Repository links and documentation conventions |

The first full frontend run passed 6,190 tests and identified three integration failures: new routes
needed the affordance inventory, a thinking-button container needed an explicit group role, and
prepending palette commands changed the established keyboard order. The implementation preserves
that order and the targeted regression suite passed after correction.

The first full backend run passed 8,965 tests with 34 declared skips. Its three failures were the
architecture source parser rejecting the new controller's primary-constructor/sealed declaration.
Conventional controller syntax fixes the incompatibility without changing the architecture gate;
the complete architecture suite then passed 28 tests with its one declared skip.

Browser implementation work additionally corrected a select's accessible name and excluded a
decorative arrow from the thinking-link name. The later full frontend run passed 6,215 tests with
three declared skips. Review fixes protect unsaved Memory drafts, reject Thinking writes when a
board is archived (including a concurrent archive), and include private workspace records in both
account export formats and transactional account erasure. Relational tests exercise surviving shared
boards and prove other users' private records remain isolated. Browser proof also confirms that an
unsaved Home capture remains protected after switching back to Classic.

Later exact-head results and hosted CI status are recorded on
[the integration PR](https://github.com/Chris0Jeky/Taskdeck/pull/2807). Historical source-prototype test
counts are not used as evidence. Further implementation is tracked in
[#2808](https://github.com/Chris0Jeky/Taskdeck/issues/2808).

## Remaining sequence

1. **Linked steps and dependency connections.** Reuse the guarded card writer with transaction-safe
   post-commit notifications before introducing promotion. Promotion must be idempotent, preserve
   WIP/archive/audit rules, derive status from the real child, and never delete a child when its
   thinking layer is removed. Do not call destructive deletion an undo.
2. **Contextual companion and proposal overlays.** Bind current board/card/selected source evidence;
   render the existing effective proposal revision in place and preserve the approve/apply separation.
   Integrate private memory retrieval only with explicit user scope and archived exclusion.
3. **Studio continuity and planning.** Implemented personal plan, last-worked continuation, Focus and
   Make room without changing due dates. List/Board/Horizon represent chosen work over the same cards.
   Direct product verification is recorded below; subjective experience preference remains a human choice.
4. **Shared source infrastructure.** Connect question originals and audio representations to Context
   Fabric, with supersession/history and retention/deletion/export coverage. Keep board-shared thinking
   and user-private answers separate throughout those pipelines.
5. **Expanded intelligence and optional attention.** Build a usefulness corpus for new structural and
   model-generated observations, then prove freshness, permissions, deduplication, budget and dismissal.
   Optional nudges require a separate opt-in suppression policy; current insights never interrupt.
6. **Comparison beyond personal trials.** Record product build and scenario when trials span releases;
   define outcomes, assignment and sample requirements before making statistical A/B claims.

These are remaining prototype capabilities, not blockers hidden behind placeholder success states.
Existing owner decisions remain in [OUTSTANDING_TASKS.md](../../OUTSTANDING_TASKS.md); no publisher,
signing, private-instance, release/runner or subjective palette/dogfooding item is inferred complete.

## Personal planning continuation (2026-09-09)

`WorkspacePlanApiTests` proves authentication, viewer permission for private choices, foreign-card
rejection, stale and interleaved writer conflicts, archive/deletion/revocation redaction, explicit
focus timestamps, both account exports, account erasure and unchanged card material. Its streaming
export regression catches synchronous response flushing; bounded plan serialization writes through
the existing asynchronous stream path. `UserPreferenceRaceTests` covers the existing insert-or-ignore
preference initialization with new default columns.

`workspacePlanStore.spec.ts` and `usePlanCardPicker.spec.ts` exercise identity clearing, old-response
rejection, failed-write refresh requirements, project pagination and discovery retry. The Chromium
journey `tests/e2e/workspace-plan.spec.ts` covers plan/reload/List/Board/Horizon, Focus, Home resume,
Make room, unchanged card deadlines, 375px width and automated accessibility. Use the repository
Playwright setup with an isolated synthetic database. Exact full-suite and hosted results belong to
the continuation PR; no live working database or physical device is claimed by these checks.
