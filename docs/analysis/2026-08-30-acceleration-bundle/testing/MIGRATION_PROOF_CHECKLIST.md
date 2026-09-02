# Migration proof checklist

Use this for every EF/SQLite slice, not only Context Fabric.

## Before code

- [ ] Exact source and target schemas documented.
- [ ] Stable identity mapping documented.
- [ ] Null/default/invalid legacy cases enumerated.
- [ ] Point of no lossless down migration stated.
- [ ] Required free disk and backup size estimated.
- [ ] Existing import/export/account-deletion paths identified.

## Forward migration

- [ ] Fresh database boot.
- [ ] Upgrade from the oldest supported fixture.
- [ ] Upgrade from current release fixture.
- [ ] Idempotent application bootstrap.
- [ ] Index/query plan reviewed for hot reads.
- [ ] No long external call inside migration/transaction.

## Backfill

- [ ] Deterministic row mapper.
- [ ] Bounded batch/transaction.
- [ ] Durable checkpoint/version.
- [ ] Safe restart after process kill.
- [ ] Concurrent new writes handled.
- [ ] Malformed rows classified without content leakage.
- [ ] Run twice yields same IDs/counts/hashes.

## Parity/read switch

- [ ] Legacy and native canonical projections compared.
- [ ] Mismatch receipt is content-free and reason-coded.
- [ ] Feature/read switch can revert.
- [ ] Switch proven on seeded integration fixture and dogfood copy.
- [ ] Performance before/after measured.

## Export/delete/import

- [ ] New entities included and ordered.
- [ ] IDs/references round-trip or remap deterministically.
- [ ] Unresolved reference behavior explicit.
- [ ] Account deletion reaches all new rows/blobs.
- [ ] Audit/provenance retention policy explicit.

## Down and rollback

- [ ] Down tested on a database with only legacy-compatible data.
- [ ] Behavior after native-only writes explicitly described.
- [ ] Pre-migration backup restore tested.
- [ ] Application downgrade sequence documented.
- [ ] No destructive automatic retry after failed migration.

## Evidence receipt

- [ ] Fixture hashes and schema versions.
- [ ] Row counts before/after/quarantined.
- [ ] Test commands/results.
- [ ] Timings and DB size delta.
- [ ] Rollback command/result.
- [ ] Remaining incompatibilities.
