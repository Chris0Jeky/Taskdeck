# Bounded acceptance and dogfood pilot

Status: proposed manual pilot, 2026-09-23. Execution: **NOT RUN**. This pack supplies task
cards and a recording rubric, not product changes, a runner, telemetry, or user-validation results.

## Existing owners and new scope

[The existing dogfood protocol](https://github.com/Chris0Jeky/Taskdeck/blob/622d9d820f48e68ac7fa77d94c9ca2536155ddf8/docs/dogfooding/README.md)
and [GH-1271](https://github.com/Chris0Jeky/Taskdeck/issues/1271) own real-use evidence and the
revival checkpoint. Keep the separate real-use database, existing snapshot instrument and log.
The ten-day floor is not automatic checkpoint eligibility; the existing ADR-0044/q-8 conditions
still apply. This pilot neither resets that clock nor changes any release or archive decision.

[GH-1947](https://github.com/Chris0Jeky/Taskdeck/issues/1947) records a prior dogfood sweep whose
build was not pinned. It motivates explicit build attribution; it is not proof that the same
bugs remain. [GH-2901](https://github.com/Chris0Jeky/Taskdeck/issues/2901) owns the wider experience
comparison. This pack does not replace that matrix or claim those sessions occurred.

The inspected [product guide](https://github.com/Chris0Jeky/Taskdeck/blob/622d9d820f48e68ac7fa77d94c9ca2536155ddf8/docs/product/DOGFOODING_GUIDE.md)
uses capture, triage, proposal review and board execution to reduce maintenance overhead while
preserving review-first trust. The new recommendation here is a bounded comparison of that loop.
No particular improvement or audience demand has yet been demonstrated.

Cross-repo architecture: [agent-harness GH-290](https://github.com/Chris0Jeky/agent-harness/pull/290)
(review/oracle boundaries and source ledger), [GH-291](https://github.com/Chris0Jeky/agent-harness/pull/291)
(advisory entropy), and [claude-config GH-326](https://github.com/Chris0Jeky/claude-config/pull/326)
(canonical workflow packaging). The completed Deep Research brief was unavailable for inspection;
its B0 citation remains unresolved in the harness ledger. This is a new proposal reconciled
against existing repository facts, not an attributed finding from that unseen brief.

## Materials and claim boundaries

- [cases.json](cases.json): four authored manual task cards, with preconditions, observations,
  reset and controls. It is not a Taskdeck import payload or the GH-281 UX-scenario schema.
- [session-template.md](session-template.md): blank per-session comparison and disposition.
- Existing product browser/testing tools and the dogfood log retain their roles. No automated
  judge, screenshot grader, second harness or recurring new acceptance gate is introduced.

A test can establish a named behavior without establishing usefulness. A synthetic task can
expose friction without representing a user population. Voluntary return to the real product
is a third observation, not something generated task traffic may stand in for.

## Prepare the comparison before observing results

Choose one existing slice and one primary question, such as whether returning to a captured task
requires less searching without weakening proposal review. Record baseline/candidate SHA, build,
configuration, browser, provider mode and task-card digest. Predeclare the expected improvement,
acceptable tradeoffs and what would make the result inconclusive. Do not choose success criteria
from the observed times or edit expected behavior to fit the candidate.

Use separate disposable synthetic databases for baseline and candidate, isolated from development
and the real dogfood database. Confirm the actual database path and initial state, not just an
environment variable. Follow the existing setup/backup protocol. Prepare an equivalent documented
state independently for each revision; a candidate migration must never rewrite the only baseline
copy. Do not assert binary database compatibility across versions without evidence.

Use only synthetic text: board `Acceptance pilot`, column `Backlog`, capture
`Prepare the release checklist for the sample project`, and proposed card `Draft sample checklist`.
Record actual generated IDs locally. Build any proposal through an existing supported path before
timing the task. Inspect its exact intended operations and record the preparation mode. Do not
bypass review through direct database writes or silently retry a generator until it yields a
preferred result. Record failed preparations; an unavailable suitable proposal makes that case
BLOCKED. A prepared/mock proposal qualifies the review path, not live-model generation quality.

Make an explicit reset for each case: restore only its disposable synthetic instance to its
recorded initial state using a supported safe procedure, or recreate that state through supported
product actions. Never overwrite the real dogfood database. Stop instead of running on a database
whose identity or backup status cannot be established.

## Manual execution

Use the task cards in the actual product. Record the existing route and controls encountered;
this document does not promise stable selectors, button labels or click counts. Observe the UI
and the durable effect after reload, plus relevant existing request/test evidence where available.
A screenshot of a completed-looking screen is not by itself proof of persistence.

Record a practice attempt separately. Use the same initial task state for a comparable pair;
alternate baseline/candidate order on a second pair where practical. Keep failed and blocked
attempts in the record. Repeated trials by the same builder are not independent participants.
Record assistance from agents, documentation or prior familiarity. Count preparation and setup
separately from task time so a fast task cannot hide expensive preparation.

The successful control is a permitted operation that reaches its expected durable state. The
negative control is a rejected/cancelled proposal that leaves board state unchanged. Proposal
metadata may change on rejection; the contract is not that the whole database is byte-identical.
Record the before/after target state, not just counts: a count can stay constant while the wrong
card is edited. Setup failure is BLOCKED, not a successful negative control or a product PASS.

## Before/after rubric

| Dimension | Record for baseline and candidate | Interpretation |
|---|---|---|
| Correctness and trust | Intended durable effect; unintended changes; approval/execution boundary | A trust or persistence defect cannot be traded for a faster time |
| Completion | Unassisted, assisted, failed, blocked or not run, with the observed outcome | Separate failure from unavailable setup and from simple missing evidence |
| Effort | Task seconds, preparation seconds, corrections, navigation detours, assistance | Report raw paired observations, not an unsupported population effect |
| Resumption | Time to locate the item and explain its next step; actual interruption interval | A short break does not prove next-day recall or long-term retention |
| Comprehension | Builder explains what was proposed, approved, executed and saved | Human explanation is advisory; an LLM paraphrase is not the participant's evidence |
| Value | What incumbent action was displaced, or why the builder would use something else | Synthetic preference is not organic demand; real near-misses belong in the existing log |

Complete one [session record](session-template.md) per actual attempt. Valid statuses are PASS,
FAIL, BLOCKED, NOT RUN and N/A with a reason. Missing times remain blank, not zero. If a revision
lacks the proposed capability by design, mark the case not applicable to that baseline and narrow
the comparison; do not manufacture a before/after percentage for an impossible baseline task.

## Continue, revise once, or stop

Proposed first budget: one slice, four task cards and a short session capped at 30 minutes; up to
two comparable baseline/candidate pairs. These are operating suggestions, not research-derived
constants. Record a different budget before running if the selected task requires it.

Keep the candidate for further real-use observation only when the predeclared improvement is
observed without a trust/correctness regression and the evidence is attributable. Revise one
bounded friction point when the observations identify a plausible cause. Stop expanding or park
the experiment if setup cannot be made comparable, the same issue persists after that revision,
or the predeclared benefit does not appear within the budget. An inconclusive pilot is not an
archive ruling and never overrides existing merge/release criteria.

Confirmed defects and advisory findings enter the existing issue/review workflow, including
GH-1947 when relevant. Do not create one issue per metric or let a QA bot decide merge eligibility.
Only broaden to a second product after a real Taskdeck session shows the pack is usable; merely
merging these files does not prove the pilot shape.

## Local-first evidence and organic follow-through

Keep raw session data local by default. Record the chosen local retention/review date before the
session. Before sharing, manually preview a minimal aggregate: candidate identity, task IDs,
status, counts/times, assistance and a sanitized explanation. Paths, timestamps, screenshots,
request bodies and small counts can reveal information; the absence of card text is not a privacy
proof. Do not commit databases, private task text, transcripts or raw session logs.

Use the existing dogfood snapshot tool only against the explicit real-use database, inspect its
reported `Database:` identity, and preserve its documented `AppliedAt` interpretation. Never point
it at these synthetic instances and call their activity organic use. Keep missed weeks and genuine
near-misses in the existing log rather than creating flattering activity. No remote collector,
automatic export or telemetry permission is introduced here.

The final session handoff names the tested revisions, what was actually observed, controls,
unknowns, the bounded next action and relevant outstanding human actions. Human sign-off remains
human; this pack does not check off `OUTSTANDING_TASKS.md`.
