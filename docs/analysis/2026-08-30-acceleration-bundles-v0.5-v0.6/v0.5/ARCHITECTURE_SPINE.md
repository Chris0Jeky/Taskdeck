# v0.5 bundle — architecture spine in Taskdeck vocabulary

Last Updated: 2026-09-02

> Curated from `taskdeck-milestone-6-acceleration-bundle-2026-08-30` (grounding FAILED: 0 issues, commit unknown) against `main` `79dd57cd3` on 2026-09-02 under tracker #2368. Only issue-agnostic material survived; this file supplies the grounding the generator could not. Planning input, not authority.

The bundle's spine was derived without seeing a single CF issue, yet it lands close to ADR-0065.
Restated below in the shipped vocabulary, with `ContextBinding` and the authority tail put back.

```text
Capture ─ SourceAsset (per-asset modality, immutable)
   │
   ├─ ProcessingPolicySnapshot (CF-03) ─ route decision + receipt (CF-10)
   │
   └─ ProcessingJob ─ ProcessorHost (Worker Protocol v1-alpha) ─ ProcessingRun
                                   │
                                   ├─ Representation (immutable, typed payload, quality state)
                                   │        └─ EvidenceAnchor (TextSpan · TimeRange · PageRegion ·
                                   │                           ImageRegion · JsonPointer · WholeSource)
                                   └─ SemanticCandidate (Action · Decision · Question ·
                                                         Risk · Fact · Reference)
                                                │
                                       ContextBinding (CF-09, at change-planning time)
                                                │
                                       ChangeSet = AutomationProposal
                                                │
                                       AuthorityDecision ─ Execution ─ Receipt
```

Names used above exist today in `backend/src/Taskdeck.Domain/` (`Entities/Capture.cs`,
`Entities/SourceAsset.cs`, the `Enums/` vocabulary, `Processing/ProcessingCapability.cs`) or are
named as draft contracts in `backend/src/Taskdeck.Application/` (`Interfaces/IRepresentationStore.cs`,
`Interfaces/IBlobStore.cs`, `Processing/Protocol/WorkerProtocol.cs`). `AuthorityDecision`,
`Execution` and `Receipt` exist in **no** code: review-first is the only shipped authority policy
(`CONTEXT_FABRIC.md` §4, §6 rule 8).

## The twelve invariants, restated and annotated

| # | Restated in Taskdeck vocabulary | Counterpart | What it adds |
| --- | --- | --- | --- |
| 1 | A `SourceAsset` is immutable and owner-attributed; a correction appends a superseding asset and never rewrites stored bytes | `CONTEXT_FABRIC.md` §6 rule 3; ADR-0065 §Decision 1 | Nothing new. Restates the shipped rule from outside, which is weak corroboration that it is the natural one |
| 2 | A capture is valid once its assets are stored; `CaptureProcessingSummary` may reach `Failed` or `Partial` without touching `CaptureUserDisposition` | §6 rules 1 and 4 | Nothing new; Taskdeck is stronger, because three axes plus `CaptureTimeline` say this structurally rather than by convention |
| 3 | `ProcessingJob` and `ProcessingRun` are the only truth for processing state and processor provenance | ADR §Decision 6; §2 (the `LlmRequest` row is the job record until CF-03 `#2257`) | Nothing new. Names the CF-03 exit condition the same way the ADR does |
| 4 | A `Representation` is immutable, typed by `RepresentationKind`, and content-hashed | ADR §Decision 3 | Nothing new; Taskdeck adds `RepresentationQualityState` and forward supersession, which the bundle omits |
| 5 | Every citation is an `EvidenceAnchor` of one `EvidenceAnchorKind` over a representation or a source asset | ADR §Decision 4 | Nothing new. `EvidenceValidators.cs` supplies one missing rule: a `TimeRange` must be half-open **and** inside the representation's duration |
| 6 | A `SemanticCandidate` is persisted before anything compiles into an `AutomationProposal` | ADR §Decision 5; CF-00 ruling 3 | Nothing new. Adds a deterministic semantic key as the rerun-supersession mechanism (CF-08 `#2262`) |
| 7 | A route decision records the policy it was made under, its cost ceiling and its egress class, and is persisted on the run | ADR §Decision 6; §6 rule 7 | Adds a **policy digest** stored beside the receipt, so a later policy edit cannot silently reinterpret an old route |
| 8 | A remote processor runs only under recorded consent naming the destination and data class | ADR §Decision 6; CF-00 ruling 5 (Balanced default, one-time consent) | Adds fail-closed validation: a snapshot whose egress is not local-only and whose consent flag is unset is invalid before any offer is considered |
| 9 | Confidence never authorises. Permission, evidence completeness and operation risk are separate hard gates | ADR §Decision 9 | Adds the executable form: an irreversible or externally visible operation class is excluded from automation at **any** confidence, and reasons accumulate rather than short-circuit |
| 10 | An automated mutation needs shadow results, a canary cohort, a proven compensating action and a healthy kill switch | ADR §Amendments 10; CF-22 `#2275` | **Conflicts as written.** It omits the maintainer's explicit go, and its companion script hard-codes the struck-through provisional numbers as an expansion verdict |
| 11 | Presentation profiles (Flow · Guided · Control) change disclosure and defaults only; commands, permissions and audit are identical across them | ADR §Three independent policy families; CF-21 `#2274` | Adds a one-function boundary that a spec can assert against, which is what stops profile drift becoming domain drift |
| 12 | Every first vertical ships with a deterministic fixture set and a replay contract | ADR §Decision 8; CF-24A `#2319` | **New as phrased.** The ADR forbids adaptivity before the corpus; making replay a per-vertical entry condition is a stronger reading worth keeping |

Vocabulary note: the processing presets are Private · Balanced · **Strict** · Expert (*Controlled*
was renamed on 2026-08-30), presentation is Flow · Guided · Control, and authority is Observe ·
Suggest · Assist · Operate · Autonomous · Custom. The three stay visibly separate.
