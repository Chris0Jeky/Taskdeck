# Decision queue

These are the decisions that should be resolved before an agent reaches the corresponding boundary. Defaults below are recommendations, not silently assumed product decisions.

| ID | Issue(s) | Decision | Recommended default | Blocks |
|---|---|---|---|---|
| D-01 | #2255 | Backfill checkpoint and batch transaction | Durable checkpoint table; 250–1,000 rows per transaction; idempotent row mapper | CF-01 implementation detail |
| D-02 | #2257 | Lease duration/renewal | Lease ≥ 3× heartbeat; renew at 1/3; DB time where possible | CF-03 runner |
| D-03 | #2258 | Options-schema validator dependency | Use a mature JSON Schema implementation if already compatible; otherwise support a deliberately tiny documented subset | CF-04 capability options |
| D-04 | #2258 | `concurrent-jobs` | Default 1; explicit positive integer per manifest; host enforces declared maximum | CF-04 host |
| D-05 | #1429 | Spawn/IPC failure fallback | Fail closed to a warning/run failure once worker mode is enabled | Containment credibility |
| D-06 | #2276 | SQLite large-blob strategy | Prototype incremental BLOB I/O first; bounded chunks second; spool-then-store only with measured constraints | CF-23 implementation |
| D-07 | #2087 | Archived-parent behavior | Archived parent cannot receive new children; existing children remain/detach per explicit command | Hierarchy commands |
| D-08 | #2092 | Dependency cycles | Reject cycles for blocks/depends-on; allow relates-to cycles by nature | Relation validator |
| D-09 | #2093 | Estimate storage | Nullable non-negative integer minutes; display converts units | Estimate schema |
| D-10 | #2094 | Numeric/date/URL representation | Bounded decimal; date-only ISO-8601; http/https URL only | Custom-field validator |
| D-11 | #1308 | Telemetry endpoint/retention | First-party endpoint, explicit opt-in, 30-day raw maximum, public aggregate dictionary | Network sink |
| D-12 | #2243 | Public-beta tenancy | Do not open registration on shared SQLite until every owner-scoped surface passes adversarial isolation; consider isolated instances if proof cost is excessive | Public gate |
| D-13 | #2243 | RPO/RTO | Beta target RPO ≤24h, RTO ≤2h, measured restore drill | Operations gate |
| D-14 | #2243 | Hosted LLM payer | User-owned key by default; bounded shared allowance only where cost/abuse controls are proven | Cost gate |
| D-15 | #2241 | Mid-stream failure semantics | Do not retry an arbitrary partial stream; surface explicit partial/failure metadata and preserve cancellation | SSE provider |
| D-16 | #2339 | Quarantine lifetime | 14-day default, 30-day hard maximum without maintainer exception | Flake governance |
| D-17 | #1309 | Runtime tool hash pinning | Treat as a separate accepted security feature; do not infer enforcement from the existing hash recorder | MCP residual |
| D-18 | #2238/#2239 | Connector key custody | Backup data and key custody separately; restore verifier must distinguish missing/wrong/corrupt key | Private instance |
