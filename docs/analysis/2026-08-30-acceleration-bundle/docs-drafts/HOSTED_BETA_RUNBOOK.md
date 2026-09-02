# Hosted beta runbook (draft)

## Service controls

Operators must be able to:

- close/open registration according to the current stage;
- disable shared LLM allowance globally and per user;
- pause processor intake while preserving captures;
- revoke/rotate API and connector keys;
- put the service in read-only maintenance mode;
- publish status/incident updates;
- start backup, verify backup, restore to an isolated target and promote after checks.

## Daily checks

- readiness and recent deployment/migration state;
- queue depth/oldest job and worker heartbeat;
- LLM/storage/registration quota usage;
- backup age/manifest verification;
- auth/abuse rate-limit signals;
- incident/support inbox;
- telemetry/egress destination health where enabled.

## Incident classes

| Severity | Example | Immediate action |
|---|---|---|
| SEV-1 | suspected cross-user data exposure, secret leak, destructive corruption | close registration, disable mutations/shared key, preserve evidence, notify owner |
| SEV-2 | widespread inability to capture/review/apply, restore required | maintenance mode, status update, restore/rollback decision |
| SEV-3 | one processor/provider degraded, bounded feature unavailable | route/disable processor, preserve captures, communicate known degradation |
| SEV-4 | cosmetic/non-critical | normal issue triage |

## Backup/restore

- Confirm latest verified off-host backup and key custody.
- Restore to a new isolated volume, never over the active DB first.
- Verify manifest, schema, connector decryptability and application-level reads.
- Record RPO gap and elapsed restore time.
- Promote through a reversible volume/deployment switch.
- Retain the old volume until the post-restore window ends.

## Public-gate rollback

At any sign of abuse, isolation uncertainty or cost runaway:

1. close registration;
2. disable shared LLM allowance;
3. pause expensive processors;
4. keep existing users/read paths where safe;
5. publish status/known impact;
6. gather content-free receipts;
7. reopen only after evidence review.

## Evidence after every operation

Record operator, time, version/SHA, command/action, content-free result, backup/restore ID, elapsed time and follow-up issue. Never paste secrets or user content into the runbook receipt.
