# ADR-0045: LLM Transcript Triage — Dedicated Worker Lane, Strategy-with-Fallback, Honest Provenance

**Status:** Accepted

**Date:** 2026-07-11

**Deciders:** Repository maintainers

## Context

REVIVAL-08 (#1304, Phase 2 of the revival plan — ADR-0044's one authorized new-backend-surface
exception) makes an LLM actually run inside the capture→proposal loop for the first time: a pasted
meeting transcript should produce N typed, evidence-backed proposal operations instead of collapsing
into one junk card through the deterministic extractor's whole-text fallback.

Constraints discovered in the shipped seam:

- Every capture shares one `RequestType` (`inbox.capture.v1`); capture-vs-automation dispatch is a
  `RequestType LIKE 'inbox.capture.%'` predicate baked into the queue repository's raw SQL.
- `LlmQueueToProposalWorker` awaits its **entire batch per tick** (`Task.WhenAll`), so one slow item
  delays every other queue item behind it. Today every item completes in milliseconds; an LLM triage
  call runs seconds-to-minutes (and REVIVAL-08 M2's chunked map-reduce will run longer).
- Capture items live in `Processing` while queued (the triage endpoint marks them Processing; the
  worker re-claims them under an `UpdatedAt` optimistic guard). Their crash recovery is that
  re-claim itself — there is no lease sweep for capture items.
- Provenance honesty is a ratified ship-gate invariant (#1273, gate item (c)): the recorded
  provider/model must name the engine that actually produced the output.
- Providers never throw: failures come back as `IsDegraded` results whose `Content` is canned
  classifier text — unusable output that must not become board proposals.

## Decision

1. **Transcript captures get their own `RequestType`, nested under the capture prefix:**
   `inbox.capture.transcript.v1` (`inbox.capture.transcript.` prefix family). Nesting keeps every
   user-facing capture query (GDPR export/delete, inbox summary/listing, executor guards) matching
   transcripts with zero changes; the worker-lane predicates treat capture and transcript as
   disjoint kinds. The type is resolved server-side from the payload's `CaptureSource`
   (`ResolveRequestTypeForSource`) at both enqueue choke points — clients never choose it.

2. **A dedicated `TranscriptTriageWorker` drains the transcript lane.** Because the shared worker
   serializes its tick on the slowest item, slow LLM triage gets its own `BackgroundService` with
   its own poll loop, fetch/claim primitives (`GetOldestProcessingTranscriptAsync`,
   `TryClaimProcessingTranscriptAsync` — mutually exclusive with the capture claim by in-query
   predicate), backlog gauge (`transcript-triage`), and the same self-heal recovery the capture lane
   uses (abandoned Processing rows are simply re-fetched and re-claimed; the payload-provenance
   short-circuit and the existing-proposal guard keep replays idempotent). Pending semantics are
   deliberately NOT lane-split: a Pending transcript is ordinary inbox depth
   (`CountPendingCaptureAsync` stays inclusive).

3. **The LLM leg is a strategy inside the existing `ICaptureTriageService` seam, with the
   deterministic extractor as the degraded fallback.** `CaptureTriageService` consults a new
   `ILlmCaptureTriageExtractor` only for transcript sources; the extractor runs the guardrail chain
   the chat surface established — kill switch (`LlmSurface.CaptureTriage`, previously declared but
   unused), passive provider health (mock/unavailable ⇒ skip), per-user quota, `CompleteAsync` with
   a purpose-built JSON prompt, degraded-result rejection, lenient parse (brace matching, fence
   tolerance), sanitization to the v1 caps, and final contract validation. **Any** failure of that
   chain degrades to the deterministic extractor in-process; LLM unavailability never fails the
   capture item.

4. **One deliberate non-fallback: the empty verdict.** When the LLM successfully returns
   `{"tasks":[]}`, that is an extraction result, not a failure — degrading to the deterministic
   extractor would fabricate a card out of unactionable text (the exact behavior this epic
   removes), and marking the capture Failed would surface a correct extraction as an error and
   invite a retry loop. The capture completes as the existing **Triaged** terminal state
   (Completed without a linked proposal), with provenance naming the LLM that produced the
   verdict. *(Revised during review: the original draft failed the capture; the adversarial
   review showed Failed inflates the needs-triage gauge and loops on re-triage.)*

5. **Provenance names the engine that ran, including "unknown".** LLM success records the real
   provider/model and prompt version `llm-triage.v1` (a new versioned constant beside `triage.v1`;
   the output envelope is built server-side — the model is never trusted with contract constants,
   and evidence must be a verbatim transcript quote so REVIVAL-09 can recover spans by substring).
   Deterministic runs keep `deterministic-extractor`/`capture-triage-v1`. When an existing proposal
   is reused and its author is unknowable (crash between proposal commit and payload stamp, either
   engine possible), the recorded value is `unknown` — never a guessed engine.

## Alternatives considered

- **Strategy only, no new RequestType/worker** — smallest diff, but every slow LLM call would sit in
  the shared worker's serialized tick, delaying all capture and automation items; M2 chunking makes
  that untenable. Rejected.
- **A third lane inside `LlmQueueToProposalWorker`** — same tick-serialization problem; the fair
  batch still awaits the slowest item. Rejected.
- **A non-capture-prefixed RequestType (e.g. `transcript.triage.v1`)** — would silently fall through
  to the NL-instruction planner lane (the trap called out in #1304) and drop out of every
  capture-scoped user query (GDPR, inbox). Rejected.
- **Lease/staleness columns for claim safety** — unnecessary: ticks are serialized within a process,
  and the cross-process double-claim caveat already exists (documented single-worker-process
  assumption in the recovery sweep). Schema stays untouched (no migration in M1).
- **Trusting the model with the full versioned envelope (`version`/`promptVersion`)** — rejected;
  the envelope is constructed server-side and only `{"tasks":[...]}` is requested, shrinking the
  failure surface and keeping version constants honest.

6. **Review-hardened boundaries.** Client payloads may not carry any server-authored triage
   provenance (`proposalId`/`triageRunId`/`provider`/`model`/`promptVersion` are forbidden on the
   untrusted parse path — a client-supplied `proposalId` would let a capture skip triage entirely
   and persist fabricated provenance). The reuse short-circuit runs before any validation that can
   fail on replay, so a crash between proposal commit and payload stamp can never orphan the
   proposal. The transcript lane processes items **sequentially**: the per-user quota is
   check-then-record with no atomic reservation, and concurrent extractions in one tick would
   overshoot it. The transcript worker is health-monitored with its own staleness budget
   (~`pollInterval*3 + MaxBatchSize×180s`) because a legitimate tick blocks for minutes of
   sequential LLM calls — the fast worker's threshold would false-alarm.

7. **Durable evidence uses server-resolved Transcript coordinates without upgrading provider v2.**
   After a successful schema-v2 LLM extraction, the server resolves each verbatim quote ordinally
   inside its normalized chunk and converts it to an absolute half-open `[start,end)` range in .NET
   UTF-16 code units over the linked LF-normalized Transcript. Every observation of a deduplicated
   same-title task must resolve to the same absolute range; an overlapping repeat or conflicting
   range retains a source reference with null offsets rather than a guessed span. A quote absent
   from its provider chunk invalidates that LLM leg, so the deterministic fallback creates no
   Transcript evidence link. Each successful LLM operation attaches its reference through the
   existing generic `ProvenanceEvidenceLink` API shape (`SourceType = "Transcript"`, `SourceId` =
   Transcript Guid in `D` format, fixed non-content label) plus a typed `TranscriptId` database FK.
   A database check makes that typed FK mandatory only for the canonical Transcript source type,
   and `ON DELETE CASCADE` makes both erasure orderings safe: an existing link is deleted with its
   Transcript, while a stale post-erasure proposal/link save fails atomically. The link does not
   copy source text into its label or inferred provenance field. Board-authorized provenance reads
   expose only opaque identifiers and optional offsets, and any future quote resolver must remain
   owner-scoped. The provider prompt/parser stays v2 and is not treated as an authoritative offset
   source.

## Consequences

- A transcript capture triages through a real LLM when (and only when) a live provider is
  configured; everyone else keeps exactly the pre-REVIVAL-08 behavior (mock/dev installs skip the
  LLM leg before any call is made).
- Pre-existing transcript captures enqueued as `inbox.capture.v1` before this change keep flowing
  through the fast capture lane; if the LLM leg engages there it can slow that lane's tick — a
  bounded, transient upgrade artifact accepted and documented here.
- The retry classification is unchanged (null/Unexpected/Conflict transient); quota- or
  kill-switch-blocked runs are not failures at the item level — they are silent fallbacks with an
  explicit log line and honest provenance.
- REVIVAL-10 can swap in an OpenAI-compatible provider without touching triage (the extractor only
  sees `ILlmProvider`); REVIVAL-09 gets verbatim-quote evidence to attach spans to; REVIVAL-11 keeps
  receiving the existing risk classification on every LLM-triaged proposal (M1 adds no auto-apply
  and fabricates no confidence values).
- New configuration section `CaptureTriageLlm` (`Enabled`, `MaxOutputTokens`, `Temperature`),
  documented in `docs/platform/CONFIGURATION_REFERENCE.md`.

## References

- #1304 (REVIVAL-08 epic brief; M1 scope) · ADR-0044 (revival pivot; new-surface authorization)
- `docs/REVIVAL_PLAN.md` §4 Phase 2, §7 · `docs/analysis/2026-07-10_revival_assessment.md` §2.2
- #1273 / gate item (c) — honest provenance · #1209 — idempotency guards this design reuses
