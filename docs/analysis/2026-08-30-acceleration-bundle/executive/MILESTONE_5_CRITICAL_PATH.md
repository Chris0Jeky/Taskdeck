# Milestone 5 critical path

## Context Fabric

```text
CF-01 durable Capture
  ├─ CF-02 capture dimensions
  ├─ CF-03 jobs/runs
  │    ├─ CF-04 processor hosts ── #1429 PdfPig containment
  │    └─ CF-06 representation headers
  │          ├─ CF-05 semantic.extract migration
  │          └─ CF-07 evidence anchors ── #2089 linked targets/ledger
  └─ CF-23 blob references
```

The fastest path is not to start every branch at once. It is:

1. Freeze CF-01 backfill/read-switch contracts.
2. Run CF-02, CF-03 and CF-23 in parallel with one database integration owner.
3. Split CF-04 and run host/registry plus representation contract work.
4. Deliver PdfPig containment through that host.
5. Move the transcript lane only after representations are writable.
6. Add anchors and linked-target lifecycle after lineage is stable.

## Work model

```text
shared Card/DTO/export/proposal contract
  ├─ item type + parent hierarchy (#2087)
  ├─ typed links (#2092; parent is not a link)
  ├─ custom fields (#2094)
  └─ M4 assignments (#2240) → estimates/roll-ups (#2093)
```

The schema/contract PR is the serialization point. UI/API/MCP/export verticals can branch after it.

## Hosted beta

```text
v0.3 backup + key verification + private instance
   ↓
adversarial isolation + encrypted secrets + abuse/cost limits
   ↓
telemetry/privacy + status/incident + launch evidence
   ↓
controlled public cohort
   ↓
open registration
```

## Smart CI

```text
M4 CI baseline + policy/control plane (#2341/#2342 at snapshot)
   ├─ API shard/harness work (#2330)
   ├─ receipt/report depth (#2336) → flake governance (#2339)
   └─ nightly/release coordinator (#2334)
```
