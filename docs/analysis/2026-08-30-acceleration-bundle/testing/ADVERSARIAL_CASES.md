# Adversarial case catalog

## Ownership and identifier attacks

- User A guesses User B's Capture, SourceAsset, BlobReference, BlobObject, Representation, EvidenceAnchor, Card, link, assignment, custom-field definition/value and export IDs.
- Valid parent object with foreign nested child ID.
- Deleted/tombstoned ID reused after import.
- SignalR subscription claims one board and supplies another resource ID.
- MCP key has read scope but invokes proposal/write or a hidden direct mutation path.
- Stdio host starts with zero or multiple active users.

Expected: stable not-found/forbidden policy with no existence oracle beyond the accepted contract; no mutation or content in logs.

## Migration/backfill attacks

- Corrupt JSON, duplicate legacy IDs in fixture, invalid enum, overlong text, missing owner, source without capture.
- Process killed after checkpoint update but before commit and vice versa.
- New capture arrives during backfill.
- Disk full during index or blob migration.
- Down invoked after native-only feature use.

Expected: deterministic quarantine/abort boundary, no partial identity loss, protected backup and explicit non-lossless downgrade refusal.

## Worker/protocol attacks

- Oversized frame, deeply nested JSON, null union member, numeric enum, unknown capability, undeclared MIME.
- Worker echoes secret, input content or arbitrary stderr.
- Worker ignores cancel, forks child, holds spool handle, opens network, writes beyond output limit.
- Wrong/replayed session proof.
- Memory bomb and timeout race.

Expected: registration/run rejection, process-tree cleanup, one content-free run outcome and host readiness.

## Blob attacks

- Declared 1 MiB stream sends 2 MiB.
- Same hash raced concurrently.
- Foreign reference release.
- Hash-collision simulation through injectable fake hash in tests.
- Transaction fails after physical bytes written but before reference commit.
- Export reads blob while final reference is being released.

Expected: quota before read, owner isolation, unique object/ref correctness and recoverable orphan policy.

## Work-model attacks

- Reparent into descendant; corrupted existing cycle; subtree move exceeding depth.
- Dependency inverse duplicate and cycle.
- Assignment target without access or deactivated after preview.
- Custom-field type changes between preview/apply; retired option; `javascript:` URL; locale number.

Expected: apply revalidates current state, stable conflict, no authority gained.

## Hosted-beta attacks

- Registration/login flood, invite replay, email enumeration, password reset abuse.
- Shared LLM key exhaustion and deliberately huge processor request.
- Cross-user export/account deletion.
- Reverse-proxy bypass to origin, forged forwarded headers, health/readiness information leak.
- Restore wrong backup/key into production, stale backup overwrite, operator lockout.

Expected: bounded rate/cost, isolation, close-registration/kill switches and safe restore refusal.

## CI governance attacks

- New test class omitted from shard manifest.
- Same test in two shards.
- Rerun-green hides first failure.
- Quarantine expires at midnight/timezone boundary.
- Wildcard quarantine expands after new tests are added.
- Same tag SHA qualified twice and ordinary PR artifact promoted.

Expected: governance gate fails visibly and receipt preserves the original evidence.
