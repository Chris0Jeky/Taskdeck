# Workspace overhaul validation and follow-through

Date: 2026-09-08 · Delivery tracker: [#2800](https://github.com/Chris0Jeky/Taskdeck/issues/2800)

The [resource map and feature ledger](WORKSPACE_OVERHAUL.md) distinguish supplied design intent from
integrated behavior. All three chats were read; all 20 supplied files have a recorded size, hash and
purpose. Raw chats, screenshots and self-contained HTML were retained locally and not imported as
executable application code or committed as public source material.

## Evidence

The dependency continuation adds `BoardDependencyApiTests` for actor-derived authentication,
viewer/foreign scope, explicit card immutability, cycles, stale writes, interleaved archive/deletion/CAS,
audit rollback, deleted references and both JSON export endpoints. Import tests prove ID remapping and
atomic rejection of foreign links, cycles and unsupported envelope versions. Domain tests cover a
diamond DAG, cycles, invalid/duplicate links and the 500-edge limit. The repository performs existence
validation inside its write transaction; the board archive token alone does not serialize independent
card deletions.

`CardDependencies.spec.ts` covers demand loading, explicit selected mutations, failed-write metadata
clearing, account changes and viewer rendering. `card-dependencies.spec.ts` proves both relationship
directions, all four experiences, reload, cycle refusal, export/import, unchanged cards, 375px layout and
automated accessibility. Exact full-suite and hosted execution results belong to the dependency PR.

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

1. **Dependency delivery qualification.** Linked-step promotion is merged in PR #2837. Explicit
   dependency edges, cycle/CAS/deletion/archive guards and portable ID remapping are implemented in
   PR #2844; complete its hosted qualification and merge. Do not reimplement those edges.
2. **Contextual companion follow-through.** Card thinking/Focus now embeds board-scoped conversations,
   explicit selected card/shared-thinking/private-memory context and effective-revision previews.
   Continue with typed SourceAsset selection and previews mapped onto affected board objects; retain
   existing subset-selection, approve and explicit Apply behavior.
3. **Studio continuity and planning.** Implemented personal plan, last-worked continuation, Focus and
   Make room without changing due dates. List/Board/Horizon represent chosen work over the same cards.
   Direct product verification is recorded below; subjective experience preference remains a human choice.
4. **Shared source infrastructure.** Connect question originals and audio representations to Context
   Fabric, with supersession/history and retention/deletion/export coverage. Keep board-shared thinking
   and user-private answers separate throughout those pipelines.
5. **Expanded intelligence and optional attention.** Build a usefulness corpus for new structural and
   model-generated observations, then prove freshness, permissions, deduplication, budget and dismissal.
   Optional nudges require a separate opt-in suppression policy; current insights never interrupt.
6. **Comparison beyond personal trials.** Scenarios, explicit completion outcomes, optional ease ratings
   and backend-reported version attribution now travel in version-2 exports. Next: independent participant
   sampling/assignment and outcome analysis before statistical A/B claims. The version is not an exact
   frontend commit fingerprint; unavailable attribution remains null.

These are remaining prototype capabilities, not blockers hidden behind placeholder success states.

## Contextual companion continuation (2026-09-09)

`ChatContextApiTests` proves explicit source inclusion without changing user intent, private actor and
board isolation, archive/revision/size selection rejection, replay revalidation, and unchanged cards.
`ProposalRevisionApiTests` proves that the preview receipt and rendered diff use the same effective
revision, including the approved pin after a later revision appears; preview never applies changes.
Existing chat/SSE and proposal service regressions remain part of the proving set.

The source picker loads on request, clears on identity/board changes and failed refresh, caps selected
private memories at five, and submits IDs/revisions rather than private text. Preview is text-only,
clears after at most 30 seconds or proposal expiry, and cannot approve or execute. The browser journey
`tests/e2e/contextual-companion.spec.ts` uses an isolated database and the deterministic Mock provider
to prove source receipts, a revised preview, unsent-draft protection, all experiences, 375px width and
automated accessibility. Mock proves wiring, not model usefulness or live-provider quality.

Focused execution: 80 API, 237 Application, 28 Architecture (one existing skip), and 89 frontend tests
passed. The Chromium journey passed after a mobile wrapping correction. Full-suite/build and hosted
qualification are recorded on the contextual delivery PR; these focused results do not imply them.

Selected material is bounded and may be excerpted. The receipt exposes source identity/version and
excerpt status; generated answers remain in owned chat history after a source changes. Audio and
question originals are not yet promoted into Context Fabric by this delivery.
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

## Linked-step continuation (2026-09-09)

The next vertical under #2808 adds manual card promotion to saved thinking steps. Its direct checks
are `ThinkingStepApiTests` (authentication, viewer/foreign scope, WIP/archive/stale/invalid rejection,
concurrent saves, repeat safety, actor audit, deletion and portable links), the existing
`ThinkingDeckApiTests` and `CardServiceTests`, and `ThinkingStepCard.spec.ts`. The new journey in
`workspace-overhaul.spec.ts` exercises creation, retry, refreshed blocker status, all experiences,
phone width and automated accessibility. Exact execution results belong to the continuation PR.

New links are server introduced only; general deck saves cannot inject a foreign card reference.
Import accepts optional source card IDs only to remap relationships within the imported payload.
Source IDs never become the IDs of newly created cards. Linked material advertises schema version 2
to prevent silent link loss in older readers. API tests cover version-2 round trips, reject linked
material mislabeled as version 1, and verify ordinary decks still use version 1.
This uses existing JSON thinking storage,
with no migration. It does not introduce a dependency graph, private-memory retrieval or autonomy.


Comparison follow-through keeps observations session-only and explicit. `workspaceExperimentStore.spec.ts`
and the comparison browser journey cover scenario/outcome validation, optional ratings, versioned export
and identity reset. The insight/memory follow-through uses delayed-response component regressions to
prove that analysis settles across route changes, conflicting actions are disabled, initial memory reads
finish before creation, and Retry returns to failed board discovery. These are fixes from #2808's
recorded review residuals, not changes to approval/apply authority.

## Comparison files across releases (2026-09-09)

Comparison exports now use version3 with stable observation IDs and frontend input fingerprints.
The comparison page imports version2/3 files locally, validates the full batch before accepting it,
skips identical IDs, rejects conflicting IDs and enforces 500 observations / 2 MiB per file. Legacy
version2 entries receive deterministic content-based IDs and retain unknown frontend attribution.
Import is manual; notes remain in memory until explicitly exported, and identity changes clear them.
No notes are sent to a server or assigned automatically. The grouped table keeps scenario, experience,
presentation, theme, backend version and frontend fingerprint separate; ratings exclude unrated
entries. This is descriptive personal evidence, not a randomized/statistical A/B result.

Production Vite builds embed a SHA256 fingerprint over frontend source/public/build inputs, locked
packages/configuration, Node version, mode and public Vite environment values. Only the digest is
embedded. Development has no fixed frontend identity and records null. This fingerprint identifies
build inputs, not a signed release or an external deployment's contents.

Proving seams: workspaceExperimentStore.spec.ts, WorkspaceComparisonFiles.spec.ts and
`node --test build/frontendIdentity.node-check.mjs`; the extended comparison/Grove browser journey exports,
reloads, imports, skips duplicates and checks all four experiences at desktop/tablet/375px widths.

