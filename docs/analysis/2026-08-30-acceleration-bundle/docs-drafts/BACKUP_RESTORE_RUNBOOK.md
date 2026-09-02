# Backup and restore runbook (draft)

## Backup set

At minimum:

- SQLite database file produced through a consistent backup mechanism;
- schema/application version and image digest;
- checksum manifest;
- configuration inventory with secrets omitted;
- connector-key generation/custody reference, stored separately from data;
- optional external blob/spool state according to the selected storage implementation.

## Backup command behavior

- refuses an unsafe target;
- reports required/available disk;
- creates a temporary output then atomically finalizes;
- emits machine-readable manifest path, sizes and hashes;
- never prints secrets;
- supports verify-only;
- returns stable exit codes.

## Restore rehearsal

1. Select backup and separately retrieve the intended connector key.
2. Verify manifest before starting the app.
3. Restore into a new empty directory/volume.
4. Start the exact production image against the restored target with registration closed.
5. Run migrations only if the procedure explicitly targets a newer version.
6. Run `verify-connector-key` without plaintext output.
7. Verify representative application reads and counts.
8. Verify export of a synthetic account/workspace.
9. Record elapsed time, source backup age and any RPO gap.
10. Destroy the rehearsal target securely.

## Failure matrix

- missing file → fail before mutation;
- checksum mismatch → fail closed;
- wrong key → distinguish from corrupt credential, no plaintext;
- newer DB on older app → refuse unsafe downgrade;
- disk full → preserve source/partial target and report cleanup;
- migration fails → keep restored pre-migration copy;
- operator selects active volume → require explicit protected override or refuse.

## Targets

Recommended beta targets to confirm:

- RPO ≤24 hours;
- RTO ≤2 hours;
- restore drill before private instance and before public registration;
- periodic drill thereafter and after material migration/storage changes.
