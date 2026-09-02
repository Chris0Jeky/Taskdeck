# Milestone 5 acceleration index

Milestone: **v0.4 — Hosted Open Beta + Work Model + Fabric Foundation**  
Snapshot: **2026-08-30**, commit `221aa88c80f5b2c3265ac794edc2ade0edd70c72`

## Status matrix

| Issue | Title | Stream | Recommended state | Dependencies | Priority |
|---|---|---|---|---|---|
| [#2255](https://github.com/Chris0Jeky/Taskdeck/issues/2255) | CF-01: Durable Capture aggregate | Context Fabric | `implementation-ready-critical-path` | — | critical |
| [#2256](https://github.com/Chris0Jeky/Taskdeck/issues/2256) | CF-02: Split capture dimensions | Context Fabric | `ready-after-cf01-contract` | #2255 | high |
| [#2257](https://github.com/Chris0Jeky/Taskdeck/issues/2257) | CF-03: ProcessingJob and ProcessingRun | Context Fabric | `ready-after-cf01` | #2255 | critical |
| [#2276](https://github.com/Chris0Jeky/Taskdeck/issues/2276) | CF-23: IBlobStore and SQLite reference semantics | Context Fabric | `ready-after-cf01` | #2255 | critical |
| [#2089](https://github.com/Chris0Jeky/Taskdeck/issues/2089) | [Capture] Linked targets, disposition ledger and advanced lifecycle | Context Fabric | `design-ready-blocked` | #2255, #2261, #2087 | medium |
| [#2259](https://github.com/Chris0Jeky/Taskdeck/issues/2259) | CF-05: Transcript triage through semantic.extract | Context Fabric | `blocked-by-cf03-cf06` | #2257, #2260 | high |
| [#2261](https://github.com/Chris0Jeky/Taskdeck/issues/2261) | CF-07: Typed EvidenceAnchor | Context Fabric | `ready-after-cf06` | #2260 | high |
| [#2260](https://github.com/Chris0Jeky/Taskdeck/issues/2260) | CF-06: Representation façade and headers | Context Fabric | `ready-after-cf01-cf03` | #2255, #2257 | critical |
| [#1429](https://github.com/Chris0Jeky/Taskdeck/issues/1429) | Memory-capped extraction worker process | Worker containment | `contract-ready` | #2257, #2258 | critical |
| [#2258](https://github.com/Chris0Jeky/Taskdeck/issues/2258) | CF-04: Worker Protocol v1-alpha and processor hosts | Worker containment | `split-required-after-cf03` | #2257 | critical |
| [#2087](https://github.com/Chris0Jeky/Taskdeck/issues/2087) | [Work model] Minimal item types and optional parent hierarchy | Work model | `ready-after-contract-freeze` | — | high |
| [#2092](https://github.com/Chris0Jeky/Taskdeck/issues/2092) | [Work model] Minimal typed work-item links | Work model | `ready-after-contract-freeze` | #2087 | high |
| [#2094](https://github.com/Chris0Jeky/Taskdeck/issues/2094) | [Work model] Minimal typed custom-field foundation | Work model | `ready-after-contract-freeze` | #2087 | medium |
| [#2093](https://github.com/Chris0Jeky/Taskdeck/issues/2093) | [Work model] Participants, multiple assignments, estimates and roll-ups | Work model | `blocked-by-m4-slice` | #2240, #2087 | high |
| [#1308](https://github.com/Chris0Jeky/Taskdeck/issues/1308) | REVIVAL-12: Beta feedback + telemetry posture | Hosted beta / launch | `partially-ready` | — | high |
| [#1310](https://github.com/Chris0Jeky/Taskdeck/issues/1310) | REVIVAL-14: Open-beta launch kit | Hosted beta / launch | `design-ready` | #1308, #2243 | medium |
| [#2243](https://github.com/Chris0Jeky/Taskdeck/issues/2243) | [Epic] Hosted open beta | Hosted beta / launch | `gated-epic` | #1772, #2238, #2239, #1308, #1310 | critical |
| [#2241](https://github.com/Chris0Jeky/Taskdeck/issues/2241) | [LLM] True SSE streaming for OpenAI-compatible endpoints | LLM runtime | `implementation-ready` | — | high |
| [#2236](https://github.com/Chris0Jeky/Taskdeck/issues/2236) | [Tech debt] v0.4 refactoring pass | Quality / performance | `measurement-ready` | — | medium |
| [#2237](https://github.com/Chris0Jeky/Taskdeck/issues/2237) | [Performance] v0.4 benchmark and ranked improvements | Quality / performance | `harness-ready-baseline-blocked` | — | high |
| [#2330](https://github.com/Chris0Jeky/Taskdeck/issues/2330) | CI-06: API behavioural shards and Windows process overhead | Smart CI depth | `design-ready-blocked` | #2324 | medium |
| [#2334](https://github.com/Chris0Jeky/Taskdeck/issues/2334) | CI-10: Nightly coordinator and clean-tag release qualification | Smart CI depth | `design-ready-blocked` | #2324 | medium |
| [#2336](https://github.com/Chris0Jeky/Taskdeck/issues/2336) | CI-12: CI receipts and weekly cost/flake report | Smart CI depth | `design-ready-blocked` | #2324 | medium |
| [#2339](https://github.com/Chris0Jeky/Taskdeck/issues/2339) | CI-15: Fail-visible flakes and expiring quarantine | Smart CI depth | `design-ready-blocked` | #2336 | medium |

## Admission policy

- **Now:** CF-01 slices, true SSE parser/provider, telemetry local contract, benchmark/refactor tooling, work-model contract freeze.
- **Next:** CF-02, CF-03 and CF-23 after CF-01 contract/schema; work-model behavior slices after shared contract; v0.3 ops proofs.
- **Blocked but productive:** CF-04/worker containment, CF-05, CF-06/07, hosted public registration and Smart CI depth can produce contracts, fixtures and decisions without prematurely editing runtime paths.
- **Tracker sync:** amend issue bodies/comments that still name an ADR blocker already resolved or a version assignment superseded by the milestone realignment.

## Issue pack format

Each file in `issue-packs/` includes current-state reconciliation, target architecture, child PRs, collision fences, edge cases, tests, rollout/rollback and a paste-ready issue update. The same update is also available alone in `issue-comments/`.
