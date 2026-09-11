# Ordinary Inbox triage status polling

Last Updated: 2026-09-11

Ordinary Accept / Start Triage watches every accepted capture independently in Paper and Legacy
Inbox. Selecting another item or accepting another capture does not cancel earlier work. This
reduces repeated manual refreshes while preserving the explicit Review, Approve and Apply flow.
No polling action creates, approves or applies a proposal.

`GET /api/capture/items/{id}/status` returns only `id`, `status`, `processedAt`, `errorMessage`,
`disposition` and `canEditSuggestion`. Identity comes from claims. As with the existing detail read,
a missing or non-capture queue item returns 404 and another owner's capture returns 403. The service
checks request type and ownership before parsing the payload. Applied-conversion status is resolved
without mutating the queue entity, persisting provenance backfill, or reading the durable capture
store. The stored queue payload is still read and parsed; this is a lightweight wire contract, not a
new database projection or a guarantee of constant storage cost.

The store schedules one current read at a time, rotating due captures fairly. Each capture backs
off from 2 to 4, 8, 16 and then 30 seconds. There is no total attempt or elapsed-time cutoff tied to
a provider's expected runtime. Each status or terminal-detail read has a ten-second deadline,
requests transport cancellation, and rejects late results even if the transport ignores cancellation.
An abandoned underlying transport may remain active until it honors cancellation or completes.

Status reads patch only status fields on existing rows and cached detail summaries. They never add,
remove or reorder list rows, and do not replace source text or provenance. A terminal status starts
a fresh full-detail read. Only a current terminal detail ends the watch and refreshes workload counts.
When a status read supersedes a full-detail read for an item nothing is cached for, the next status
tick re-issues that full body once under the current read authority and caches only the detail, so
supersession alone cannot leave a watched item with no full detail while its status stays nonterminal.
Status and full-detail requests share per-item read authority: an older response cannot replace a
newer observation in either response order. A failed or cancelled newer read does not make an older
request current again; the watch retains its next retry. Terminal hydration continues the authority
of its triggering status request.
An older cached terminal result cannot complete a new enqueue. Per-item watch identity, session epoch
and successful-write generation checks reject obsolete results, including a re-enqueue during hydration.

Network failures, deadline expiry and server errors retain the watch and show a delayed/retrying
notice. A 403 or 404 retires only that capture with an unavailable notice and an explicit status retry.
A 401 pauses all ordinary checks until session reset. Scope changes, entry to or exit from archived
history, unmount and logout cancel all ordinary watches. Selection changes keep them running.
The notices make no completion-time promise and distinguish accepted work from failed refreshes.

The existing batch triage reconciliation, its sixty-second bound and recovery receipts are unchanged.
The status endpoint and ordinary scheduler do not replace that workflow.

Focused service/API tests cover the narrow contract, ownership and read-only conversion resolution.
Store tests cover multiple watches, the former 450-attempt boundary, retries, authority failures,
late responses, re-enqueue races and confirmed terminal hydration. Both Inbox view tests exercise
waiting, retry, unavailable and paused notices. The browser journey is
`frontend/taskdeck-web/tests/e2e/inbox-triage-polling.spec.ts`; it uses real authentication with
synthetic capture transport and never calls a live model.
