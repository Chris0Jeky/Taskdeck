# Switchable workspace overhaul

Last Updated: 2026-09-08

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
| Detail | Zen, Studio, Control | Amount of visible structure and spacing |
| Theme | Existing Paper/Legacy choices plus Grove and Grove Night | Prototype-inspired colors over the shared token system |

The overloaded name Studio is retained from the source, but experience and detail have separate
labels. Existing Guided/Workbench/Agent workspace modes remain separate. None of these controls
grants permission, changes processing policy, approves proposals, or applies changes.

One mounted route outlet, existing board/card IDs, shared stores and the current realtime connection
carry work across presentation changes. Capture and chat reuse existing services. Review remains an
explicit approve action followed by a separate apply action and the existing confirmation dialog.

## Feature ledger

| Capability | Implementation target | Evidence required |
| --- | --- | --- |
| Switchable experiences and prototype themes | Persistent preferences, shared shell and real workspace entry points | Switch with an open card and unsaved text; reload preferences; keyboard/mobile layouts |
| Thinking Deck | Ordered, typed card layers with revision checks; stack/path views; preserve alternatives | Save/reload, stale-write 409, permissions, presentation continuity, linked child integrity |
| Quiet insights | User-requested structural checks on authorized current work; one durable queue | Deduplication, cause resolution, dismissal/snooze persistence, source links, honest empty/error states |
| Questions and memory | Explicit answers, statements/assumptions/unknowns, corrections and archive/restore | Original history retained, private scope, no task mutation on answer, archived retrieval exclusion |
| Companion | Existing accountable chat and capture/review services with contextual navigation | Real sessions and proposals; no demo claims about execution |
| Comparison | User-initiated local experience trials with optional notes | No telemetry or automatic assignment; recorded versions and exportable observations |
| In-place proposal overlays | Later integration of the existing authoritative diff into board presentation | Same effective revision as Review; partial selection represented as a real revision |
| Audio answers | Context Fabric original/representation pipeline | Physical microphone, storage, transcription failure/retry and consent proof |
| Model-generated observations | Bounded candidate producer after structural queue is proven useful | Grounding, authorized retrieval, stale-output rejection, usefulness corpus and budget |
| Optional nudges | Later opt-in attention policy | Non-intrusion evaluation, focus/input suppression and shared user budgets |

This ledger distinguishes the delivery target from verified completion. Exact implemented behavior,
test commands and remaining gaps are recorded with the integration PR; source screenshots are not
validation of the integrated application. The broader voice, model and attention work is not made
complete by introducing its UI vocabulary.

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
