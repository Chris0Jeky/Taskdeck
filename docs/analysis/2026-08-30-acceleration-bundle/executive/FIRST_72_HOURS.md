# First 72 hours: safest acceleration plan

Do not start with 10 runtime agents. Start with one reconciliation/integration owner and four disjoint producers. Expand only after the contract and tracker state are current.

## Coordinator / integration owner

**Task:** ingest this bundle on a docs/data-only branch and reconcile live state.  
**Owns:** issue/PR refresh, accepted destination map, task statuses, EF/shared-file locks, generated docs.  
**Does not implement:** Context Fabric, SSE, telemetry or work-model behavior.  
**Exit:** one ingestion PR plus an updated list of the first executable tasks.

## Agent A: CF-01 backfill contract and fixture

- Task ID: `M5-2255-cf01-1-backfill-service`
- Branch: `issue-2255/cf01-1-backfill-service`
- Deliver: seeded legacy database fixture, ID-preserving mapping table, checkpoint/quarantine contract, idempotency and interrupted-resume tests.
- Fence: no Inbox read switch, no export version change, no shared DbContext/model snapshot without integration-owner handoff.
- Stop condition: malformed-row policy or checkpoint durability requires a maintainer decision.

## Agent B: true SSE parser/decoder

- Task ID: `M5-2241-sse-1-parser`
- Branch: `issue-2241/sse-1-parser`
- Deliver: incremental UTF-8/SSE parser, OpenAI-compatible delta/error/usage decoder, chunk-fuzz fixtures and bounded-size failures.
- Fence: no controller/provider registration changes.
- Stop condition: live provider contract differs materially from the issue assumptions.

## Agent C: telemetry local contract

- Task ID: `M5-1308-tlm-1-contract`
- Branch: `issue-1308/tlm-1-contract`
- Deliver: explicit allowlist schema, off-by-default state/null sink, installation-ID lifecycle, egress test seam and redaction tests.
- Fence: no network endpoint or third-party SDK; transport remains blocked on ownership/retention decisions.

## Agent D: measurement tooling

- Task IDs: `M5-2236-ref-0-measurement-tooling`, then `M5-2237-perf-1-scenario-contracts`
- Branches: one branch/PR per task.
- Deliver: size×churn ranking, benchmark scenario manifests, environment fingerprint and raw-result schema.
- Fence: no production refactor and no threshold enforcement until the v0.3 release tag is frozen.

## First integration review

After these land or reach review:

1. freeze the shared work-model Card/DTO/proposal/export contract;
2. admit CF-02, CF-03 and CF-23 with one EF integration owner;
3. split CF-04 into host/registry, supervisor/protocol and platform-containment lanes;
4. execute the milestone-4 backup/key proof in parallel with no Context Fabric runtime crossover;
5. leave Smart CI v0.4 behavior in design/shadow until the v0.3 control plane is canonical.

## Concurrency expansion rule

Add an agent only when its task has: a merged contract or isolated surface, no unresolved hard dependency, one file-ownership group, a failing/acceptance fixture, and a named integration owner. Agent count follows available independent contracts, not backlog size.
