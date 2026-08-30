import assert from 'node:assert/strict';
import { test } from 'node:test';
import { assertDeletable, nextLink, parseArgs, selectArtifacts } from './artifact-cleanup.mjs';

const now = Date.parse('2026-08-30T12:00:00Z');
const pr = (branch) => ({ head_branch: branch });
const listing = [
  { id: 1, name: 'container-image-artifacts', size_in_bytes: 170_000_000, expired: false, created_at: '2026-08-01T00:00:00Z', workflow_run: pr('issue-1/x') },
  { id: 2, name: 'container-image-artifacts', size_in_bytes: 170_000_000, expired: false, created_at: '2026-08-30T11:00:00Z', workflow_run: pr('issue-2/y') },
  { id: 3, name: 'container-image-artifacts', size_in_bytes: 170_000_000, expired: true, created_at: '2026-05-01T00:00:00Z', workflow_run: pr('issue-3/z') },
  { id: 4, name: 'frontend-unit-artifacts-ubuntu-latest', size_in_bytes: 2_000_000, expired: false, created_at: '2026-08-10T00:00:00Z', workflow_run: pr('issue-4/w') },
  { id: 5, name: 'release-win-x64', size_in_bytes: 50_000_000, expired: false, created_at: 'not-a-date', workflow_run: pr('v0.3.0') },
  { id: 6, name: 'container-image-artifacts', size_in_bytes: 170_000_000, expired: false, created_at: '2026-08-02T00:00:00Z', workflow_run: pr('main') },
  { id: 7, name: 'container-image-artifacts', size_in_bytes: 170_000_000, expired: false, created_at: '2026-08-03T00:00:00Z', workflow_run: pr('v0.2.0') },
  { id: 8, name: 'container-image-artifacts', size_in_bytes: 170_000_000, expired: false, created_at: '2026-08-04T00:00:00Z' },
];

test('selectArtifacts ignores expired artifacts, unparsable dates, and never touches main/tag or unknown-origin artifacts', () => {
  const selection = selectArtifacts(listing, { now });
  assert.deepEqual(selection.selected.map((artifact) => artifact.id), [1, 2, 4]);
  assert.equal(selection.bytes, 342_000_000);
  assert.equal(selection.gb, 0.342);
  assert.deepEqual(selection.excluded, { protectedBranchOrTag: 2, unknownOrigin: 1 });
  const everything = selectArtifacts(listing, { now, includeProtected: true });
  assert.deepEqual(everything.selected.map((artifact) => artifact.id), [1, 2, 4, 6, 7, 8]);
});

test('selectArtifacts filters by name prefix, age and an id allow-list', () => {
  const byPrefix = selectArtifacts(listing, { namePrefix: 'container-image-artifacts', now });
  assert.deepEqual(byPrefix.selected.map((artifact) => artifact.id), [1, 2]);
  const older = selectArtifacts(listing, { namePrefix: 'container-image-artifacts', olderThanDays: 7, now });
  assert.deepEqual(older.selected.map((artifact) => artifact.id), [1]);
  const byIds = selectArtifacts(listing, { now, ids: [2, 4, 999] });
  assert.deepEqual(byIds.selected.map((artifact) => artifact.id), [2, 4], 'ids not in the fresh listing are simply absent');
  assert.deepEqual(selectArtifacts([], { now }), { selected: [], count: 0, bytes: 0, gb: 0, excluded: { protectedBranchOrTag: 0, unknownOrigin: 0 } });
});

test('assertDeletable refuses truncated listings, unscoped deletes, missing ids files, and count drift', () => {
  const scoped = { namePrefix: 'container-image-artifacts' };
  assert.match(assertDeletable({ truncated: true, confirmCount: 3, count: 3, idsFileCount: 3, ...scoped }), /truncated/);
  assert.match(assertDeletable({ truncated: false, confirmCount: 3, count: 3, idsFileCount: 3 }), /explicit scope/);
  assert.match(assertDeletable({ truncated: false, confirmCount: 3, count: 3, idsFileCount: null, ...scoped }), /ids-file/);
  assert.match(assertDeletable({ truncated: false, confirmCount: null, count: 3, idsFileCount: 3, ...scoped }), /confirm-count/);
  assert.match(assertDeletable({ truncated: false, confirmCount: 2, count: 3, idsFileCount: 3, ...scoped }), /does not match/);
  assert.match(assertDeletable({ truncated: false, confirmCount: 0, count: 0, idsFileCount: 3, ...scoped }), /nothing to delete/);
  assert.equal(assertDeletable({ truncated: false, confirmCount: 3, count: 3, idsFileCount: 5, ...scoped }), null);
  assert.equal(assertDeletable({ truncated: false, confirmCount: 3, count: 3, idsFileCount: 5, all: true }), null);
});

test('parseArgs defaults to a dry run over artifacts older than 7 days and rejects unsafe values', () => {
  const defaults = parseArgs([]);
  assert.equal(defaults.delete, false);
  assert.equal(defaults.olderThanDays, 7);
  assert.equal(defaults.maxPages, 1000);
  assert.throws(() => parseArgs(['--delete']), /--ids-file/);
  assert.throws(() => parseArgs(['--delete', '--ids-file', 'x.json']), /--confirm-count/);
  assert.throws(() => parseArgs(['--delete', '--ids-file', 'x.json', '--confirm-count', '1']), /--name-prefix <prefix> or --all/);
  assert.equal(parseArgs(['--delete', '--ids-file', 'x.json', '--confirm-count', '1', '--all']).all, true);
  assert.throws(() => parseArgs(['--older-than-days', '-1']), /--older-than-days/);
  assert.throws(() => parseArgs(['--older-than-days', 'abc']), /--older-than-days/);
  assert.throws(() => parseArgs(['--repo', 'not a repo']), /--repo/);
  assert.throws(() => parseArgs(['--bogus']), /Unknown argument/);
  const full = parseArgs(['--delete', '--ids-file', 'r.json', '--confirm-count', '12', '--name-prefix', 'x', '--older-than-days', '3']);
  assert.equal(full.confirmCount, 12);
  assert.equal(full.idsFile, 'r.json');
});

test('nextLink reads the rel="next" pagination link', () => {
  assert.equal(nextLink('<https://api.github.com/x?page=2>; rel="next", <https://api.github.com/x?page=9>; rel="last"'), 'https://api.github.com/x?page=2');
  assert.equal(nextLink('<https://api.github.com/x?page=1>; rel="prev"'), null);
  assert.equal(nextLink(''), null);
});
