# Taskdeck v0.3/v0.4 acceleration bundle — live reconciliation

Last Updated: 2026-08-30

## Purpose and authority

The maintainer supplied the extracted `taskdeck-acceleration-bundle-2026-08-30` and authorized a
normal Taskdeck development wave: inspect it, reconcile it against live repository state, update
trackers, seed bounded work, implement useful slices, and carry those slices through the usual
review and verification gates.

The bundle is planning input, reference implementation, and adversarial test material. It is not a
second source of truth. Current precedence remains:

1. live Git, GitHub issues/PRs/checks, and executable tests;
2. `docs/STATUS.md`, accepted ADRs, and the nearest applicable `AGENTS.md`;
3. this reconciliation record;
4. the extracted bundle and its snapshot-derived task queue.

The 220 received files are deliberately not committed wholesale. Doing so would turn stale issue
copies, candidate code, generated diagrams, and an already-diverged task queue into competing
project authority. Useful contracts are adapted into live issues and small repository-native PRs.

## Source and validation receipt

| Check | Result | Interpretation |
| --- | --- | --- |
| Extracted inventory | 220 files, 26 directories, 1,826,723 bytes | Matches the bundle's advertised file count |
| Bundle baseline | `221aa88c80f5b2c3265ac794edc2ade0edd70c72` | The snapshot is real and reachable in Taskdeck history |
| Live reconciliation head | `ca93903c8e64b37b8e6187b5eac138b732f47264` | 42 commits after the baseline; 116 files changed, 16,816 insertions, 159 deletions |
| `py -3 -B 07_AGENT_HANDOFF/scripts/verify_bundle.py` | Pass: `Bundle valid` | Internal manifest/checksum/required-file checks reproduced |
| `py -3 -B 07_AGENT_HANDOFF/scripts/validate_task_queue.py 07_AGENT_HANDOFF/task-queue.json` | Pass | The received queue is internally well-formed, not necessarily live-current |
| Bundle Python utility suite | **11 passed, 1 failed** | The advertised 12/12 result does not reproduce on Windows; details below |
| C# candidate compilation | Pass, 0 warnings / 0 errors | All isolated candidates compile together under a temporary .NET 8 project; this does not prove repository integration |
| Diagram inspection | Milestone graph, Context Fabric lifecycle, work model, worker boundary, and hosted-beta gates inspected | Diagrams are useful explanatory maps; issue/ADR state still controls execution |
| ZIP SHA-256 `c37209ef…3dac2a` | NOT verified | Only the extracted directory is present in this checkout; the original ZIP was not available to hash |

The Python failure is deterministic on this Windows host:

```text
test_absolute_manifest_path_is_rejected
expected: path_invalid:/etc/passwd
actual:   file_missing:/etc/passwd
```

`Path('/etc/passwd').is_absolute()` is false under Windows path semantics, so
`backup_manifest.py` reaches the missing-file branch. The backup-manifest candidate must not be
adopted unchanged. A portable validator must reject both POSIX-rooted and Windows-rooted manifest
paths independent of the host that performs verification.

## Live-state corrections to the received snapshot

At reconciliation time, milestone 4 has 59 open / 27 closed issues and milestone 5 has 27 open /
1 closed issue. The bundle recorded 61/18 and 24/0 respectively. The difference is delivery, not
minor metadata drift.

| Bundle claim or starter lane | Live state | Disposition |
| --- | --- | --- |
| CF-01 `#2255` backfill is the first producer lane | PR `#2344` merged and `#2255` closed; durable capture/backfill/read-switch foundation is on `main` | **Superseded.** Continue through explicit residuals `#2345` and `#2347`, then the dependency graph |
| `#2241` needs a new OpenAI-compatible SSE parser | The provider has streamed real SSE since `36e9efda2` and was hardened in later commits | **Mostly superseded.** AC2/AC4/AC6 were checked after 152 focused tests passed; AC3 API-level proof and a maintainer-key live smoke remain |
| Smart CI baseline/control plane is a future prerequisite | ADR-0066, baseline, shadow planner/gate, action-pin inventory, and artifact-retention work have landed | **Adapt.** CI-06/10/12/15 extend the existing control plane; no parallel planner, receipt, or required-check policy |
| Milestone-4 archive fix is PR `#2222` | Correct archive fix is PR `#2216`; PR `#2222` is an unrelated frontend Enter-key fix | **Correct the reference before use** |
| Milestone-4 partial-date fix is PR `#2214` | Correct primary fix is PR `#2206`; PR `#2214` does not exist in this repository | **Correct the reference before use** |
| Four parallel producers can start immediately | Required checkout fingerprinting and the worktree helper both reject this canonical OneDrive checkout because the relevant roots carry `ReparsePoint` | **No bypass.** Move execution to a short, non-reparse checkout where the repository helper, printed guard, and initializer all succeed; otherwise park the write lane. Portability evidence is on `#1711` |
| Machine dependency graph is ready for orchestration | The rendered graph is useful, but the JSON repeats many ordered pairs as both `depends-on` and `unblocks` without documenting edge direction | **Advisory only.** Derive dependencies from live issue bodies/ADRs before admission |

## Component disposition

| Bundle component | Decision | How Taskdeck uses it |
| --- | --- | --- |
| `00_EXECUTIVE` | Adapt | Use the critical-path framing and staged launch posture; replace snapshot counts and starter lanes with live evidence |
| `01_MILESTONE_5` | Adapt per issue | Keep edge cases, invariants, and child-slice ideas only after reading the live issue and accepted ADR |
| `02_ARCHITECTURE` | Adopt concepts with ADR precedence | The single capture writer, immutable sources, job/run separation, evidence lineage, one worker supervisor, contract-first work model, and hosted gates align with accepted direction |
| `03_IMPLEMENTATION_CANDIDATES/csharp` | Reference only | Compile-shaped and useful for adversarial cases; do not copy over existing production seams or bypass repository namespaces, DI, auth, persistence, and error contracts |
| `03_IMPLEMENTATION_CANDIDATES/python` | Selective adaptation | REF-0 ranking is useful after hardening; backup manifest is non-portable as received; other tools remain opt-in references until a live issue owns them |
| `04_TESTING` | Adopt as test ideas, not pass evidence | Fixtures and adversarial matrices can strengthen repository tests; the bundle's historical 12/12 claim is not current-machine evidence |
| `05_DIAGRAMS` | Explanatory | Reuse relationships when they clarify an issue; do not make rendered arrows a dependency authority |
| `06_MILESTONE_4_AUDIT` | Adapt and correct | The residual-first method is sound; PR identifiers and several shipped-state claims must be re-read live |
| `07_AGENT_HANDOFF` | Adapt | File collision groups are useful ownership fences; queue states/dependencies are stale and claims cannot override Project WIP or the OneDrive guard failure |
| `08_DOCS_DRAFTS` | Source material | Incorporate only claims that remain true in current docs/code; never create a parallel canonical manual |
| `09_SOURCE_LEDGER` | Retain as provenance evidence | It explains the bundle snapshot and validates internal integrity; it does not validate post-snapshot Taskdeck state |
| `dashboard.html` | Snapshot visualization | Useful for exploration, not a live project dashboard |

## Reconciled workstream map

### Context Fabric

Adopt the bundle's dependency shape, but start from shipped CF-01 reality:

```text
#2347 correctness repair + #2345 JSON-twin retirement
  -> #2256 dimensions / #2257 jobs-runs / #2276 blob references
  -> #2258 worker host / #2260 representations
  -> #2259 semantic extraction / #2261 evidence anchors
```

The current ADR-0065 amendments, `CaptureIntakeService` single-writer contract, three capture state
axes, `SourceAsset`, and live issue amendments supersede the bundle's earlier field shapes. This
sequence reduces capture-maintenance overhead without weakening review-first proposal semantics.

### Work model

Keep the shared-contract freeze. `Card`, DTOs, proposal operations, exports, realtime contracts, EF
snapshots, assignments, relations, and custom fields must not be independently reinvented by
parallel lanes. ADR-0060 and ADR-0062 are accepted, but `#2240` has a live design fork: Taskdeck has
no existing single-assignee field to generalize. Resolve that tracker contract before treating
`#2093` as an implementation-ready extension.

### Worker containment

Adopt one supervisor for CF-04 `#2258` and PdfPig containment `#1429`. The bundle's registry → host
→ sidecar supervisor → OS boundary → conformance path matches the live Worker Protocol direction.
Do not create a PdfPig-specific second process host. Runtime work remains sequenced behind CF-03 and
the current protocol residuals.

### Hosted beta and v0.3 private instance

Adopt the operating-model gates: downloadable beta → backup/key proof → private trusted instance →
small-team isolation → controlled untrusted cohort → open registration. Public registration remains
last. `#2238` and `#2239` are engineering prerequisites for private-instance `#1772`; CL-1 still owns
the collaborator, budget/alert, and retention values. `#2243` is not a container-start ticket and
does not authorize deployment, billing, account creation, public registration, or telemetry.

### Smart CI

Use the bundle's schemas/algorithms only through ADR-0066 and CI-00 `#2324`. The v0.4 depth issues
`#2330`, `#2334`, `#2336`, and `#2339` consume the merged baseline and shadow control plane. They do
not introduce another required gate, policy source, or receipt schema.

### OpenAI-compatible streaming

Do not adopt the isolated parser over the mature provider. Use the bundle's chunk-boundary,
multi-data, malformed-event, mid-stream-error, cancellation, usage, limit, and fallback cases as an
adversarial checklist. The remaining repository seam is an API contract test proving that
`ChatController` flushes a real provider delta before completion while buffered providers remain
compatible, followed by a redacted live smoke supplied with a maintainer key.

### Refactoring and performance

Adopt the explainable score:

```text
ln(1 + lines) × ln(1 + churn) × sqrt(max(1, touching_commits))
```

REF-0 `#2236` lands repository-native measurement tooling and deterministic fixtures without
changing product behavior. The authoritative REF-1 baseline waits for the exact final `v0.3.0`
tag. The top 20 are reviewed manually; five selected seams become separate characterization-first
issues. Generated files, vendor trees, locks, migrations/snapshots, binaries, dirty worktrees, Git
renames, and unresolved refs require explicit behavior rather than implicit best effort.

### Telemetry and feedback

The bundle's local no-op/allowlist contract is a useful safe shape, but `#1308` still requires the
maintainer's explicit Option A (zero telemetry) or Option B (granular opt-in) decision. No network
transport, third-party key, install identifier, or silent collection is authorized by this bundle.

## First admitted wave

The live Project v2 queue had two of four `Now` slots occupied. This reconciliation admitted exactly
two independent Priority II issues, producing the full 4/4 queue:

| Issue | Status | Ownership | Completion boundary |
| --- | --- | --- | --- |
| `#1271` | Now | Existing dogfooding lane | Unchanged |
| `#2230` | Now | Existing Inbox frontend lane / PR `#2299` | Unchanged |
| `#2348` | Now | This reconciliation record and a narrow master-plan pointer | Live disposition, validation caveats, tracker receipts |
| `#2236` | Now | `scripts/analysis/**`, focused tests, `docs/analysis/refactoring/**` | REF-0 tooling only; no baseline/refactor |

`#2241` remains Pending after its issue checkboxes and residual scope were reconciled. No additional
issue enters `Now` until one of the four current lanes moves to Review, Done, or Blocked.

## Candidate-code admission contract

Candidate code enters Taskdeck only when all of the following are true:

- a live issue owns the exact behavior and Project priority/status is synchronized;
- current source does not already provide the behavior;
- the implementation is adapted to Taskdeck namespaces, boundaries, error contracts, auth and DI;
- tests cover the candidate's adverse cases plus repository integration;
- migration, rollback, export/delete/import, or deployment evidence is present when the seam needs it;
- the PR records exact base/head, changed files, commands/results, NOT verified items, and residuals;
- normal review, CI, aging, and merge gates pass.

Bundle receipt claims, source review, or standalone compilation never substitute for these gates.

## Tracker and instruction receipts

- Root `AGENTS.md` now requires REST fallback through `gh api` when GraphQL quota is exhausted and a
  REST equivalent exists; genuinely GraphQL-only work, such as Project v2 field mutation, waits for
  reset while unrelated work continues.
- `#1711` records the exact fingerprint and helper failures for a OneDrive-root checkout. No safety
  check was weakened.
- `#2241` records the source/history reconciliation, 152/152 focused tests, checked AC2/AC4/AC6,
  and its two remaining proofs.
- `#2236` records the hardened REF-0/REF-1 split and ownership fence.
- `#2348` is the live owner of this ingestion/reconciliation pass.

## NOT verified and retained human gates

- The original ZIP checksum was not recomputed because the ZIP is absent.
- No real OpenAI-compatible endpoint smoke was run; no maintainer key was requested or read.
- Docker is unavailable on this machine, so production-image backup/restore and worker-containment
  drills were not run.
- No hosted deployment, registration, repository visibility, branch protection, runner, billing,
  artifact deletion, release/tag, signing, or account setting changed.
- No telemetry decision was inferred.
- No delegated agent was used. The canonical OneDrive checkout remains ineligible for issue-worker
  writes; review-fix execution moved to a short, non-reparse checkout and passed the repository
  helper, printed guard, and initializer without weakening them.

All 42 open checkbox rows in [`OUTSTANDING_TASKS.md`](../../../OUTSTANDING_TASKS.md) remain open at
this snapshot. This list surfaces them; the source file retains their full context, rulings, and
completion conditions:

- Release/private-instance decisions: RT-1, RT-2, RT-3, CL-1, BEN-1, and DIST-1.
- Residual implementation: `#1133` rest, Cohort metrics stub (`#1142`), Ollama real streaming,
  `#1134` remaining acceptance criteria, `#1131`, `#1166`, `#1138` rest, and
  `#1135` / `#1140` / `#1141` / `#1139`.
- Generative/revival checkpoints: GEN-00 (`#1327`), the GEN-12/GEN-11 maintainer checkpoint,
  REVIVAL-00 (`#1311`), Phase 0 dogfooding, Phase 2 transcript engine, Phase 3 slim + launch, and the
  re-anchored revival checkpoint.
- Standing trackers and gates: `#1271`, `#1276`, `#1277`, `#1291`, `#1504`, `#1482`, `#1323`,
  `#1770`, `#1821`, and `#2012`.
- Device/secret actions: set the second-machine `Llm__OpenAi__ApiKey`; resolve the OneDrive
  checkout's frontend-test environment blockers.
- Delegated-authority and Smart CI gates: CF-22 and SC-1 through SC-8.

## Next safe slices

1. Finish and review REF-0 `#2236`; run it provisionally against a non-authoritative base only to
   prove the tool, then wait for `v0.3.0` before committing the ranking baseline.
2. When a WIP slot opens, add the API-level `#2241` streaming contract test; leave the live smoke
   visibly unverified until a maintainer key is supplied for that purpose.
3. Prefer CF-01c `#2347` before broad new Context Fabric verticals because it repairs a known
   divergence/disposition correctness seam shipped by CF-01.
4. Keep `#2238` / `#2239` as separate production-image and key-verification PRs, then run one timed
   restore drill before private-instance `#1772` evidence.
5. Resolve `#2240`'s assignment substrate fork before the v0.4 work-model contract train.

## 2026-09-02 follow-up

This record is the 2026-08-30 disposition and stays as written. The 2026-09-02 re-pass under `#2376` archived the
bundle's durable material beside it and validated every issue pack against `main` `de488fea0`; read
`HEAD_START.md` for what shipped since, the re-validation receipt, per-issue findings and the startable slices, and
`issues/<n>-*.md` for the head-start of a specific issue. Items 1, 3 and 4 of the next safe slices above are now
delivered or in flight (`#2356` open for REF-0; `#2238` / `#2239` closed); items 2 and 5 remain.
