# CF-22 — Authority-policy evaluation v1: create-card under the Assist preset (ADR-0057 first slice; separately gated on CF-24 evidence) (#2275)

Last Updated: 2026-09-02

> Curated from the v0.6 acceleration bundle (grounded `c27283fb2`, 2026-08-30) against `main` `79dd57cd3` on 2026-09-02 under tracker #2368. Planning input, not authority: the live issue, ADR-0065/ADR-0057 and `docs/architecture/CONTEXT_FABRIC.md` win. Corrections to the bundle are listed in the last section.

## Outcome

Define and run a **shadow-only** authority evaluator for exactly one narrow, reversible class —
create-card from an explicit `Act` capture into an explicitly named board with extractive evidence,
under ADR-0057's **Assist** preset — and produce the risk-based shadow-and-canary report the amended
ruling 6 requires. **Nothing here authorises execution.** Review-first (explicit approve, then explicit
execute with an `Idempotency-Key`) stays the only shipped authority policy until the maintainer records
an explicit go on `#2275` (ADR-0065 rule 8; ADR-0057 §5; STATUS 2026-08-24).

The pack was audited for this: no child contract, candidate, schema or fixture opens an execution path.
`AuthorityShadow.cs` exposes `ExecutionIsForbidden()`; `authority-shadow-receipt.schema.json` pins
`executionPerformed` to `"const": false`; `fixtures/authority-shadow-receipt.example.json` carries
`"executionPerformed": false`; the go-gate child is `humanOwned: true`, `status: needs-decision`; and
the three shipped write seams — `AutomationProposalService.cs`, `AutomationPolicyEngine.cs`,
`AutomationProposalsController.cs` — are in every child's `avoids` list. The separation
proposer → policy evaluator → executor is respected, with the executor deliberately absent.

## Live dependencies (verified 2026-09-02)

| Issue | State | What it must deliver first | Unblocks |
| --- | --- | --- | --- |
| CF-08 `#2262` candidates | open (umbrella) | `SemanticCandidate` with `derivation` extractive/inferred, so "extractive evidence complete" is computable | 02, 03 |
| CF-09 `#2263` resolver | open | the binding that makes "target board is explicit, not inferred" a recorded fact rather than a guess | 02, 03 |
| CF-21 `#2274` presentation | open | where a shadow result is shown (Control) without implying authority | 06 |
| CF-24B `#2277` runtime metrics | open, **same v0.6 milestone** | the fact projection the shadow/canary report is built from | 06 |
| CF-07 `#2261` anchors | open (implicit) | `EvidenceAnchor` completeness is half the eligibility predicate | 02 |
| Maintainer go on `#2275` | **not given** | the ADR-0057 "own gate" | 07 and any future execution |

## Child slices (one PR each, in order)

| id | Outcome | Depends on | Mode | Startable now? |
| --- | --- | --- | --- | --- |
| `V06-CF22-01-shadow-contract` | Freeze `AuthorityPolicyDefinition` / `AuthorityEvaluation` / `AuthorityDecision` / `AuthorityShadowOutcome` / `ExecutionReceipt` records, no execution | — | contract-only | **Yes.** Pure records plus a deterministic evaluator over supplied facts; the pack marks it `status: ready` and no predecessor is required. It should still land as records + unit tests only, wired to nothing |
| `V06-CF22-02-eligibility` | Narrow explicit-`Act` + explicit-board + extractive-evidence predicate | 01 | implementation | No — CF-08 `derivation`, CF-09 binding, CF-07 anchors |
| `V06-CF22-03-shadow-runner` | Evaluate beside human review, persist content-free outcomes | 02 | implementation | No |
| `V06-CF22-04-compensation-proof` | Prove create-card compensation / undo under every outcome | 03 | implementation | No |
| `V06-CF22-05-kill-switch` | Prove revocation and daily ceiling at the evaluation boundary | 03 | implementation | No |
| `V06-CF22-06-evidence-report` | Risk-based shadow/canary report | 05, CF-24B | implementation | No |
| `V06-CF22-07-go-gate` | Record the maintainer's explicit authorisation | 06 | **human-owned** | No — and no agent may mark it done by inference |

## Architecture

**Separation of duties (ADR-0057 §2).** Proposer → **AuthorityEvaluator** → Executor. For CF-22 the
executor does not exist. The evaluator records a decision; it never records a human approval. The
shipped proposal decision field is `AutomationProposal.DecidedByUserId` (there is no
`ApprovedByUserId`), and a shadow result must never be written to it.

**Eligible class — every condition must hold** (matches ADR-0065 ruling 6 as amended plus the live
issue's Assist framing):

| Condition | Where the fact comes from | Precision note |
| --- | --- | --- |
| Capture requested intent is explicit `Act` | `Domain/Enums/CaptureIntentMode.cs`, `Capture.RequestedIntent` | Must be **`RequestedIntent == Act`**. `Auto` is "an instruction to infer"; an `EffectiveIntent` of `Act` resolved from `Auto` is **not** explicit |
| Target board explicit, not inferred | CF-09 binding reason | The resolver records reason and certainty; only "explicit named target" qualifies |
| Caller currently has write access | `Board.OwnerId` **or** a `BoardAccess` row | Rechecked at the last safe boundary, not at proposal time |
| Exactly one supported create-card operation | `AutomationProposalOperation` | One operation, one target type |
| Evidence extractive and complete | CF-08 `derivation == extractive` + ≥1 CF-07 anchor | An inferred candidate is ineligible |
| Risk is Low | `AutomationPolicyEngine.ClassifyRisk` (read-only) | Reuse the shipped classifier; do not edit it |
| Policy active, unexpired, not revoked | new `AuthorityPolicyDefinition` | Versioned + digest |
| Daily ceiling available | new counter | Atomic and idempotent |
| Kill switch off | new authority kill switch | Shape it after `Services/ILlmKillSwitchService.cs` (`KillSwitchScope`, `IsKilledAsync`) rather than inventing one |
| Compensation supported | archive/undo of the created card | Simulated only |
| Preset is **Assist** | ADR-0057 §4 | The preset must be a recorded field, not an implied context |

**Any unknown is ineligible** — a distinct result from "would-deny".

**Content-free by construction.** The shadow record stores policy id/version/digest, proposal + revision
ids, principal, evaluated time, eligibility, stable reason codes, operation fingerprint, target board,
permission-snapshot result, evidence completeness, risk, would-reserve ceiling unit, compensation
availability, and the later human decision. Never a title, description or evidence quote.

**Canary (post-go only).** A stable hash over `saltVersion:policyVersion:subjectId` assigns
shadow / canary / holdout; allocation input and salt version are recorded; no dynamic cohort expansion.

## Implementation plan

**Preflight.** Read `#2275` in full including the *Reconciliation amendments*; read ADR-0057 (Status
line caveat, §2, §4, §5), ADR-0065 §Decision 9 and §Amendments ruling 6; confirm the maintainer go is
**absent**; confirm nothing in the branch touches an execution route.

| Path | State | Owner |
| --- | --- | --- |
| `backend/src/Taskdeck.Domain/Authority/` | to be created | producer |
| `backend/src/Taskdeck.Application/Authority/` | to be created | producer |
| `backend/tests/Taskdeck.{Domain,Application}.Tests/Authority/` | to be created | producer |
| `scripts/context-fabric/authority-report.*` | to be created (`scripts/context-fabric/` does not exist) | producer |
| `backend/src/Taskdeck.Application/Services/AutomationProposalService.cs` | exists — **do not edit** | integration owner |
| `backend/src/Taskdeck.Application/Services/AutomationPolicyEngine.cs` | exists — read `ClassifyRisk` only; it is a proposal risk/permission engine, not an authority-profile engine | integration owner |
| `backend/src/Taskdeck.Api/Controllers/AutomationProposalsController.cs` | exists — **do not edit**; it owns the explicit approve/execute + `Idempotency-Key` contract | integration owner |
| `docs/GOLDEN_PRINCIPLES.md` (GP-06 wording) | exists | **human-gated** — only in the same PR as a shipped execution slice, never during shadow |

**Rollout / rollback.** The evaluator ships disabled; enabling it starts shadow evaluation with zero
writes to work state. Rollback disables evaluation and retains shadow records for the report. No
migration is destructive; shadow tables join export, import and account deletion.

**Definition of done (shadow phase).** Zero domain writes proven; the report generated from CF-24B
facts with method and date; kill switch and daily ceiling proven; compensation simulated for every
outcome; every acceptance box traced to a test **except** the maintainer go, which stays open. GP-06 is
**not** amended in this phase — `#2275`'s third acceptance box belongs to the execution PR.

## Test plan

- [ ] **Zero-write proof:** a shadow evaluation over a seeded proposal performs no insert, update or delete on `Cards`, `Columns`, `Boards`, `AutomationProposal*` or any work table — asserted by a change-tracker/entry-count assertion around the call, not by absence of an exception (Application + Api, both).
- [ ] No shadow result is ever written to `AutomationProposal.DecidedByUserId` or any approval field (Application).
- [ ] Implicit or inferred board → ineligible (Domain).
- [ ] `RequestedIntent == Auto` resolving to an effective `Act` → ineligible (Domain).
- [ ] Inferred derivation or a missing anchor → ineligible (Domain).
- [ ] Permission revoked before evaluation → deny; board owner without a `BoardAccess` row → allowed by the predicate (Application).
- [ ] Unknown risk or unexpected operation shape → **ineligible**, not would-deny (Domain).
- [ ] Kill switch stops the next evaluation within one tick (Application).
- [ ] Daily ceiling is atomic and idempotent under two concurrent evaluations (Application/persistence).
- [ ] Policy expiry exactly between evaluation and the recorded would-execute time → ineligible (Domain).
- [ ] Compensation simulation succeeds for every supported outcome and fails loudly when the target is already archived (Application).
- [ ] Receipts validate against `authority-shadow-receipt.schema.json` with `executionPerformed: false` (Application).
- [ ] No API route exposes execution; the MCP surface still has no approve/apply tool (Api + `Architecture.Tests`).
- [ ] Owner isolation: shadow records are unreadable cross-user (Api).

Commands: `dotnet test backend/tests/Taskdeck.Domain.Tests/Taskdeck.Domain.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~Authority"`;
same filter on `Taskdeck.Application.Tests` and `Taskdeck.Api.Tests`;
`dotnet test backend/tests/Taskdeck.Architecture.Tests/Taskdeck.Architecture.Tests.csproj -c Release -m:1`.

## Edge cases

Proposal revised after shadow evaluation · board archived before the would-execute time · duplicate
idempotency key · policy expires mid-evaluation · receipt write fails after a simulated create ·
compensation target already archived or deleted · maintainer authorisation later revoked · metric cohort
contains unknown target outcomes · owner deleted mid-evaluation · clock skew at the ceiling window
boundary · a shadow record referencing a deleted proposal revision · export during an in-flight
evaluation.

## Reference material

| Kind | Archived path | Good for | Caveats |
| --- | --- | --- | --- |
| C# candidate | `docs/analysis/2026-08-30-acceleration-bundles-v0.5-v0.6/v0.6/candidates/csharp/AuthorityShadow.cs` | The ten-condition predicate and its stable `authority.*` reason codes; `ExecutionIsForbidden()` guard | Namespace `Taskdeck.Acceleration.V06`; **never returns `Ineligible`** despite the enum and schema having it; no Assist-preset field; boolean inputs assume a fact-gatherer that does not exist |
| C# candidate | `.../candidates/csharp/StableCanaryAllocator.cs` | Deterministic salted cohort allocation | Post-go only; `((hash[0]<<8)\|hash[1]) % 10_000` over a 65 536-value space is modulo-biased — widen the draw before adoption |
| Python | `.../candidates/python/shadow_canary_report.py` | Labelled-rate aggregation with an explicit `unknown` denominator | Standalone; needs repo-native packaging |
| Schema | `.../schemas/authority-shadow-receipt.schema.json` | The receipt contract; `executionPerformed` is `const: false` | Minimal — no principal, operation fingerprint, permission snapshot or compensation field, all of which the bundle's own architecture doc requires |
| Fixture | `.../fixtures/authority-shadow-receipt.example.json`, `authority-shadow-report.example.json`, `shadow-observations.example.json` | Valid receipt/report/observation examples; the report carries `authorizationRecommendation: "human-decision-required"` | Numbers are illustrative |
| TS candidate | `.../candidates/typescript/authorityShadowModel.ts` | Read-only shadow presentation | Presentation must never imply authority |
| Diagram | `.../diagrams/authority-safety-ladder.svg` | The ladder from review-first to bounded authority | Advisory |

## Corrections to the bundle

1. **`ApprovedByUserId` does not exist.** The bundle's "no faked `ApprovedByUserId`" rule points at a field that is not in the model; the shipped field is `AutomationProposal.DecidedByUserId` (with `DecidedAt`, `AppliedAt`, `ApprovedRevisionId`). Write the rule against the real field or the test will assert nothing.
2. **The candidate evaluator cannot produce `Ineligible`.** `AuthorityShadowEvaluator.Evaluate` returns only `WouldAllow` or `WouldDeny`, yet `AuthorityShadowResult`, the schema's `result` enum and the bundle's own "any unknown is ineligible" rule all require the third state. Ineligibility (wrong class) and denial (right class, failed check) are different facts and must not be collapsed.
3. **The Assist preset is named only in titles.** The eligibility list in `03_ARCHITECTURE/AUTHORITY_SHADOW.md` never binds the class to ADR-0057's *Assist*, and the candidate record has no preset field. ADR-0057 §4 requires exactly its preset set; make the preset a recorded, validated field.
4. **"Explicit `Act`" needs the requested/effective distinction.** `CaptureIntentMode.Auto` is documented as an instruction to infer, resolved by a run into `EffectiveIntent`. Only `RequestedIntent == Act` is explicit; the pack's single `ExplicitActIntent` boolean hides the exact place this could silently widen.
5. **The receipt schema is narrower than the architecture doc.** The doc lists principal, operation fingerprint, permission-snapshot result, evidence completeness, risk, ceiling unit and compensation availability; the schema carries none of them. Extend the schema before treating it as the contract.
6. **`AutomationPolicyEngine` is not an authority engine.** It classifies proposal risk and guards decisions on archived boards. Listing it as a coordinator seam is correct as a fence, but the pack's framing invites reuse as the authority evaluator. Read `ClassifyRisk`; add a separate evaluator.
7. **CF-24B `#2277` is a same-milestone sibling, not an earlier prerequisite.** The report cannot be produced before another open v0.6 issue ships, which is a scheduling fact the pack's flat dependency table hides.
8. **GP-06 amendment timing.** The live issue's third acceptance box amends GP-06 "in the same PR". That PR is the execution slice, not the shadow work — a shadow PR that touches GP-06 would record a safety property the product does not have.
9. **Canary allocation is post-go only.** The pack describes it inside the same architecture as shadow; keep it behind slice 07 so no allocation code ships before authorisation exists.
10. **Vocabulary is clean.** Observe/Suggest/Assist/Operate/Autonomous/Custom, Flow/Guided/Control and Private/Balanced/Strict/Expert are used correctly across the pack and candidates; no "Controlled" and no invented preset appears. Nothing to fix.
