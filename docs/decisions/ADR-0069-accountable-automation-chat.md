# ADR-0069: Accountable Automation Chat

- **Status**: Accepted under the maintainer's recorded product ruling; implementation pending on #2004
- **Date**: 2026-09-07
- **Deciders**: Chris0Jeky, product ruling of 2026-09-04 reaffirmed 2026-09-06; the coordinator records the implementation contract below
- **Related**: #2004, CF-09 #2263, ADR-0003, ADR-0056, ADR-0065

## Context and authority

Automation Chat can answer an instruction with plausible prose while producing no proposal.
The v0.3 contract must make the outcome clear, bind the intended board, and ground a proposed
update in real board state. Generating a proposal still requires the separate human Review,
Approve and Apply steps established by [ADR-0003](ADR-0003-proposal-first-automation.md).

The [September 4 ruling](https://github.com/Chris0Jeky/Taskdeck/issues/2004#issuecomment-5533592852)
settles default-to-act, one clarification round, grounding and inline board binding. Its
[September 6 reaffirmation](https://github.com/Chris0Jeky/Taskdeck/issues/2004#issuecomment-5556351348)
requires one ADR first, then implementation and acceptance tests, all in v0.3. This ADR records
that decision; it does not claim the runtime change has shipped or grant release approval.

## Decision

### 1. Actionable turns attempt a proposal by default

Remove the user-facing `Request proposal generation` checkbox and its reset-after-send behavior.
The chat service owns the decision to attempt a proposal for actionable intent; a forgotten client
toggle must not silently turn an instruction into prose. Ordinary questions can still receive
ordinary answers. Reuse the existing planner and tool orchestration rather than creating a second
intent parser or board resolver for this change.

The old request flag is not an authorization boundary. Any API compatibility treatment must be
explicitly tested: default-to-act must work for the shipped client and an omitted flag, and no
flag may bypass board authorization, provider gates, quota, proposal validation or human review.

An attempted action ends with either a persisted proposal linked to Review, one bounded request
for missing context, or an explicit reason no proposal was created. The server-derived outcome
must remain visible independently of the model's wording. A proposed description is a draft,
never evidence that a board change was applied. A tool-produced proposal and planner fallback
must not create two proposals for the same attempt.

### 2. Board binding is explicit and local to the session

An unbound session may remain useful for conversation. When a turn needs a board, present an
inline board picker beside that turn and retain the user's instruction. Selecting a board binds
that existing owned session and offers an explicit continuation of the retained instruction;
it must not send the instruction a second time merely because a component rerendered.

If exactly one eligible board exists, it may be bound automatically with a visible receipt naming
the board. Eligibility is determined by existing server-side board access and writable-state
rules, not a client-provided count. Zero eligible boards produces an actionable explanation;
multiple boards require a selection. A `board.create` request receives intent-aware guidance
instead of the misleading claim that selecting an existing board would create a new one.

Binding changes only the session's board context, never ownership or existing proposal scope.
The server validates session ownership and board access again at binding and at proposal creation.
Do not relabel historical messages or proposals as if they were produced for the newly bound board.
A late response for another selected session must not bind or send through the current one.

Natural-language resolution of board names belongs to CF-09 #2263 in v0.5. This work does not
introduce a competing resolver or infer a board solely from a name found in message text.

The current API has no operation for binding an existing session. This ADR authorizes a narrow
`POST /api/llm/chat/sessions/{id}/board` operation carrying a board ID, with a corresponding
application-service method and domain transition on the existing `ChatSession.BoardId` field.
It binds an unbound, caller-owned session; repeating the same binding is idempotent, while trying
to replace a different existing binding returns a conflict and offers a new session instead.
Concurrent different binds must not both succeed. Return the updated session only after the
authorized binding is persisted. Use the existing stable 400/401/403/404/409 conventions and
conceal another user's session through the existing ownership policy. This is no general-purpose
session update API and requires no new table or alternate identity mechanism.

### 3. Ground existing-card updates before generation

Before generating operations for an existing card, read the bound board through the existing
authorized read path and use the actual card identity and state. The proposal's before-state and
preview are based on that state, not a title or description invented from the conversation.
Missing, inaccessible or ambiguous targets lead to the bounded clarification or an explicit
refusal; best effort does not authorize inventing identifiers or guessing between matching cards.

Keep existing read limits and provider/tool budgets. If a bounded read cannot establish the
target, disclose the limitation rather than claiming the card was inspected. A proposal retains
the existing revision, validation and stale-state protections through Review and Apply. Nothing
in chat can silently execute it or expand a user's rights.

### 4. Clarification terminates after one round

For one actionable request, allow one assistant clarification round. A following plain answer,
`just do it` or `do your best` attempts best effort using the original intent plus the answer.
It then returns a proposal or an explicit reason it could not create one. Rephrasing the same
clarification forever or returning prose that reads as completed work is not a valid outcome.

Persist enough request/clarification context that reloads and the next HTTP request cannot reset
the round budget. Best effort does not waive missing board context, target identity, authorization,
provider readiness, quotas or validation. The board picker is a context-recovery action: an
unbound-board refusal must remain recoverable without consuming an endless chat clarification loop.

### 5. Failure truth applies to both transports

The ordinary message endpoint persists the action outcome with the assistant message so reloads
retain the reason and Review link. A degraded response to a requested action explicitly says when
no proposal was created, alongside its existing degraded reason. Known failures retain the stable
error contract; unexpected internal details are not exposed as user-facing explanations.

The existing stream endpoint carries the same no-proposal notice as a persisted suffix, as ruled
on #2004. The shipped web client need not adopt streaming in this issue. Never claim a streamed
draft has been applied or a proposal exists before persistence succeeds, and never overwrite a
real proposal receipt with a blanket no-proposal sentence after a later failure.

## Acceptance and sequence

Land this ADR before the runtime implementation. Keep #2004 open until all implementation
criteria are proven; an ADR merge fulfills only its decision-record criterion.

| Scenario | Required observable result |
| --- | --- |
| Actionable message with no bound board | Inline picker or one eligible board bound with a visible receipt; no silent loss of the instruction |
| Zero, multiple, archived or inaccessible boards | Truthful recovery/refusal; server access checks remain authoritative |
| Natural wording requesting an existing-card update after binding | Proposal linked into Review, real card ID and before-state, no board mutation before approval and execution |
| Clarification followed by a plain answer or skip phrase | One best-effort attempt using the original intent, then proposal or explicit refusal |
| Session reload or selection changes during a request | Clarification budget persists; stale response cannot operate on another session |
| Provider degradation or planner failure | Persisted reason and accurate no-proposal outcome, with ordinary and stream-path coverage |
| Tool orchestration already produced a proposal | Its receipt is reused; fallback produces no duplicate |
| Non-actionable question | Useful conversational answer without fabricated work or a spurious board write |

Use deterministic service and API tests for binding, grounding, clarification, failure truth and
cross-user refusal, plus composable/component tests for the removed toggle, picker and late-response
behavior. Include an end-to-end synthetic chat-to-Review journey that proves the board remains
unchanged before Apply. Follow the repository's required backend/frontend checks for each changed
seam; a live-provider smoke is separate evidence and is not implied by mock tests.

## Alternatives and consequences

Keeping an opt-in checkbox preserves the observed silent failure and is rejected. Deferring
binding or grounding to v0.5, or moving the whole redesign to v0.4, contradicts the recorded ruling.
Natural-language board resolution remains deferred to the shared resolver. Automatic execution
is outside this decision and remains forbidden by the current review-first contract.

Default proposal attempts can consume more provider work than an unchecked toggle. Existing
quota, readiness, bounded tool rounds and duplicate-prevention controls must therefore remain
effective. The benefit is a clear path from an instruction to accountable proposed work, with
the human's approval still determining whether any board change happens.
