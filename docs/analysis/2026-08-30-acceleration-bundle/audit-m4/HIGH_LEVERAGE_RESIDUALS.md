# High-leverage milestone-4 residuals

## Operations pair: #2238 + #2239

Treat these as one release proof with separate code PRs:

```text
production image
  ├─ ops backup → versioned manifest/checksums/off-host copy
  ├─ ops verify-connector-key → no plaintext, stable exit codes
  └─ ops restore → isolated target → checksum/key/schema/app-read verification
```

The restore drill must use the exact container/runtime image and record elapsed time. A database file that copies successfully but cannot decrypt connector credentials is not a valid recovery.

## CLI residual: #1131

Introduce an explicit command context:

```text
CLI invocation → resolved actor/operator mode → authorization policy → application command
```

Normal domain mutations require a real actor and the same server-side authorization as HTTP. Operator-only commands are isolated under `ops`, emit machine-readable content-free results and do not masquerade as user actions.

## MCP residual: #1309

- ship one documented package/entrypoint;
- provide least-privilege HTTP and stdio configurations;
- run a hostile-write smoke that proves proposal-only behavior;
- record tool hash changes, but gate runtime pin enforcement behind an explicit decision.

## Tracker closure pair: #2185 + #2193

These are ideal low-conflict test-only tasks for separate agents. Each should end with a concise issue update naming the merged primary fix, residual test PR and exact closure evidence.

## Assignment bridge: #2240 → #2093

Publish a small compatibility note as part of #2240:

- assignment table/API identity;
- eligible principal rule;
- uniqueness and audit;
- export/import ordering;
- extension points for nullable estimate and participant roll-ups.

That note prevents v0.4 from inventing a parallel model.
