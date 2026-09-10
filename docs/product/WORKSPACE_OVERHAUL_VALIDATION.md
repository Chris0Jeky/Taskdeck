# Workspace overhaul validation and follow-through

## Hosted delivery receipts (2026-09-10)

The source manifest was rechecked against the original local pack: all 20 paths, byte lengths and
SHA-256 values match, with no additional or missing files. PR #2866 merged as `384d8dfaa` after its
required gate. PR #2886 merged as `f00cf1d31` at 08:10 UTC after required run `34450719109` passed
at reviewed head `fce97dc67`, including Ubuntu/Windows API and unit suites and final browser smoke.
All prior continuation heads are included. The #2886 merge receipt has the same tree as that tested
head; integrating it into #2892 changes no runtime files. Reminder hours remain subject to their own
final exact-head hosted gate. These receipts supersede earlier pending-hosted notes for #2886.

## Reminder work-hours and time zones (2026-09-10)

After integrating the final transcription and grounded-question continuation, 56 targeted API tests
and 56 component tests pass, alongside typecheck and production build. Five combined Chromium
journeys pass in 36.3 seconds, including actual synthetic speech transport, planning and reminder
hours. The hours journey also reloads Grove and Grove Night at mobile width and checks accessibility.

A real account-erasure race first reproduced a failed expectation (HTTP 200 instead of 409): an
in-flight request could recreate default preferences after deletion and then save private hours.
The conditional database update now also requires an active account. The same regression passes
within the final 56-case API run, preserving an empty, disabled default preference after erasure.
This is a scoped repair after full-suite qualification, not a claim of another full-suite run.

Twenty-five domain cases cover existing budget/spacing behavior and the new named-zone window:
start/end boundaries, selected days, overnight carry, seasonal and repeated DST hours, invalid values
and unchanged daily limits. Eight API cases prove save/read/export, outside-window no-budget behavior,
inside-window claims, old-client toggle preservation, explicit clearing, stale revisions and invalid
configuration. Sixteen component cases cover payloads, disabled saving controls, uncertain-save reload
and invalidation of in-flight reminders when a loaded window changes.

All 6,421 frontend tests pass with three existing skips across 415 files. Typecheck, production build
and scoped ESLint pass. Both Chromium journeys pass in 40 seconds; the final control styling has a
separate passing 26.1-second settings journey. Both Firefox journeys pass in 34.1 seconds. They cover
opt-in/quiet-time/budget behavior and saved hours across all experiences, reload, concurrent settings
recovery, 375px overflow and scoped accessibility. The mobile screenshot was inspected. Firefox was
installed through the repository's existing Playwright package. The temporary override selected the
same tests under Desktop Firefox; it is retained in local evidence, not production configuration.

The first temporary Firefox config was placed beneath node_modules, where Node refuses TypeScript
stripping. Moving the config into the frontend root resolved the runner setup; no product fix was
needed. Temporary configs and services were removed from the active checkout after proof. The bounded
independent review is clean. Full backend and hosted outcomes follow when complete; no schema change,
external provider, physical-device or subjective usefulness claim is made.

The complete backend run passed 9,270 tests with 34 existing skips: Domain 1,664, Application 4,276,
API 3,052, CLI 243, Architecture 28 and Integration 7. This qualifies the reminder slice before its
parent refresh; the larger transcription/grounded continuation has its separate proof below. Hosted
qualification remains the final merge gate.

## Final integrated continuation qualification (2026-09-10)

The continuation combines grounded commit recovery, thinking-route exit and explicit transcription,
including destination-bound consent. A real SQLite write-lock regression distinguishes storage
contention from changed evidence, detaches rejected candidates and preserves accounted model usage;
18 application and 17 API/reminder cases pass for that slice.

The full backend run at `ea8c0cbe6` passes 9,281 tests with 34 existing skips and one worker-wait
failure: its bounded queue read was not observed within the test's waiting period. The worker and
test are unchanged from the baseline. The final 30 API/worker cases pass in isolation, including
that case and the reviewed account-erasure repair. Full-run per-project passes are Domain 1,645,
Application 4,301, API 3,057, CLI 243, Architecture 28 and Integration 7. The initial run is not
claimed green.

The first full frontend run passes 6,427 tests and exceeds a five-second startup-test timeout.
That unchanged test passes in isolation; the complete rerun with four workers passes all 6,428
tests with three existing skips across 416 files. Typecheck and production build pass. Ten combined
Chromium journeys pass in 1.3 minutes using an isolated database and actual synthetic speech HTTP
transport: transcription/recovery, manual audio, retained originals, both planning journeys,
attention, grounded evidence, Companion continuity, contextual companion and board previews.

The bounded combined review identified a HIGH account-erasure race: a surviving board owner ID
could authorize a late grounded-analysis save after the user's account was deactivated. Commit
`948751cab` adds the active-user predicate to every source read, including the transaction's final
check. The real regression deletes an account after the final service read while a co-owner retains
the board; the response conflicts and no insight is recreated. The independent fix review confirms
resolution with no remaining HIGH/CRITICAL. Hosted exact-head qualification remains separate.

## Grounded-question commit recovery (2026-09-10)

The source/access fingerprint is checked again inside the serializable transaction that persists staged observations. Four real API races change evidence, archive/delete the source or revoke membership just after the final service read; all return conflicts and persist no candidates. A separate repository test rejects staged questions and then performs another save, proving rejected state cannot leak through the scoped context.

Usage settlement precedes question staging. A failed commit produces a known no-save result without retrying usage; a failed release does not replace the provider's original cancellation. Seventeen application tests and sixteen API/reminder tests pass. The first application-test compile used the wrong mock argument type for the existing quota method; correcting the fixture to its integer signature resolved compilation. Full backend verification passes 9,240 tests with 34 existing skips: Domain 1,641, Application 4,275, API 3,046, CLI 243, Architecture 28 and Integration 7. Bounded independent review is CLEAN; documentation links/governance and diff checks pass. Hosted qualification remains separate. No frontend, migration or external-provider change is included.

## Thinking route exit recovery (2026-09-10)

Leaving a thinking route invalidates pending receipts and clears view state before checking route IDs. Incomplete params no longer request an empty board endpoint; complete reentry loads normally. Six continuity component tests and typecheck pass, including delayed responses after route exit. Both full personal-plan Chromium journeys pass in 47.2 seconds with per-page assertions that no empty-board request occurs, plus existing Focus, recovery, four-experience, calendar, mobile and accessibility checks. Full frontend verification passes 6,420 tests with three existing skips across 415 files. Production build, typecheck, scoped ESLint and bounded independent review pass. Hosted qualification remains separate. Backend behavior is unchanged.

## Explicit audio transcription (2026-09-10)

Four domain cases cover attempt deadlines and UTC admission. Twenty-five application cases cover accepted formats, single multipart dispatch, bounded output, invalid responses, cancellation and disabled/unsafe configuration. Eight real API cases cover separate provisional results, exact retries, competing requests, expired output, erasure during processing, deactivation, retained originals, both exports, account erasure and kill/configuration gates. Three actual localhost transport cases cover multipart bytes, disclosure/trace suppression, blocked redirects and the shared circuit; existing audio/export cases bring the focused run to 24 passes. A later reviewed-draft provenance extension passes 22 API/transport/audio cases.

The first API compile exposed the snapshot-export test decorator's missing new interface methods; forwarding them fixed compilation. The first transport run exposed an unclassified egress redirect rejection; the adapter now returns a content-free failure receipt, and all 24 cases pass. The application fixture needed an explicit Xunit import. No live provider or paid request was involved.

Thirty-three component tests, typecheck and scoped ESLint pass. Two old library text assertions were updated for the new ability to request a transcript from retained originals while keeping them separate from answers. The three real-API Chromium journeys pass in 41.8 seconds: automatic transcription through actual synthetic HTTP transport, existing audio upload/confirmation recovery, and the retained original library. The new journey proves provider failure, explicit retry, lost success response/reload, reviewed draft provenance and confirmation, all four experiences, 375px overflow and scoped accessibility. The mobile screenshot was inspected; temporary listeners 5244,5346,5349 are stopped. Full-suite, migration and independent/hosted outcomes remain pending here.

Full frontend qualification passes 6,425 tests with three existing skips across 416 files; production build passes. The full backend run passes 9,272 tests with 34 existing skips and catches one architecture inventory failure: the new protected speech client/registration were not listed among known outbound sites. Their direct transport/envelope proof already passes; adding those two legitimate entries resolves the inventory seam. Domain 1,645, Application 4,298, API 3,052, CLI 243 and Integration 7 all pass in that full run; 27 architecture tests pass before the inventory correction. This records a corrected full-run failure rather than claiming the original run was green. The new migration applies to an isolated empty SQLite database and EF reports no pending model changes. Bounded backend review finds no HIGH/CRITICAL; its two MEDIUM limits are recorded in [AUDIO_TRANSCRIPTION.md](AUDIO_TRANSCRIPTION.md). Final scoped fix and frontend review evidence follow on the PR.

Final backend correction passes all 28 architecture tests (one existing skip) plus 22 transcription/audio/transport API cases. Confirmation warnings now retain truthful transcription provenance and point to the reviewed-parent lineage instead of claiming no automated transcription occurred. No behavior change to consent, provider dispatch or frontend follows its full run. Hosted qualification and physical/live-provider usefulness remain separate.

The separately configured disabled-provider Chromium run also passes (one journey, 23.4 seconds including startup), proving default-off options across all four experiences without a speech fixture or external request. This is additional to the three configured-provider/audio/library journeys above.

The frontend review found a HIGH consent defect: refreshing options after a provider configuration change preserved a checked checkbox. The final fix binds consent to the displayed configuration hash and clears it on any change, including A→B→A. The regression proves dispatch remains blocked until fresh consent and an unchanged configuration can retain deliberate consent. All 34 affected components, typecheck and scoped ESLint pass after the fix; the bounded independent fix review confirms resolution with no remaining HIGH/CRITICAL. This later change supersedes the earlier statement that frontend consent was unchanged after the full run.

## Expanded follow-through qualification (2026-09-10)

The combined runtime at 6c6a344a4 includes optional reminders, playback binding, exact confirmation receipts, retained confirmation labels, both moved-card lane markers and the fragmented-upload repair. Full backend verification passed 9,232 tests with 34 existing skips: Domain 1,641, Application 4,273, API 3,040, CLI 243, Architecture 28 and Integration 7. Full frontend passed 6,418 tests with three existing skips across 415 files; production build and typecheck passed.

Ten combined Chromium journeys passed in 2.1 minutes, covering reminders, both board renderers, audio/library recovery, original source conflicts, grounded questions, companion continuity and both personal-plan flows. The separate comparison compatibility journey passed in 21.4 seconds. Both additive migrations were applied together to an isolated empty SQLite database, and EF reports no pending model changes. The latest attention migration's target model retains the earlier confirmation receipt. A bounded combined interaction review found no HIGH/CRITICAL defect.

The final filename-only recovery patch normalizes the raw validated name for both storage and retry comparison, rejecting whitespace-only names before storage. Eleven audio API tests and a bounded independent review pass at 66b082a8d; the merged product tree is identical to that tested input. It follows the combined full-suite pass without altering frontend or migration inputs. The retained-library hard-delete label is proved by the real SQLite API and component tests; the browser archive journey is not misreported as physical deletion.

All local services are stopped and all databases are synthetic. Exact-head hosted CI and the moving main base remain delivery gates. Automated transcription, general representation backfill/recall, complete restoration, real-provider/device usefulness and OUTSTANDING_TASKS.md owner decisions remain open.

## Audio playback receipt binding (2026-09-10)

Playback responses are bound to the original recording ID and both source/request generations. Changing the source invalidates the request and revokes its object URL; late success, failure and cleanup cannot overwrite a newer request. Playback is unavailable until the current receipt has loaded successfully.

Sixteen component tests, typecheck, production build and scoped ESLint passed. Full frontend: 6,403 passed, three existing skips, 414 files. The Chromium audio journey passed in 17.1 seconds, including a controlled stalled original response, question edit, discarded old playback, restored question, playback recovery, lost-upload receipt retry, explicit written confirmation, all four experiences and narrow-screen accessibility. A bounded independent review found no HIGH/CRITICAL defect. Backend behavior is unchanged; no hardware microphone or provider acceptance is claimed.

## Optional reminders (2026-09-10)

The default-off account preference and shared server budget permit quiet board-page links to existing revalidated questions. No implicit analysis/model call is added. Two domain and three API tests pass; initial API compilation caught an incorrect test DbSet name, corrected before the three API cases passed. Thirteen component tests, typecheck/build and the 8.5-second Chromium consent/Zen/budget/mobile/accessibility journey pass. The first browser fixture had an automatic-semicolon-insertion error, corrected without changing runtime assertions. Screenshot inspected.

The full frontend run passed 6,382 tests with three existing skips and exposed 31 failures in an old shell fixture with incomplete mocked route data. Isolating the independently tested reminder child in existing shell/view fixtures restores their intended seam; all 102 tests across the four affected suites pass. No product logic was changed for that fixture repair. The full backend passed 9,224 tests with 34 existing skips and exposed three architecture-parser failures on the new controller declaration. Using the repository's conventional controller constructor/declaration preserves authorization and resolves all three: final architecture tests pass 28 with one existing skip and all three reminder API tests pass. A temporary browser API initially locked rebuild outputs; stopping that synthetic service permitted the final API rebuild. Bounded independent review found no HIGH/CRITICAL defect. Hosted outcomes remain separate. See [attention policy](WORKSPACE_ATTENTION.md) for limits and subjective acceptance boundaries.



## Combined continuation qualification (2026-09-10)

The continuation combines comparison appearance, uncertain-plan recovery, accepted-navigation Focus, retained audio drafts, source-selection conflicts, storage startup validation, grounded questions and receipt/Review recovery. All four experiences use the same services and stored data.

At combined runtime 652ee5d1a, the full backend passed 9,222 tests with 34 existing skips; the full frontend passed 6,396 with three existing skips. Typecheck/build and ten Chromium journeys passed, covering comparison, two plan flows, audio/library/history, grounded preview, companion continuity, board overlays and contextual companion. Grounded provider transport has separate synthetic-localhost proof on its input PR. The later frontend-only receipt/Review patch has the full-run and final 73-test/final two-browser proof recorded above; backend inputs remain unchanged. The subsequent main merge changes only CI scheduling, with 261 passing continuation/workflow contract tests and a clean bounded base interaction review. Combined interaction review found no HIGH/CRITICAL defect.

Exact-head hosted CI and merge state remain on the delivery PR. Local proof does not imply real microphone, external model usefulness, physical-device, full restore or release acceptance.



## Receipt and Review recovery (2026-09-10)

Post-send receipt reconciliation has a 15-second HTTP timeout and disables automatic retries. A timeout preserves the successful message and offers an explicit GET-only Retry. Legacy Review exposes eligible board previews for Chat and Manual proposals even without capture provenance; status and read-only guards remain enforced.

Validation: production build and typecheck passed. Full Vitest ran 6,399 passing tests, three existing skips and one stale request-options assertion; the assertion was updated for the new timeout, then all 73 tests across the four affected suites passed. Scoped ESLint passed. Two Chromium journeys passed in 49.5 seconds, including a genuinely stalled receipt request, exactly one message POST, explicit read recovery, both Review renderers and narrow-screen accessibility. Bounded independent review found no HIGH/CRITICAL defect. No backend behavior changed; hosted qualification remains pending on the continuation PR.
## Exact audio confirmation receipts (2026-09-10)

New confirmations retain a SHA-256 fingerprint of the original expected recording revision, expected deck revision, representation ID and answer status under a versioned schema. An identical retry returns the existing result; changing any field returns a conflict before another memory or representation is written. Pre-migration confirmations have no provable request fingerprint and require a read rather than accepting an unverifiable write retry. Their retained originals and read access remain available.

The nullable receipt is included in both account export routes. The additive migration was applied successfully to an isolated empty SQLite database. Ten API tests passed, including all four mismatched fields, exact retry identity, unchanged representation count, both exports and a legacy null receipt. Full backend qualification passed 9,223 tests with 34 existing skips. Exact-head hosted state belongs to the continuation PR. Independent review found no HIGH/CRITICAL defect. No working database was migrated.

## Fragmented upload bounds (2026-09-10)

The byte store fills its existing 64 KiB buffer before inserting a chunk, except for the final short tail. This prevents tiny network fragments from creating a database row each while retaining bounded memory, size validation, content hashing and savepoint rollback. Four new fragment/mismatch cases and all selected blob, audio and source-export regressions pass: 24 API tests. This scoped repair follows the earlier full backend qualification; exact-head hosted checks remain required.



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

## Comparison appearance attribution (2026-09-10)

Auto observations use the existing version-3 theme string to retain both selected mode and resolved appearance at submission: auto (paper) or auto (paper-night). Old auto labels remain unchanged and separate; no version-2 checksum input, file schema or historic identity changes. The UI explains the submission-time scope and asks the observer to note any appearance changes during the task.

The HTTP LAN compatibility Chromium journey records both live color-scheme settings, confirms the body theme, exports those labels alongside a legacy auto observation, checks three separate result rows, reloads and imports all three without SubtleCrypto or randomUUID. It passes in 14.6 seconds. Node fingerprint checks verify that src/tests and build/*.node-check.mjs changes leave product attribution stable, while runtime source, public assets, build plugin code and build configuration change it. Full frontend/build results belong to the continuation PR; there is no telemetry, randomized assignment or statistical conclusion.
Final local comparison-attribution gate: 6,377 frontend tests passed with three existing skips across 412 files. Production build/typecheck, two Node identity checks, the 14.6-second Chromium journey, documentation links and GitHub governance pass. Independent bounded Luna review is CLEAN. The test-only fingerprint exclusions are exact repository paths; this is still an input fingerprint, not a byte-for-byte bundle hash or proof of the remote server version. Temporary services are stopped; hosted checks remain separate.

## Personal-plan operation recovery (2026-09-10)

Shared plan reads coalesce across Home and plan consumers, with account changes detaching the old request so its cleanup cannot release a new account's pending read. Store tests exercise that ordering and both uncertain save/Focus failures. Failed mutations remove displayed plan metadata and block subsequent writes until an explicit successful refresh. Empty board/card Focus targets are validated before repository access; real API tests preserve the complete prior plan, revision and last-worked timestamp.

The browser recovery journey simulates a rejected Focus response and a committed plan edit whose response is lost. It verifies retracted entries, disabled controls, explicit refresh reconciliation and exactly one write. The first run exposed the shared client's automatic PUT retry; plan writes now use the existing retry opt-out. The full successful planning journey remains alongside this failure proof. Final counts and hosted qualification belong to the continuation PR. This does not yet change when navigation records Focus or assert physical-device acceptance.
Final local plan-recovery qualification: 9,188 backend tests passed with 34 existing skips; 6,380 frontend tests passed with three existing skips across 412 files. Production build/typecheck, ten targeted store cases, scoped ESLint, documentation links/governance and diff checks pass. Both Chromium journeys pass in 27.3 seconds, including the lost-after-commit response, exactly one write, explicit refresh, complete successful plan/Focus/Classic continuity and mobile proof. One bounded independent Luna review is CLEAN, including the HTTP retry opt-out. Temporary services are stopped; hosted qualification and accepted-navigation Focus timing remain separate.

## Accepted Focus navigation (2026-09-10)

`usePersonalPlanFocus` connects Home and plan controls to one accepted-route check before calling the existing revision-guarded Focus API. Six real memory-router cases cover delayed acceptance, origin disposal with the router app retained, aborted/login/account guards, leaving while the save is pending and unavailable plan state. Together with the store cases, sixteen targeted tests pass. The first fixture incorrectly destroyed the whole router application when trying to simulate disposal of its source component; the corrected fixture models the actual surviving app and passes.

The two real-API planning journeys pass in 21.5 seconds, including failed-history recovery from the thinking destination and successful Classic/other-experience resume. The destination banner directs the user back to refresh the plan; no late save redirects them. Full frontend/build and independent review results belong to this continuation. Browser logs also expose the existing thinking-route watcher attempting an empty-board read while leaving; it does not navigate or mutate and remains a bounded follow-through item under #2808.
Final local Focus navigation qualification: 6,386 frontend tests passed with three existing skips across 413 files. Sixteen targeted router/store cases, production build/typecheck, scoped ESLint, documentation links/governance and diff checks pass. Both real-API Chromium journeys pass in 21.5 seconds. Independent bounded Luna review is CLEAN. Backend code is unchanged from #2880; temporary services are stopped. Hosted qualification remains separate.

## Written audio draft recovery (2026-09-10)

Thirteen ThinkingAudioAnswer component cases include missing, replaced and confirmed receipts retaining the exact draft plus its original question evidence; explicit discard clears the guard, and failed reloads disable both mutation controls until recovery. Nine real API tests include changed and removed questions rejecting written versions, including an identical-text retry, without changing revision or representation history. Original bytes remain downloadable.

The original-library browser journey now edits the shared question while a private written draft exists, saves that edit, checks the accessible retained text and explicit discard, then continues through removed-question/library/playback/all-experience/mobile proof. The original audio save/retry/write/confirm journey remains alongside it. Final browser/full-suite evidence belongs to the continuation PR. These changes address hosted comments3974815071,3974815077 and3974815082; automatic transcription, physical devices and wider source operations remain open.
Final local written-draft recovery qualification: 9,186 backend tests passed with 34 existing skips; 6,381 frontend tests passed with three existing skips across 412 files. Thirteen focused component cases, nine real API tests, production build/typecheck, scoped ESLint, documentation links/governance and diff checks pass. The two Chromium journeys pass in 38.7 seconds, including the actual edited-question draft recovery and the original retry/confirmation/library flows. Independent bounded Luna review is CLEAN. Temporary services are stopped; hosted qualification and physical/provider acceptance remain separate.

## Original-source selection recovery (2026-09-10)

The shared original picker retracts items and emits an empty selection on HTTP 409, with explicit refresh guidance. Thirteen targeted component tests and typecheck pass. The real-API original-source Chromium journey changes a selected memory concurrently, receives the actual revision conflict on the next page, refreshes and reselects, and proves the outgoing context uses revision13. Receipt replay, all four experiences,403 recovery and375px accessibility remain covered; the journey passes in17.6seconds.

Full frontend verification passes6,378tests with three existing skips across412files; production build/typecheck, documentation links/governance and diff checks pass. This addresses hosted comment3975016183. No backend authority changes, implicit retrieval or automatic send are introduced. Temporary services are stopped; hosted qualification and physical/provider acceptance remain separate.

## Source-storage startup validation (2026-09-10)

Eleven focused API-project tests cover each quota at zero and minus one, default and large valid limits, the identity of the direct settings singleton and IOptions value, and actual API refusal to boot with OwnerQuotaBytes=0 before any upload. The first isolated DI fixture omitted the repository's standard synthetic connector encryption key; adding that fixture prerequisite made all eleven pass. No real credentials were used.

Shared infrastructure registers ValidateOnStart and preserves runtime validation for directly constructed settings. API/MCP hosts start their host; the CLI currently only builds and dispatches, so it validates source options when resolved rather than at CLI startup. No all-host startup claim is made. Independent bounded Luna review found no concrete API configuration-binding defect. Full backend verification passed 9,197 tests with 34 existing skips: Domain 1,639, Application 4,258, API 3,022, CLI 243, Architecture 28 and Integration 7. Hosted results remain the merge gate; there is no frontend change or browser requirement for this configuration seam. This addresses hosted comment3975016187; human deployment choices remain separate.

## Explicit grounded questions qualification (2026-09-10)

The one-card model question vertical passed fifteen contract/budget tests and eight real API cases. The initial full backend run passed 9,205 tests with 34 existing skips and found three MCP startup failures: the model-dependent observation service had been registered in shared infrastructure. Moving that registration into API model setup fixed the host boundary. The final scoped API/insight/MCP pass is 21/21, including all three original failures and the added membership-revocation interleaving. This records a corrected full-run failure, not a claim that the initial full run was green.

Full frontend verification passed 6,382 tests with three existing skips across 413 files. Final typecheck/build and fourteen focused frontend tests pass after the actionable HTTP409 message and theme-token correction. The browser initially exposed generic Axios conflict copy and then a localhost fixture configured with a blocked literal IP; the final standard localhost configuration passed the full real-transport journey in 13.6 seconds. It covers all experiences, exact source preview, actual stale submission409, provider transport, private saved questions, reload, private answer, 375px overflow and scoped accessibility. The screenshot was inspected. Services are stopped and the database/provider are synthetic.

No live-model usefulness, production provider, unattended attention, physical-device or broad semantic-recall acceptance is inferred. The usefulness corpus and two tracked non-blocking review limits are in [GROUNDED_OBSERVATIONS.md](GROUNDED_OBSERVATIONS.md). Exact-head hosted CI remains the merge gate. OUTSTANDING_TASKS.md owner choices remain open.
