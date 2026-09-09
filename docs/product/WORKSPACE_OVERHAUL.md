# Switchable workspace overhaul

Last Updated: 2026-09-09

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
| Memory portability and account privacy | Private JSON download plus both account export formats include originals, evidence, source references and history; account deletion erases private memories, revisions and insights | Implemented for active and archived records. The board-scoped download uses two authenticated reads, not an atomic backup or import format. Shared board exports never include private answers; collaborators' records survive account deletion |
| Companion | Existing accountable chat embedded in Companion Home and contextual links to existing chat/review | Implemented entry point; no new model provider or memory retrieval integration. Unified's board-side contextual companion remains next |
| Comparison | User-initiated session-only scenarios, explicit completion outcomes, optional ease ratings and notes with JSON download | Implemented; version-2 export records selected experience/detail/theme and backend-reported product version (null if unavailable). No telemetry, random assignment or statistical A/B claim |
| In-place proposal overlays | Existing authoritative diff rendered at affected board objects | Next; preserve effective proposal revision, subset-selection semantics and explicit apply gate |
| Linked steps | Explicit title/destination, atomic child-card/link/audit creation, repeat-safe requests, refreshed real card status and portable links | Implemented; shares the guarded card writer and board concurrency token. Removing thinking never deletes linked cards. Separate dependency edges are still next |
| Connections and continuity | Dependency links, chosen daily plan and last-worked continuation | Next. Current agenda is derived from existing Today data, not last-worked tracking |
| Audio answers and unified source evidence | Context Fabric original/representation pipeline and typed source anchors | Later; new memory currently preserves evidence directly, not as Capture/SourceAsset records. No microphone/transcription integration added here |
| Model-generated observations and recall | Bounded candidate producer and authorized knowledge retrieval | Later; requires grounding, fresh evidence, usefulness corpus, privacy and budget proof |
| Optional nudges | Opt-in attention policy after usefulness is established | Later; requires non-intrusion evaluation, focus/input suppression and shared user budgets |

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
archive/restore and a private JSON download. **Experiences** offers the manual comparison protocol: choose a scenario and observed outcome; the ease rating starts unrated. Export observations before reloading or signing out.

For a **steps** layer, save thinking and choose **Create card from step…**. Choose a title and
column, then explicitly **Create linked card**. WIP, board permissions and archive state apply.
Repeated requests reuse the saved link. **Refresh card status** reads the card's current title,
column and blocker state; a checklist tick is never used to infer completion of a linked card.
Removing a step/layer keeps its cards. A deleted card shows as unavailable and is not recreated by
retrying the old promotion. Board JSON import remaps links to the newly imported cards; links to
already-deleted cards become unlinked thinking items in the export, leaving source tombstones intact.
This adds no database migration and no dependency edge or automatic proposal execution.

The three additive database migrations create thinking/insight/memory tables and question-source columns.
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
