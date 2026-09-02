# Collision matrix

This is the practical file-ownership guard for concurrent agents. A row marked **exclusive** must have one owner until its contract PR merges.

| Seam | Primary issues | Mode | Why |
|---|---|---|---|
| `Capture`, `SourceAsset`, capture migrations, `CaptureIntakeService` | #2255, #2256 | **Exclusive contract lane** | IDs, ownership, lifecycle axes and legacy mapping are shared foundations. |
| Processing entities, leases, policy snapshots, runner registration | #2257, then #2259 | **Exclusive until CF-03 contract merges** | Transcript adaptation must consume, not invent, the job/run contract. |
| Worker protocol/manifest/supervisor | #2258, #1429 | **One integration owner; child PRs may parallelise** | Process launch, session proof, cancellation, spool ownership and memory caps cross the same host boundary. |
| Representation header/payload migration | #2260 | **Exclusive contract lane** | #2259 and #2261 depend on one settled lineage/read/write façade. |
| Evidence anchor schema/viewer API | #2261 | Parallel after #2260 contract | Mostly isolated once representation identity is fixed. |
| Blob object/reference tables and source-asset migration | #2276 | Parallel with processing after #2255 contract | Keep `SourceAsset` edits coordinated with CF-01. |
| `Card`, Card DTOs, proposal operations, export/import | #2087, #2092, #2093, #2094 | **Contract PR first; then feature lanes** | Four tickets otherwise collide in the same aggregate and serializers. |
| Hosted auth/registration/security middleware | #2243, #1772, #1308 | **One security owner** | Registration, tenancy, rate/cost controls and telemetry egress share trust assumptions. |
| Chat streaming/provider transport | #2241 | Isolated | Avoid unrelated chat UX changes while transport semantics are changing. |
| CI policy/planner/receipts/quarantine | #2330, #2334, #2336, #2339 | **Wait for baseline/control-plane contracts, then split by schema vs workflow** | Editing workflows before policy and receipt schemas stabilise creates churn and misleading green checks. |
| CLI bootstrap, backup/restore, connector verifier | #1131, #2238, #2239 | One ops integration owner; services may be separate PRs | Command routing and startup ordering are shared. |
| Docs/status/generated trackers | all | Dedicated integration/documentation owner | Feature agents should provide a receipt; one owner resolves generated/documentation drift. |

## Merge rule

A contract PR may add entities, interfaces, schema, migrations and invariant tests, but should avoid behaviour cutover. Behaviour PRs rebase on the merged contract and own one vertical slice. This reduces semantic conflicts even when Git conflicts are low.
