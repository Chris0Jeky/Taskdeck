import assert from 'node:assert/strict';
import { test } from 'node:test';
import { selectArtifacts } from './artifact-cleanup.mjs';

const now = Date.parse('2026-08-30T12:00:00Z');
const listing = [
  { id: 1, name: 'container-image-artifacts', size_in_bytes: 170_000_000, expired: false, created_at: '2026-08-01T00:00:00Z' },
  { id: 2, name: 'container-image-artifacts', size_in_bytes: 170_000_000, expired: false, created_at: '2026-08-30T11:00:00Z' },
  { id: 3, name: 'container-image-artifacts', size_in_bytes: 170_000_000, expired: true, created_at: '2026-05-01T00:00:00Z' },
  { id: 4, name: 'frontend-unit-artifacts-ubuntu-latest', size_in_bytes: 2_000_000, expired: false, created_at: '2026-08-10T00:00:00Z' },
  { id: 5, name: 'release-win-x64', size_in_bytes: 50_000_000, expired: false, created_at: 'not-a-date' },
];

test('selectArtifacts ignores expired artifacts and unparsable dates', () => {
  const selection = selectArtifacts(listing, { now });
  assert.deepEqual(selection.selected.map((artifact) => artifact.id), [1, 2, 4]);
  assert.equal(selection.bytes, 342_000_000);
  assert.equal(selection.gb, 0.342);
});

test('selectArtifacts filters by name prefix and age', () => {
  const byPrefix = selectArtifacts(listing, { namePrefix: 'container-image-artifacts', now });
  assert.deepEqual(byPrefix.selected.map((artifact) => artifact.id), [1, 2]);
  const older = selectArtifacts(listing, { namePrefix: 'container-image-artifacts', olderThanDays: 7, now });
  assert.deepEqual(older.selected.map((artifact) => artifact.id), [1]);
  assert.equal(older.count, 1);
});

test('selectArtifacts returns an empty selection for an empty listing', () => {
  assert.deepEqual(selectArtifacts([], { now }), { selected: [], count: 0, bytes: 0, gb: 0 });
});
