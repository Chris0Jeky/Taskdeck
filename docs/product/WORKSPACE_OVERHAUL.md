# Switchable workspace overhaul

Last Updated: 2026-09-10

**Automatic transcription** is an optional, explicit step for a saved private recording. Open its
options and receipts in Thinking Decks or the original-recording library, inspect the configured
destination/model and consent to one request. A provisional result stays separate until reviewed as
a written draft and explicitly confirmed as an answer. Failure and lost-response receipts survive
reloads across all experiences. It is disabled by default; see [configuration, evidence and limits](AUDIO_TRANSCRIPTION.md).

**Preview on board** opens a read-only proposal layer from Chat or Review. Choose **Refresh board preview** to check the current proposal revision and highlight saved cards/columns affected by its operations in either board renderer. The checked diff also describes new or hidden objects. The board continues to show saved data; **Open Review** returns to that exact proposal for approval and explicit Apply. Closing the preview, changing board data, losing access or reaching the short freshness limit removes its markers. This reduces the need to mentally match proposal changes to board objects while preserving review-first trust.

Companion's source picker also offers individual preserved originals beside each private memory.
Open **Choose original sources**, inspect the saved excerpt, then select the exact answer or evidence
asset for the next message. Superseded answers are explicitly historical. Current memories and
originals share a five-item limit, and sources are sent to the configured model only when selected
for processing. Source receipts keep the asset fingerprint and memory version across reloads and
experience switches. Refresh choices after a correction or access change. Older memories without
native sources can first be preserved from Memory. No original is rewritten and board changes
still require Review/Approve/Apply.

Delivery tracker: [#2800](https://github.com/Chris0Jeky/Taskdeck/issues/2800).

The maintainer authorized this overhaul on 2026-09-08: map all supplied prototypes and chats,
implement alternate experiences over the working application, and include thinking, insights,
questions and memory in the delivery. The implementation is an additive experiment. The current
experience remains selectable and remains the default while the alternatives are evaluated.

## Source map

The local source folder is `filesAndResources/taskdeck-overhaul/`. It contains 20 files:
three self-contained HTML prototypes, three chat transcripts, three Unified handoffs, and eleven
PNG images. [The complete manifest](overhaul-resource-map.json) records every relative path,
byte length, SHA-256 and purpose. Raw chats and originals stay in the supplied local folder.

| Family | Files | Product intent | Distinctive behavior |
| --- | --- | --- | --- |
| Alternative Studio | HTML, chat, Focus and After Hours screenshots | Workspace first; continue where you stopped | Your day, personal plan, focus thread, Board/List/Horizon, proposed objects in context |
| Companion | HTML, chat, Review and Night screenshots | Conversation as a front door to actual work | Contextual sessions, source-backed change previews, Ask/Plan/Remember, Flow/Guided/Control disclosure |
| Unified | HTML, chat, three Markdown handoffs, seven screenshots | One board with progressively disclosed complexity | Zen/Studio/Control, Thinking Deck stack/path, Quiet insights, typed memory and source history |

The chats explain the evolution: Companion establishes conversational access; Studio asks for a
workspace that does not require conversation; Unified combines both with adjustable detail and
optional intermediate thinking. Their HTML demonstrates local interactions, not authenticated
Taskdeck integration. The downloads, separate source modules, test scripts and raw test reports
mentioned inside the chats are absent. Reported 30/60/84 test counts are historical author claims.

## Integration choices

Three independent preferences avoid turning prototypes into incompatible products:

| Preference | Values | Effect |
| --- | --- | --- |
| Experience | Classic, Studio, Companion, Unified | Entry point, navigation treatment and emphasis |
| Detail | Zen, Studio, Control | Shell spacing, optional card detail disclosure, and compact operational cards; Classic preserves existing board settings |
| Theme | Existing Paper/Legacy choices plus Grove and Grove Night | Prototype-inspired colors over the shared token system |

The overloaded name Studio is retained from the source, but experience and detail have separate
labels. Existing Guided/Workbench/Agent workspace modes remain separate. None of these controls
grants permission, changes processing policy, approves proposals, or applies changes.

One mounted route outlet, existing board/card IDs, shared stores and the current realtime connection
carry work across presentation changes. Capture and chat reuse existing services. Review remains an
explicit approve action followed by a separate apply action and the existing confirmation dialog.

## Feature ledger

| Capability | Integrated behavior | Status and limits |
| --- | --- | --- |
| Switchable experiences and prototype themes | Persistent preferences, one mounted route, four Home/navigation treatments, Grove/Grove Night | Implemented; Classic remains default. Board Zen disclosure and Control density preserve blocked/due/trust information |
| Thinking Deck | Ordered note/question/options/steps/thread layers, revision checks, stack/path, preserved alternatives, board JSON portability | Implemented; Path is a connected vertical reading view. Saved steps can explicitly create linked board cards. Unlinked checkboxes remain thinking-only; linked status is read from the real card |
| Quiet insights | Explicit analysis of blocked cards and unknown/needs-review private memory; durable dismiss/snooze/mute state and revalidation | Implemented structural rules only; no semantic/model inference, automatic scans or interruption |
| Questions and memory | Saved shared questions can have private answers; source snapshots, statements/assumptions/unknowns, corrections and archive/restore | Implemented. Thinking is board-shared; answers, memory and insights are private to the user. Original answer/evidence and correction history survive; archive excludes the active memory list |
| Memory portability and account privacy | Private JSON download plus both account export formats include originals, evidence, native source references and history; account deletion erases the private source graph | Implemented for active and archived records. Board-scoped download uses independent authenticated reads, not an atomic backup or import format. Native originals also survive physical board deletion in account export. Shared board exports exclude private answers; collaborators' records survive account deletion |
| Companion | Accountable chat beside card thinking/Focus, board-scoped conversations, explicit card/shared-thinking/private-memory and native original-asset sources, persisted receipts | Implemented across experiences. Up to five combined private memories/originals; permission/archive/revision/hash checks before dispatch and replay. Unsaved-thinking and pending-send guards plus receipt-only recovery preserve continuity. No implicit private retrieval or new provider. Checked board-object markers and read-only previews are available in both renderers |
| Comparison | User-initiated scenarios, explicit completion outcomes, ease ratings and notes retained through manual JSON export/import across releases | Implemented; version-3 files retain stable IDs, selected experience/detail/theme, backend version and frontend input fingerprint. Version-2 imports retain unknown frontend attribution. Atomic validation, deduplication and descriptive grouping; no telemetry, random assignment or statistical A/B claim. New Auto-theme entries retain their resolved light/night appearance; older records preserve unknown attribution |
| In-place proposal previews | Authoritative diff beside the card conversation, with the effective revision in the same response | Implemented read-only preview with expiry, bounded freshness and identity clearing. Open Review retains approval/subset/explicit Apply. Affected existing cards/columns are marked from a checked proposal receipt; proposed new objects remain in the authoritative diff |
| Personal continuity | Chosen plan, last-worked focus, List/Board/Horizon over planned cards | Implemented privately with revision checks. Home resume uses explicit focus and shows chosen threads; the separate agenda remains derived from Today. Requires a connected backend; backend-less demo builds hide these entry points |
| Linked steps | Explicit title/destination, atomic child-card/link/audit creation, repeat-safe requests, refreshed real card status and portable links | Implemented; shares the guarded card writer and board concurrency token. Removing thinking never deletes linked cards. Explicit dependency edges are also available |
| Dependencies | Explicit same-board prerequisite relationships with both directions, live status refresh and portable import | Implemented with cycle validation, revision conflicts, archive and deletion guards. No automatic edges or status/deadline changes |
| Audio answers and unified source evidence | Native Capture/SourceAsset question originals, explicit older-memory preservation, original audio and separate immutable written representations | Implemented for manual question answers: explicit recording/file intake, playback/download, stable upload retry, written history and confirmation, account portability/erasure and read-only retained library. Audio alone stays untranscribed; automatic transcription/failure processing and general legacy representation backfill remain next |
| Model-generated observations and recall | Explicit one-card preview and up to three quoted private questions with shared Chat budget, category/card deduplication and one-day freshness | Experimental question producer implemented; [contract and usefulness corpus](GROUNDED_OBSERVATIONS.md). General semantic candidates, recall and live-provider usefulness acceptance remain separate |
| Optional nudges | Explicit account opt-in, quiet links to existing questions, shared two-per-day/two-hour budget | Implemented experimentally with typing/dialog/Focus/Zen/visibility suppression and no automatic model work. [Policy and limits](WORKSPACE_ATTENTION.md); subjective usefulness/non-intrusion evaluation remains open |

Insight actions wait for active analysis to settle, Memory creation waits for the initial list, and Retry repeats failed board discovery before reading content. These guards prevent overlapping operations from stranding the workspace or hiding a newly saved memory.

Implementation above is the integrated branch behavior. [Follow-up delivery tracker #2808](https://github.com/Chris0Jeky/Taskdeck/issues/2808)
owns the remaining prototype capabilities. [Validation and remaining delivery work](WORKSPACE_OVERHAUL_VALIDATION.md)
records direct proving commands and limits; source screenshots are not
validation of the integrated application. The broader voice, model and attention work is not made
complete by introducing its UI vocabulary.

## Try the integrated experience

The shell's **Experience** and **Presentation** selectors remain reachable while working. Appearance
settings also expose the selectors and **Grove / Grove Night**. Experience changes preserve the current
route and open card. Home changes retain an unsaved capture or composed Companion reply.

Open a card, then **Open thinking deck**. Save a question layer before choosing **Your private answer**.
That answer is separate from the shared question. Mark uncertain context **Unknown** or **Needs review**,
then use **Quiet insights → Analyze now** for that board. **Memory** supports correction, original history,
archive/restore and a private JSON download. **Experiences** offers the manual comparison protocol:
choose a scenario and observed outcome; the ease rating starts unrated. Export observations before
reloading or signing out, then explicitly import the saved version-2/3 file to resume or combine
observations across builds. Identical observations are skipped; conflicting IDs reject the batch.

For a **steps** layer, save thinking and choose **Create card from step…**. Choose a title and
column, then explicitly **Create linked card**. WIP, board permissions and archive state apply.
Repeated requests reuse the saved link. **Refresh card status** reads the card's current title,
column and blocker state; a checklist tick is never used to infer completion of a linked card.
Removing a step/layer keeps its cards. A deleted card shows as unavailable and is not recreated by
retrying the old promotion. Board JSON import remaps links to the newly imported cards; links to
already-deleted cards become unlinked thinking items in the export, leaving source tombstones intact.
Linked material uses thinking schema version 2, so older importers reject it instead of silently
discarding relationships. Ordinary decks retain version 1; the current importer accepts both.
Step promotion itself adds no database migration or automatic dependency edge.

Choose **Explore dependencies** in a card's thinking space to see its prerequisites and the cards
that depend on it. **Add prerequisite** and **Remove link** save explicit relationships immediately.
The server rejects cycles, duplicate/self links and references outside the board. Read-only and archived
boards retain read access; editing requires write permission on an active board. **Refresh dependencies**
reloads current card status and board relationships. A failed save clears stale metadata and requires a
reload, including when a response may have been lost. Neither action changes task status, assignments,
deadlines or the original thinking step. Use the normal review/approve/apply flow for automation.

The board graph is limited to 500 edges and uses a separate revision. Saving stages the actor audit and
archive guard in one transaction, then revalidates card existence before commit. Deleted-card references
are hidden on reads and exports and pruned by the next save; board deletion cascades the stored graph.
Portable exports with dependencies use the `taskdeck-board` version-2 envelope, which older readers
reject. Import remaps both ends within the payload and rolls back the board if any relationship is
invalid. Existing files and exports without dependencies retain the prior shape.

Additive database migrations create thinking/insight/memory tables, question-source columns and personal-plan preference columns.
Follow [UPGRADING.md](../../UPGRADING.md) when updating an existing instance. The integration's automated
browser proof uses an isolated synthetic database; it does not migrate a maintainer's working database.

## Comparison protocol

Try the same real task in each experience: capture a rough note, inspect its proposal, approve and
apply deliberately, continue the resulting card, record a thinking layer, inspect an insight, and
answer a question. Compare ability to resume, number of confusing decisions, discoverability,
readability and unwanted interruptions. An experience is not better merely because it produces
more clicks or longer sessions. Start with personal crossover trials; statistical A/B claims need
enough independent participants and a defined outcome before experimentation.

## Boundaries and follow-through

Do not copy the prototype's hardcoded TD-15 support rule, September walkthrough text, simulated
model answers or browser-global data model. The original HTML has no authentication, backend,
live model, real collaboration or distributed idempotency. Its optional nudge timings are design
hypotheses. Archive is not erasure. A human statement is not independently verified fact.

Existing human decisions remain in [OUTSTANDING_TASKS.md](../../OUTSTANDING_TASKS.md): publisher
and signing, private-instance execution, release/runner settings, and the remaining subjective
night-palette and dogfooding choices. This overhaul does not infer any of those complete.

## Personal plan and continuity

Open **Personal plan** from navigation or Home. Choose any readable active project and card, select
**Plan for**, then **Add to plan**. The plan persists privately across reloads and experiences.
It holds at most 40 cards. **List** preserves your chosen order, **Board** groups by current project
and column, and **Horizon** groups by your chosen plan date. These are representations of your plan;
the actual board and calendar remain available for broader work.

**Focus** records the card and server time, then opens its Thinking Deck with surrounding navigation hidden. A saved thread layer holds a note
for next time; the normal draft/leave guard remains in place. Home and the plan offer
**Resume focus**. This is the last card you explicitly focused, not a guess based on views, due dates,
board activity or a collaborator's edits. **Plan tomorrow** changes only its personal plan date;
**Make room** removes it from the plan without deleting, completing or rescheduling the card.
Removing a plan entry retains its last-focus receipt. Unavailable cards show no title or board metadata
and remain removable. Refresh rechecks permissions and live card state; no background attention or
automatic board mutation is added.

Concurrent updates reject stale revisions. After an unconfirmed save, refresh before retrying so a
lost response cannot silently overwrite another session. Account export includes plan references and
last focus in both formats; shared board exports exclude them. Account deletion erases the preference
row. An additive migration introduces the plan JSON and revision columns with empty defaults.

## Native originals for private memory

New private answers and memories preserve original evidence and exact answer text as Context Fabric
source assets. Corrections supersede an answer asset without rewriting it or the original evidence.
Each memory revision names its answer asset. Title/status/archive-only changes reuse the same text
asset. Existing memory history is admitted on the next explicit write from its saved text; admission
timestamps describe source creation, while memory history retains the original revision dates.

The authenticated `GET /api/workspace-memory/{id}/sources` route requires both memory ownership and
current active-board access. Source IDs are server assigned. No queue job or automatic model retrieval
is created. Question revision and memory concurrency failures roll back source writes as well.

Preserved originals are available on request beside memory history in every experience. The board's
private memory download includes native assets and supersession links when present (format version 2);
it is archival JSON, not an import/restore format. Both account exports include owner-scoped native
captures, including originals whose board was deleted. Board deletion removes memory, not the retained
originals. Account deletion erases native captures and their assets. Archiving only excludes active
memory context; the viewer states this retention rule explicitly. Shared board exports omit originals.

Further source and representation work remains in
[the continuation tracker](https://github.com/Chris0Jeky/Taskdeck/issues/2808).

## Private audio answers

In a saved Thinking Deck question, open **Your private answer**, then **Record or open a private
audio answer**. Recording begins only after **Record audio** and browser permission. It stops at
60 seconds; file selection also accepts WebM, Ogg, WAV, MP3 and MP4/M4A audio up to 2 MiB. The draft
offers playback/download and remains local until **Save original privately**. Keeping audio first
reduces capture friction without requiring immediate transcription or treating unheard audio as knowledge.

The saved original remains immutable and untranscribed. **Save written version** preserves your
own transcription or description as a separate transcript representation. Corrections append versions;
**Confirm written version as my answer** creates private memory and a human-confirmed representation.
Confirmation is not external verification. A separate existing private answer is corrected in Memory.
The question and original evidence stay unchanged. Saving a recording, writing a version or confirming
an answer never queues a model/transcription job or changes the card. Automatic transcription, provider
consent, streaming recognition and model usefulness remain separate continuation work.

One original is retained per person and saved question version. Retrying an uncertain upload uses the
same upload ID and checks the original bytes and metadata; it cannot silently replace that recording.
The UI preserves draft files/text after failures and holds navigation while recording or saving.
The server rejects stale recording revisions and confirmation against changed question text. Up to
50 written/confirmation representations are retained for one recording. Confirmed answer corrections
belong to Memory, with its existing immutable source history.

Question-flow playback/download requires recording ownership and current access to an active board.
Collaborators cannot read another person's recordings, even with board ownership. Missing, foreign and
revoked private sources return 404; anonymous requests return 401. Deleted-board recordings remain in
the owner's account export. Shared board exports exclude private audio and written answers.

### Retained original library

Memory includes an explicit **Browse original recordings** action, independent of the selected board.
The owner can page through retained recordings, inspect the exact original question and written-version
history, and replay/download the original bytes. Pages read at most 21 owner rows to return up to 20
entries and a continuation; full written history loads only for the selected recording. Existing-board
access is rechecked for listing, details and download. Revoked recordings are hidden, including their
question excerpts; an empty filtered page can still offer the next page. Account changes/logout clear
loaded private content and playback URLs; refreshing a token for the same person preserves the request.

This library explicitly extends archive access: owner-linked originals on archived boards remain
readable with current board permission, and originals whose board was deleted remain readable under
their owner identity. Deleted-board retention already existed in account export. Library reads do not
restore a board, reactivate a question or authorize a write. Changed/removed questions and archived or
deleted boards show a read-only retained copy; only an unchanged current question links back to Thinking
for writing or confirmation. Account deletion remains the erasure boundary for these retained originals.
An unsaved local file is held only while the current page stays open: **Reload saved recording** keeps
that draft, but a browser refresh or closing the page loses an unuploaded file.

Both account export formats add `data.sourceStorage`: objects, references, ordered base64 chunks,
representation headers/supersession links and audio-answer links. Transcript text remains in
`data.transcripts`; source assets carry their `blobReferenceId`. To reconstruct an original, concatenate
decoded chunks by object ID and ordinal, then verify byte size and SHA-256. Buffered export preflights
source size and enforces its budget while collecting rows; streaming export emits bounded chunks.
This is archival export, not an import/restore tool. Account erasure removes the owner’s native
captures, representation links/payloads and blob references/bytes in the account transaction.

Storage uses 64 KiB SQLite chunk rows, with per-owner content deduplication. Declared-size, owner,
modality and reference quotas are checked before input is read. The `SourceStorage` configuration
defaults are `MaximumUploadBytes=67108864`, `OwnerQuotaBytes=268435456`, `ModalityQuotaBytes=134217728`,
`MaximumReferencesPerOwner=10000`; the question-audio endpoint retains its stricter 2 MiB limit.
An unknown duplicate can be refused at quota before its hash is known; an accepted upload's retry
does not reacquire quota. Existing artefact storage is unchanged. New manual representation headers
cover this audio path; legacy representation backfill and automated processing remain unclaimed.

### Source interaction recovery

Original-source pickers name the memory they belong to and distinguish revoked access from a transient
read failure. A permission failure removes displayed original choices and their selection. Source
selection and older-memory preservation survive a same-person token refresh; logout, account changes,
board changes and source revisions still clear stale private state. Retained-instruction continuation
uses the same disabled state as the composer while shared thinking is unsaved or a receipt is loading.
Original text history continues beyond offset 1000 in bounded pages of ten; every emitted continuation
is accepted, and a large empty offset cannot overflow the next-page calculation.
