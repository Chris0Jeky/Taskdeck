# Executive summary

## Bottom line

Milestone 5 is not 24 independent issues. It is four programs with different readiness and collision patterns:

1. **Context Fabric critical path:** CF-01 → {CF-02, CF-03, CF-23} → {CF-04, CF-06} → {CF-05, CF-07}. This is the strongest immediate development opportunity because the scaffold is already merged and CF-01 can produce an end-to-end vertical.
2. **Work model:** hierarchy, links, assignments/estimates and custom fields can run in parallel only after one owner freezes the shared Card/DTO/export/proposal contract. Milestone-4 #2240 must remain the assignment substrate owner.
3. **Hosted beta and operations:** telemetry/launch material can be prepared, but public registration is gated by private-instance operations, backup/restore, secret decryptability, adversarial user isolation, abuse/cost controls and a revised threat model.
4. **Smart CI depth:** design and schemas can be prepared, but implementation should follow the currently open milestone-4 Smart CI baseline/control-plane PRs. Otherwise the repository risks two competing policy engines and receipt formats.

## Highest-value moves

| Order | Move | Why it accelerates safely |
|---:|---|---|
| 1 | Execute CF-01 as five small PRs | Unlocks four direct descendants and replaces payload parsing with durable truth. |
| 2 | Freeze work-model shared contracts | Lets three agents work without editing Card/DTO/migration/export files concurrently. |
| 3 | Finish v0.3 backup + connector-key proof | Converts the private hosted instance from deployment theory into an operable prerequisite. |
| 4 | Ship true SSE parser/provider slices | Independent, bounded, testable and user-visible; low collision with milestone-4 work. |
| 5 | Split CF-04 before admission | Prevents one huge worker/protocol/security PR and lets containment work reuse one supervisor. |
| 6 | Reconcile stale milestone-4 tickets | #2185 and #2193 primarily need residual proof; #1131 and #1309 need re-scoping, not broad reimplementation. |

## Strong recommendation

Use a **contract-first integration train**:

- one integration owner serializes migrations, model snapshots, shared DTOs and registration files;
- feature agents own bounded verticals and submit generated integration fragments rather than all editing shared files;
- every task carries `owns`, `avoids`, dependencies, verification commands and an evidence receipt;
- issue updates are generated only after code/PR/live-status reconciliation;
- migration slices require forward, backfill, parity, read-switch, rollback and down evidence before legacy state is removed.

## What this bundle already contributes

- all 24 issue packs and paste-ready tracker updates;
- 106 granular milestone-5 work items in a machine-readable queue;
- file collision and dependency maps;
- concrete data models, migration order, state machines and error semantics;
- C# candidates for streaming SSE, hierarchy validation, anchors, custom fields, worker handshake and bounded hashing;
- working Python tools for refactor ranking, shard-union validation, CI reporting, quarantine governance, backup manifests, telemetry payload checks and agent task claims;
- schemas and fixtures for receipts/quarantine/agent handoff;
- issue-ready diagrams.

## Notable tracker drift

- **#2185:** the core archive bug is fixed; residual integration proof, remediation text and generated tracker refresh remain.
- **#2193:** partial-date logic is fixed; deterministic year-boundary and metadata false-positive tests remain.
- **#1131:** CLI key bootstrap and serialized pre-migration backup are present; authorization parity and ops commands remain.
- **#1309:** scopes and stdio identity are substantially implemented; packaging/live smoke and an explicit hash-pin decision remain.
- Several work-model and launch tickets still mention older version assumptions even though their ADR blockers are now Accepted or the work moved to v0.4.
