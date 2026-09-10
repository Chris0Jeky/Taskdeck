# Workspace overhaul validation and follow-through

## Board-object proposal overlays (2026-09-10)

`BoardProposalPreview.spec.ts` covers matched effective revisions, approved pins, mismatched board/revision/status/update receipts, parameter-ID precedence, immutable board inputs, permission/board refresh failure, stale in-flight results, logout, same-user token refresh, server-clock expiry and both card renderers. Existing Chat preview and Board view regressions also pass. Full frontend: 6,368 passed and three existing skips; subsequent projection/access changes have 49 focused passing tests plus typecheck. Exact final evidence is retained with the continuation PR.

`board-proposal-overlays.spec.ts` creates a real update/reorder/create proposal and checks both board renderers across all four experiences. It proves visible markers, saved titles, explicit close, unchanged saved cards/columns, no browser mutation requests, Review-to-board navigation, access refusal and 375 px/no serious-critical axe findings. The final journey passed in 15.8 seconds. Initial fixture failures omitted required operation parameters, requested an unsupported column action, and captured column card counts before seeding the card; each was corrected without relaxing product assertions. New objects remain described by the checked diff rather than simulated board state. No actual board Apply, production deployment or provider-quality acceptance is claimed.

## Indexed original-history pages (2026-09-10)

Real HTTP/SQLite tests follow 1,012 immutable originals beyond the former cutoff and exercise the maximum cursor, invalid negative cursors, mixed offset/cursor requests, ordinal zero, and gaps caused by interleaved external references. The new query uses the existing unique `(CaptureId, Ordinal)` index and takes eleven rows to display ten plus continuation; ownership, active board access and expected memory revision remain checked before reading.

Picker/API tests verify cursor forwarding, increasing ordinal validation, exact continuation identity, malformed pages, permissions and session changes. The Chromium original-source journey now creates twelve answer versions, loads the second page and selects the original first answer, then replays its receipt across all four experiences and checks narrow-screen accessibility. Full-suite outcomes belong to the continuation PR. The initial unrestricted-worker frontend run was stopped after a depth-token test failure; that unchanged test passed alone with two workers, and the bounded full run is recorded separately.

This supersedes the earlier source-interaction offset implementation below: legacy row-offset requests are bounded to 1,000, while current clients start `afterOrdinal=-1` and follow `nextAfterOrdinal` through the complete history. No migration or provider/device acceptance is implied.

## Source upload errors and library metadata (2026-09-10)

`ThinkingAudioApiTests` injects the host's 413 exception during a partially read audio stream, exercises the real application transaction through the controller, and verifies the standard error envelope plus absence of stored blobs, references, captures, audio answers and model requests. Existing owner/access, correction, export/erasure and library cases remain in the same suite.

The twenty-record library fixture checks exact ordered metadata, 500-character SQL excerpt clipping with an ellipsis, foreign-owner exclusion and an empty change tracker. A SQLite command interceptor proves one joined metadata query per non-empty page and no query for an empty page; binary chunks and capture histories are not materialized. The browser library path and full backend qualification are recorded with the continuation PR. This does not claim Kestrel socket transport, live deployment or transcription-provider acceptance.

## Audio review repairs (2026-09-10)

Audio drafts now bind to the board, card, question and revision present when file selection or microphone acquisition starts. Editing that question retains the local file for replay/download but prevents uploading it under new question evidence. A new draft receives the current binding. Two regressions cover edits before upload and while recording.

Production CSPs permit only same-origin and local `blob:` media; nginx and its AWS template permit same-origin microphone requests while continuing to deny camera and geolocation. API header checks, proxy policy contracts and Chromium probes exercise these exact policy strings. The probes load a local WAV and inspect effective microphone policy; they do not claim real microphone hardware acceptance. The retained-original library browser journey also passes.


## Original-source context continuation (2026-09-09)

`ChatOriginalContextApiTests.cs` extends the context API suite with original-only dispatch, exact
historical receipts, stream replay, immutable source membership, current-version rejection, private
owner/access checks, bounded pages/excerpts and untracked current-memory reads. Source selection
does not introduce a migration, implicit retrieval, background processing or automatic board writes.
`ChatOriginalSourcePicker.spec.ts`, the parent picker suite and the API client suite cover explicit
load, text escaping, page continuation, the combined five-source budget, retry and stale responses
after identity/revision changes or unmount. `original-source-context.spec.ts` uses a fresh synthetic
account to select a superseded answer, send through the real API, reload all four experiences and
inspect the receipt at 375px with an accessibility scan. Execute with a dedicated database and
`TASKDECK_E2E_REUSE_EXISTING_SERVER=0`; exact run results are recorded with the continuation PR.

These checks prove selection and source transport with the test/Mock provider. They do not establish
live model answer quality, audio/transcription quality, release deployment or physical-device acceptance.
## Comparison compatibility follow-through (2026-09-09)

The observation UUID fallback uses the [browser API available on insecure origins](https://developer.mozilla.org/en-US/docs/Web/API/Crypto/getRandomValues).
Legacy import checksums retain SHA-256 compatibility using a local fallback solely for observation
deduplication. The standalone `node --test build/comparisonIdentity.node-check.mjs` check compares
Unicode, block-boundary and long inputs against Node's SHA-256. Store and
`comparison-compatibility.spec.ts` browser tests remove `randomUUID` and `subtle` while retaining
`getRandomValues`, then prove record/export/import and cross-path duplicate handling. This simulates
the relevant LAN API availability; it is not a physical LAN-device acceptance claim.

The frontend identity plugin reads [Vite's resolved configuration](https://vite.dev/guide/api-plugin.html#configresolved)
and stamps the identity module at build time. Base path, production mode, target, minification,
CSS/asset options, sourcemaps and public environment join the existing source/config input hash.
The default production build and a `/Taskdeck/` base-path build must emit distinct stamps.
The fingerprint remains input attribution, not a signed artifact or deployment assertion.

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

1. **Dependency delivery is merged.** Linked-step promotion landed in PR #2837; explicit dependency
   edges, cycle/CAS/deletion/archive guards and portable ID remapping landed in PR #2844 after hosted
   qualification. Continue broader source and representation work without reimplementing those edges.
2. **Contextual companion follow-through.** Card thinking/Focus now embeds board-scoped conversations,
   explicit selected card/shared-thinking/private-memory context and effective-revision previews.
   Continue with typed SourceAsset selection and previews mapped onto affected board objects; retain
   existing subset-selection, approve and explicit Apply behavior.
3. **Studio continuity and planning.** Implemented personal plan, last-worked continuation, Focus and
   Make room without changing due dates. List/Board/Horizon represent chosen work over the same cards.
   Direct product verification is recorded below; subjective experience preference remains a human choice.
4. **Shared source infrastructure.** The private-memory source continuation implements native question
   evidence and answer originals, corrections, retention/deletion and exports. Finish bulk historical
   admission, typed source selection, audio representations and retry/failure handling. Keep shared
   thinking and user-private answers separate throughout those pipelines.
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

## Private memory source continuation (2026-09-09)

`PrivateMemorySourceApiTests` and extended `ThinkingAnswerApiTests` exercise immutable question/answer
originals, exact history references, correction supersession, admission of existing saved history,
owner isolation, competing-save rollback and stale-question rollback. Buffered and streaming account
exports preserve originals after board deletion; account erasure removes source assets. Saving memory
does not enqueue an LLM request. The initial focused API set passed 30 tests.

The new originals viewer and export tests passed with the surrounding memory surface (26 frontend
tests). The real browser journey creates and corrects memory, reads exact preserved whitespace,
downloads and inspects source JSON, archives/restores and switches through all four experiences.
It passed at 375px with no horizontal overflow and no serious/critical axe findings in the new panel.
The first test attempt inspected sources before correction completed; the final test waits for the
saved revision. This was a test synchronization correction, not a source-storage failure.

The first full backend invocation found a capture-store test double missing the added native-export
method. After updating that double, the entire Application suite passed 4,258 tests. Other full-suite
results and hosted qualification belong to the delivery PR; this entry does not imply they passed.
Typecheck passed; scoped lint has no errors and retains the existing memory-view length warning.

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

## Preserve older memory originals (2026-09-09)

The Memory page offers explicit preservation for displayed older entries without native sources,
including the archived filter. Batches contain at most 50 owner-scoped IDs and expected revisions.
Every entry is checked against the active accessible board before one transactional save; stale,
foreign or invalid selections admit nothing. Preservation advances the concurrency revision without
changing wording, status or archive state. Existing history is linked to immutable originals, and
retrying already preserved entries with current revisions creates no duplicate capture. No migration
or background backfill runs. Source retention and account export/deletion follow the native-memory
policy above; approval and board Apply remain separate.

Direct checks: `LegacyMemorySourceApiTests` and `PrivateMemorySourceApiTests` cover exact text,
archived history, ownership, bad/stale batches, repeat admission and competing saves.
`WorkspaceMemoryPreservation.spec.ts` covers bounded selection, conflict retry, identity/revision
changes and unmount. The browser journey `legacy-memory-originals.spec.ts` requires
`TASKDECK_E2E_DB=legacy-originals.e2e.db`; it inserts one legacy-shaped synthetic row in that exact
isolated database, then uses the real UI/API to preserve, inspect, export and reload it at 375px.
Exact executed results belong to the continuation PR; hosted checks are separate.

## Private audio-answer continuation — 2026-09-10

This implementation adds original audio retention and manual written confirmation under #2808.
Focused proof: `dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release -m:1
--filter "FullyQualifiedName~ThinkingAudioApiTests|FullyQualifiedName~ManualRepresentationStoreTests|FullyQualifiedName~SqliteBlobStoreTests|FullyQualifiedName~PrivateMemorySourceApiTests"`
passed 20 tests, covering real SQLite chunk streaming, quota-before-read, owner deduplication, two-writer
acquisition, lost-receipt retries, original byte integrity, stale question confirmation, both account
exports, board-deletion retention and account erasure. An initial erasure failure exposed deletion order
against new representation FKs; dependent rows now erase before source assets and the rerun passed.

Four targeted frontend files passed 18 tests: recorder permission/disposal, draft retention, uncertain
upload retry IDs, separate written confirmation, playback URL cleanup and workspace navigation guards.
The Chromium `thinking-audio.spec.ts` journey passed in 47.4 seconds using a synthetic playable WAV:
it held an accepted upload, blocked navigation, simulated a lost response, retried without duplication,
loaded real playback, saved/confirmed a written answer, reloaded all four experiences, and preserved
the original card. At 375 px the audio region had no overflow or serious/critical axe findings.

Full frontend: 6,330 passed, three existing skips across 409 files; typecheck and production build passed.
The full backend command recorded 9,175 passes, 34 existing skips and one capture export wire-format
parity failure. Adding `blobReferenceId` to its manual streaming writer fixed that mismatch; a scoped
34-test API rerun passed, including the failing parity test and a new 7 MiB streaming/buffer-budget test.
The original full-command failure is retained as evidence, not reported as a green full rerun. CLI
(243), architecture (28 plus one skip), and integration (7 plus 29 skips) all passed. Later actor-change
and same-user token-refresh guards passed 20 targeted frontend tests and typecheck. Scoped ESLint and
674-file doc-link/governance checks passed. Raw untranscribed audio has an explained local caption-rule
exception: the UI offers written alternatives and does not fabricate a timed caption track.
Hosted qualification remains on the continuation PR.
Actual microphone capture, mobile hardware/browser playback, external transcription quality, provider
consent and archival restore have not been verified by these tests. All data was synthetic; no working
database migration, external processing, telemetry or release acceptance is claimed.

## Retained original library (2026-09-10)

Memory offers explicit owner-only browsing of audio originals without requiring a currently selected
board. Pages return at most 20 entries; written representation history and binary content load only
on selection. Changed or removed questions cannot be reactivated from the library. Existing-board
read permission is rechecked for every list/detail/download; archived-board originals are readable,
and deleted-board originals remain owner-linked and readable. Current-question writes retain their
active-board/revision guards. No schema migration or background backfill is introduced.

Proving seams: ThinkingAudioApiTests covers changed/removed questions, archive and hard-delete
retention, original byte identity, anonymous/foreign/revoked requests and bounded continuation.
OriginalAudioLibrary.spec.ts covers explicit loading, empty filtered pages, errors/retry, written
alternatives, private async disposal and same-user refresh. The original-audio-library.spec.ts journey
uses a real synthetic WAV, removes its question, downloads exact bytes, invokes public board deletion
(which archives), and reloads the library across all four experiences at 375 px with an axe check.
Hard-delete retention is exercised in SQLite API tests, not falsely attributed to the public Delete
button. Unsaved local audio survives the saved-recording reload action only while the page is open;
a browser refresh is not durable draft storage. Exact suite and hosted results are recorded in the PR.

## Combined source, continuity and comparison checkpoint (2026-09-10)

The integration branch preserves the separate commits and review records for #2855 (original source
selection), #2857 (Companion continuity), #2861 (audio originals), #2852/#2856 (retained comparisons and
LAN/build compatibility), and the subsequent original-library slice. One final main-based head checks
their combined behavior, avoiding repeated sibling-base qualification as these independent features
land. Only append-only planning/status/validation text conflicted; runtime files merged automatically.
The backend tree is byte-identical to the original-library branch, whose full solution command is the
backend proof; the combined frontend/build and four browser journeys are run in the integration tree.
This is an implementation checkpoint for #2808, not completion of the entire prototype expansion.

## Final integration recovery (2026-09-10)

Hosted smoke on head 9bac8e647 passed 207 journeys, skipped thirteen existing opt-ins, and failed two old locators expecting the retired Leave unsaved thinking? dialog title. Both now target the actual Leave this thinking space? dialog; the recovered seven-test contextual/overhaul browser batch passes against an isolated real API. Assertions still verify draft preservation, explicit discard, review/apply and cross-experience behavior.

Question-layer removal now stays disabled while that question holds a private text/audio draft, including when the removal prompt was opened before the draft began. The user receives guidance to keep or explicitly discard it first. Eighteen targeted component tests and typecheck pass; the focused regression proves the child remains mounted until its draft clears. This closes the direct browser-only recording loss found in hosted comment3974598891. Physical microphone acceptance remains separate.
## Source interaction follow-through (2026-09-10)

The recorded permission/accessibility/session-refresh/retained-control findings are addressed without
changing chat dispatch or board-write authority. ChatOriginalContextApiTests now seeds 1,012 immutable
source versions and follows 1000 -> 1010 -> end through real HTTP/SQLite reads; pages remain ten entries
with 1,500-character excerpts. ChatOriginalSourcePicker, ChatContextPicker, ChatMessageList and
WorkspaceMemoryPreservation component tests exercise permissions, distinct names, same-owner refresh,
logout and blocked continuation. The updated original-source-context browser journey creates two
memories, selects one by its accessible name, checks 403 explanation/recovery, replays exact receipts
across all four experiences and checks 375 px accessibility. Companion continuity is rerun alongside it.
Exact results are recorded with the continuation PR; live providers and physical-device acceptance are
not inferred from this deterministic scope.

## Calendar-day planning and Classic resume (2026-09-10)

The workspace-plan Chromium journey now runs in America/Los_Angeles with an en-US locale. It asserts a midnight-UTC October20 card deadline remains October20, the Today filter selects the browser-local calendar date, and saved thinking resumes through Classic Home in both Grove and Legacy. The existing List/Board/Horizon, Focus, Make Room, four-experience,375px/no-overflow and accessibility checks remain, including unchanged card data. The final journey passed in17.6seconds. Home/date/store targeted tests passed25cases; full frontend, typecheck/build and scopedESLint results accompany the continuation PR.

An initial typecheck caught a remaining Today reference to the removed local helper; it was replaced with the shared utility. The new Today browser assertion initially matched both navigation and plan controls; its final locator is scoped to the personal plan. Failed evidence is retained, and behavior assertions remain strict. This does not change card deadlines or the server Focus contract, and does not claim physical-device acceptance.

## Source-storage export snapshot (2026-09-10)

SourceExportSnapshotApiTests runs both account-export routes against a real SQLite WAL database. After the objects query has finished, a second authenticated HTTP request commits a complete audio upload and written version before the export reads the remaining sections. The active export contains none of that later graph across all five source-storage sections; a subsequent export contains the complete graph. Existing ThinkingAudioApiTests retain owner/access, source byte, correction, confirmation, export/erasure and library proof. Eleven targeted API tests pass; full backend results belong to the continuation PR.

The deferred transaction belongs to SourcePortabilityStore and is disposed before subsequent export sections or audit writes. Buffered source byte checks remain active inside the snapshot. This does not assert a single snapshot across unrelated account sections, external object stores, production providers, physical-device workflows or restoration.

## Expanded integration qualification (2026-09-10)

PR #2866 now also contains the reviewed source interaction, board overlay, library/413, indexed history, calendar/Classic resume and source-export snapshot continuations (#2870, #2872 through #2877). All runtime merges were automatic. The final combined runtime passed `dotnet test backend/Taskdeck.sln -c Release -m:1`: 9,186 passed and 34 existing skips. Full frontend verification passed 6,377 tests with three existing skips across 412 files; typecheck and production build passed.

Sixteen selected Chromium journeys have passing evidence. The initial combined batch passed fifteen and found one ambiguous memory-title locator; using the checkbox role fixed that test, and both complete contextual/source specs then passed. Coverage includes four experiences, both board renderers, Los Angeles calendar dates, Classic resume, 375 px accessibility/overflow, source permissions, exact original audio, explicit Review/Apply and three production audio-policy cases. This is not a claim that the initial batch was entirely green.

Two comparison identity checks, two audio-policy checks, eleven failure-ledger projection tests, documentation links and GitHub governance pass. A bounded fresh-context integration review found no HIGH/CRITICAL interaction defect. Temporary browser services are stopped; all databases are synthetic. Hosted exact-head qualification remains the merge gate. The wider #2808 scope, physical microphones/devices, live processors, restoration and the existing human decisions remain open.
