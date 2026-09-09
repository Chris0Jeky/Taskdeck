# ADR-0070: Switchable workspace experiences and explicit working memory

- **Status**: Accepted under the maintainer's 2026-09-08 implementation delegation
- **Date**: 2026-09-08
- **Deciders**: Chris0Jeky delegated product/integration choices to the coordinator in the overhaul session
- **Related**: #2800, ADR-0038, ADR-0056, ADR-0065, ADR-0069

## Context

The maintainer supplied three stateful reference prototypes and their chats, asked to retain
different versions for comparison, and explicitly included thinking, insights, questions and memory
in the implementation scope. The [resource map and delivery ledger](../product/WORKSPACE_OVERHAUL.md)
record the source interpretation. The prototypes simulate intelligence and persistence; their
client-side models cannot become production permission or execution boundaries.

## Decision

Keep one application, shared board/card identities and one mounted route outlet. Add independent
local preferences for experience (Classic, Studio, Companion, Unified), presentation (Zen, Studio,
Control), and theme (including prototype-inspired Grove/Grove Night). Classic remains the default.
Existing workspace modes, processing configuration and authorization are unaffected. Preferences
may change the visible structure but never implicitly approve or apply work.

Thinking material is an optional card-attached aggregate with validated ordered layers and optimistic
revision checks. It follows board permissions and card lifetime. A stale write returns a conflict;
the client retains its draft. Steps are intermediate thinking until an explicit, tested linked-task
promotion is implemented. Alternative selection retains the other alternatives.

Quiet insights and working memory are dedicated relational records. Both are board-scoped and
user-private: another collaborator on the same board does not inherit a person's answers. The
current service requires ongoing read access to an active board. Statements, assumptions, unknowns
and material needing review remain distinguishable. Corrections retain original wording/history;
archive excludes material from the active projection and is reversible. It does not claim erasure.

Begin with an explicit Check insights action and structural observations about recorded blockers
and memory marked unknown/needs-review. Reads revalidate existing observations. Durable dismissal,
snooze and topic mute belong to the queue, independent of layout. An answer records knowledge and
does not assign, unblock or otherwise mutate a card. Source evidence is checked again at answer
time. No confidence score grants authority and no unsolicited nudge is enabled.

Contextual conversation reuses the existing chat, health and proposal paths. This decision does
not declare the separate accountable-chat work complete, connect new memory to model retrieval,
or turn structural observations into model-generated analysis. Audio intake remains owned by
Context Fabric. New memory records currently preserve text/evidence directly; capture-original
and representation integration remains a separately tracked extension.

## Consequences and verification

Switching experience must preserve unsaved work on the current route. Navigating away from unsaved
thinking or capture requires a visible choice; persistence failures preserve input. The same capture,
review, approve and explicit apply journey must pass in the integrated application. New records need
database migration, permission, revision, archival and portability coverage. Desktop and narrow
viewport checks complement unit tests; source screenshots do not prove the live app.

Personal crossover comparisons are user initiated, session-only and exportable. There is no automatic
cohort assignment or telemetry. The default experience can be revisited after observed use; this
delegation does not constitute subjective acceptance of one winning layout or of a release.
