# CF-17 — Meeting understanding bundle: speaker mapping, actions/decisions/questions/risks registers, grouped review, conflict checks (#2271)

Last Updated: 2026-09-02

> Curated from the v0.6 acceleration bundle (grounded `c27283fb2`, 2026-08-30) against `main` `79dd57cd3` on 2026-09-02 under tracker #2368. Planning input, not authority: the live issue, ADR-0065/ADR-0057 and `docs/architecture/CONTEXT_FABRIC.md` win. Corrections to the bundle are listed in the last section.

## Outcome

Turn diarised meeting candidates into participant-aware, evidence-linked registers — actions,
decisions, questions, risks — reviewed as one grouped capture, with conflicts surfaced as facts.
Speaker identity is **never inferred**: a label resolves only through an explicit alias to an
authorised participant, or stays unresolved. No new work-item types; registers are read models over
CF-08 candidates.

## Live dependencies (verified 2026-09-02)

| Issue | State | What it must deliver first | Unblocks |
| --- | --- | --- | --- |
| CF-08 `#2262` candidates | open (umbrella, unsplit) | `SemanticCandidate` + revisions + typed kinds; registers are a projection of these | 02, 03, 04, 05 |
| CF-09 `#2263` resolver | open | context binding / target board at plan time; boardless captures | 02, 03, 06 |
| CF-14 `#2268` WhisperX route | open | diarised transcript with speaker labels and time segments | 01, 02 |
| CF-21 `#2274` presentation profiles | open | Flow / Guided / Control extension points the grouped review renders into | 06 |
| `#2093` participants | open | the participant/assignment substrate — **and it has a live design fork** (below) | 01, 03, 05 |
| CF-07 `#2261` anchors | open (implicit) | `EvidenceAnchor` (`TimeRange`) so every register row cites evidence | 02, 07 |
| CF-16 `#2270` voice UX | open (implicit) | audio evidence playback named in the live issue scope but absent from its dependency list | 06 |

**The `#2093` fork.** `docs/analysis/2026-08-30-acceleration-bundle/RECONCILIATION.md` §Work model
records it: ADR-0060/ADR-0062 are accepted, but Taskdeck **has no existing single-assignee field to
generalise**, and `#2240` must settle the tracker contract before `#2093` is implementation-ready.
Verified on `main`: `backend/src/Taskdeck.Domain/Entities/Card.cs` has `BoardId`, `ColumnId`, `Title`,
`Description`, `DueDate`, `IsBlocked`, `BlockReason`, `Position` — no assignee, and there is no card
relation entity. Every CF-17 slice that mentions assignment or dependency conflicts is behind that fork,
not merely behind `#2093`.

## Child slices (one PR each, in order)

| id | Outcome | Depends on | Mode | Startable now? |
| --- | --- | --- | --- | --- |
| `V06-CF17-01-speaker-aliases` | Board-scoped speaker-label → authorised-participant aliases | — | contract-only | **Partly.** The alias record and the explicit-only resolution order can be frozen against shipped `Board.OwnerId` + `BoardAccess`; the alias *target* semantics wait on `#2093`/`#2240` |
| `V06-CF17-02-register-readmodel` | Content-safe decision/question/risk register read model | 01 | implementation | No — CF-08 candidates do not exist |
| `V06-CF17-03-action-mapping` | Speaker assignment hints carried into proposal review only | 01, 02 | implementation | No — no assignment substrate |
| `V06-CF17-04-existing-match` | Bounded existing-work match candidates with reasons | 02 | implementation | No — CF-08 |
| `V06-CF17-05-conflict-facts` | Non-blocking assignment / dependency / due-date review facts | 02, 03 | implementation | No — no assignee, no relation entity |
| `V06-CF17-06-grouped-review` | Capture-centred meeting review through CF-21 extension points | 02, 05 | implementation | No — CF-21 |
| `V06-CF17-07-portability` | Export/import registers, aliases, evidence references | 02, 05 | implementation | No |

## Architecture

**Speaker resolution (the invariant).** `SPEAKER_00` is a label, not identity. Order: explicit alias
for this recording → explicit board-scoped alias → **unresolved**. No name similarity, no email guess,
no voiceprint, no LLM inference may ever produce a `UserId`. Two aliases disagreeing on one label is
`speaker.alias-conflict`, not a tie-break. The alias target must satisfy the participant predicate at
**write time and read time** — shipped predicate: `Board.OwnerId == userId` **or** a `BoardAccess` row
(`Domain/Entities/BoardAccess.cs`: `BoardId`, `UserId`, `Role`, `GrantedBy`, `GrantedAt`). A board owner
has no `BoardAccess` row, so an access-table-only check is a bug — the bundle's test for this is right.

**New records (all new):** `SpeakerAlias`, `MeetingRegisterEntry`, `ExistingWorkMatch`,
`MeetingConflictFact`. **New services:** `ISpeakerAliasResolver` (explicit only),
`IMeetingRegisterProjector`, `IExistingWorkMatcher` (candidates, never silent dedupe),
`IMeetingConflictAnalyzer` (stable codes).

**Existing surfaces to extend, not duplicate:**

| Concern | Existing file | Note |
| --- | --- | --- |
| Review shell | `frontend/taskdeck-web/src/views/paper/PaperReviewView.vue` | Thin shell; the grouped meeting view is a section, not a rival route |
| Conflict rail | `frontend/taskdeck-web/src/views/paper/review/ReviewConflicts.vue` | A conflict-facts surface **already exists** — extend it |
| Evidence playback | `frontend/taskdeck-web/src/components/review/TranscriptEvidenceViewer.vue` | Transcript/time evidence is shipped; audio playback is CF-16 |
| Provenance | `components/review/ProvenanceDrawer.vue`, `views/paper/review/ReviewProvenance.vue` | Register rows cite provenance through these |
| Proposal loop | `Application/Services/AutomationProposalService.cs`, `AutomationPolicyEngine.cs` | Actions compile into proposal operations; registers never mutate work |
| Portability | `Services/DataExportService.cs`, `BoardJsonExportImportService.cs`, `ExportImportService.cs`, `AccountDeletionService.cs` | Every new table joins all four |

**Boundary rules.** Registers are read models keyed by candidate + candidate revision; a rerun that
produces new labels must not silently rewrite an existing register row's identity. Conflict facts are
evidence for a human, never authorisation to alter work. Frontend state is explanatory; server-side
ownership checks run before any presentation filter. Persist stable codes, not free text — a register
row stores candidate/anchor ids, not quoted meeting content.

## Implementation plan

**Preflight.** Read `#2271`, `#2093`, `#2240`, ADR-0060/ADR-0062 and the §Work model fork; confirm
CF-08's candidate schema is merged and not draft; confirm CF-21 exposes named extension points; run the
frontend and Application focused suites before editing.

| Path | State | Owner |
| --- | --- | --- |
| `backend/src/Taskdeck.Application/Meetings/` | to be created | producer |
| `backend/tests/Taskdeck.{Domain,Application}.Tests/Meetings/` | to be created | producer |
| `frontend/taskdeck-web/src/components/review/meeting/` | to be created — **use this, not `src/features/`** | producer |
| `frontend/taskdeck-web/src/tests/components/review/meeting/` | to be created (`src/tests/components/review/` exists) | producer |
| `frontend/taskdeck-web/src/views/paper/review/ReviewConflicts.vue` | exists | integration owner |
| `frontend/taskdeck-web/src/views/paper/PaperReviewView.vue` | exists | integration owner |
| `backend/src/Taskdeck.Domain/Entities/Card.cs` | exists — **do not add an assignee here**; that is `#2240`/`#2093` | integration owner |
| `backend/src/Taskdeck.Infrastructure/Migrations/TaskdeckDbContextModelSnapshot.cs` | exists | integration owner |

**Rollout / rollback.** Registers ship read-only and advisory; disabling the projector hides the view
without deleting rows. Aliases are user data — rollback never drops the table.

**Definition of done.** Speaker identity never inferred (a test proves each rejected inference route);
registers are read models, not `Card` types; export/import round-trips aliases, register identity and
evidence references; account deletion removes aliases and registers; the three live acceptance boxes are
traced to tests.

## Test plan

- [ ] Unmapped speaker stays unresolved; no candidate is created with a guessed `UserId` (Domain).
- [ ] Alias to an unauthorised or revoked participant fails server-side (Application).
- [ ] Board owner with **no** `BoardAccess` row resolves as a participant (Application).
- [ ] Two aliases for one label → `speaker.alias-conflict`, no resolution (Domain).
- [ ] Decision / question / risk candidates compile to **zero** work operations (Application).
- [ ] Existing-work matches scoped to boards the caller may read; cross-board hostile read denied (Api).
- [ ] Conflict facts never block proposal creation or apply (Application).
- [ ] Registers preserve candidate revision and evidence anchors across a rerun with changed labels (Application).
- [ ] Export/import retains aliases, register identity and evidence ids; account deletion removes them (Application + Api).
- [ ] Grouped review renders unresolved speakers explicitly under Flow / Guided / Control (frontend spec).

Commands: `dotnet test backend/tests/Taskdeck.Application.Tests/Taskdeck.Application.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~Meeting"`;
`dotnet test backend/tests/Taskdeck.Domain.Tests/... --filter "FullyQualifiedName~SpeakerAlias"`;
`cd frontend/taskdeck-web; npx vitest --run --maxWorkers=2 src/tests/components/review/meeting/<spec>.spec.ts`
(bare `vitest --run` OOMs on this box).

## Edge cases

Speaker labels change across rerun · two labels map to one participant (legal; one participant may hold
several) · one label deliberately left unresolved · participant access revoked after review opens · the
same action appears in two meetings · due date missing or ambiguous · a referenced relation is later
deleted · a meeting capture spans boards or has no target at all (boardless is mandatory, ADR-0065
§Decision 12) · owner deleted mid-projection · export during an in-flight rerun · maximum-length and
empty speaker labels · numeric-string enum tokens for register kind.

## Reference material

| Kind | Archived path | Good for | Caveats |
| --- | --- | --- | --- |
| C# candidate | `docs/analysis/2026-08-30-acceleration-bundles-v0.5-v0.6/v0.6/candidates/csharp/MeetingUnderstanding.cs` | `SpeakerAliasResolver` explicit-only resolution and its four reason codes; register/conflict record shapes | Namespace `Taskdeck.Acceleration.V06`; `MeetingRegisterEntry.State` is an untyped string; assumes an authorised-participant set that no shipped service produces |
| TS candidate | `.../candidates/typescript/meetingRegisterModel.ts` | Grouping + `hasUnresolvedSpeaker` presentation logic | Plain TS, not a Vue composable; place under `components/review/meeting/`, not `src/features/` |
| TS candidate | `.../candidates/typescript/profileVocabulary.ts` | Flow/Guided/Control migration from `guided \| workbench \| agent` (`#1972`) | Owned by CF-21, not CF-17 |
| Diagram | `.../diagrams/meeting-understanding.svg` | Capture → registers → proposals → conflicts → evidence flow | Advisory |
| Bundle doc | bundle `03_ARCHITECTURE/MEETING_UNDERSTANDING.md` | Resolution order, register read-model axes, matching signals, portability list | Read with the corrections below |

## Corrections to the bundle

1. **`src/features/meeting-review/` is not a Taskdeck convention.** `frontend/taskdeck-web/src/` has no `features/` directory; review UI lives in `components/review/*` and `views/paper/review/*`, and specs in `src/tests/components/...`. Adopting the pack's paths would fork the frontend layout.
2. **A conflict surface already exists.** `views/paper/review/ReviewConflicts.vue` is shipped; the pack plans conflict rendering as if it were greenfield. Extend it.
3. **Assignment conflicts have no substrate.** `Card` has no assignee and there is no card-relation entity, so "same participant overloaded" and "due date precedes declared dependency" cannot be computed today. The pack lists `#2093` as an ordinary predecessor; per the previous reconciliation's §Work model, `#2240` must settle the contract first. Slices 03 and 05 are behind a *design* gate, not just a code gate.
4. **`Card.cs` as a coordinator seam is misleading.** CF-17 must add nothing to `Card`; naming it a seam invites an assignee column that belongs to `#2240`.
5. **CF-16 `#2270` and CF-07 `#2261` are missing from the dependency table** even though the live issue's scope requires evidence playback and every register row must cite an `EvidenceAnchor`.
6. **Registers project candidates that do not exist.** No `SemanticCandidate` entity is on `main` — only the `SemanticCandidateKind`/`SemanticCandidateState` enums in `backend/src/Taskdeck.Domain/Enums/`. Every "read model over candidates" slice is CF-08-blocked in full, not partially.
7. **Vocabulary is correct** — Flow/Guided/Control, no "Controlled", no invented authority terms; `profileVocabulary.ts` maps the retired `workbench`/`agent` selectors onto `control`, matching ADR-0065 §Amendments ruling 9. Nothing to fix here.
