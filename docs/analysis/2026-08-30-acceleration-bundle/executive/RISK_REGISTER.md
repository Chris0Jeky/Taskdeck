# Risk register

| Risk | Probability | Impact | Early signal | Mitigation / owner |
|---|---|---|---|---|
| Agents implement stale issue bodies | High | High | Code already satisfies primary AC; duplicate PRs | Coordinator live reconciliation and paste-ready residual scopes |
| EF migrations collide | High | High | Multiple branches edit model snapshot/DbContext | One integration owner; schema contract PR; serialized migration train |
| CF capture/job state is conflated | Medium | Critical | Inbox item becomes Failed/unreadable after processor error | Capture axes separate; job/run truth; projection tests |
| Legacy backfill loses IDs or provenance | Medium | Critical | SourceArtefact/Transcript links break | ID-preserving mapper, seeded parity DB, down proof |
| “Streaming” still buffers whole response | Medium | High | First delta arrives only at completion | Parser/transport timing test and fallback metadata |
| Worker cap exists only in application code | Medium | Critical | Host OOM during xref bomb | OS cap proof on Windows/Linux and one-worker/one-parse model |
| Blob API loads audio into memory | High | High | byte[]/MemoryStream in implementation | Larger-than-buffer contract fixture; real SQLite streaming decision |
| Public instance exposes cross-user data | Medium | Critical | ID enumeration/SignalR/export mismatch | Adversarial owner-surface matrix before registration |
| Hosted shared key incurs abuse cost | High | High | token/cost spike | User keys default, quotas, egress ceiling, close-registration/kill switch |
| Smart CI duplicates M4 control plane | Medium | High | Two policy digests/receipt schemas | Design-only until #2341/#2342 merge; extend canonical contract |
| Sharding multiplies fixed waits | High | Medium | More total compute with same P95 | Remove startup/wait/teardown overhead first |
| Quarantine becomes permanent hiding place | Medium | High | Expired entries or no owner | Schema, expiry gate, issue link, weekly inventory |
| Refactor pass becomes release churn | Medium | High | omnibus PR, formatting noise | Measure at tag, one seam/PR, characterization first |
| Telemetry erodes self-host trust | Medium | High | unexpected egress or opaque fields | off by default, allowlist, network capture, public dictionary |
| Backup exists but restore is unproven | High | Critical | copied file never opened in production image | timed restore, app-level reads, checksum/key verification |
