import test from 'node:test';
import assert from 'node:assert/strict';
import { canonical, hash, validPath, glob } from '../core/primitives.mjs';
import { parseTree, snapshot, changedPaths } from '../core/snapshot.mjs';
import { validateContracts, expandAffected, fingerprint } from '../core/contracts.mjs';
import { mkdtempSync, writeFileSync, mkdirSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { execFileSync } from 'node:child_process';
import { graph, state, request, entry, oid } from './fixtures.mjs';

test('canonical object order does not change hashes; array order does', () => {
  assert.equal(hash({ a: 1, b: 2 }), hash({ b: 2, a: 1 }));
  assert.notEqual(hash([1, 2]), hash([2, 1]));
});
for (const bad of [undefined, NaN, Infinity, new Date(), () => 1]) test(`canonical rejects ${String(bad)}`, () => assert.throws(() => canonical(bad)));
for (const path of ['', '../x', '/root/x', 'a//b', 'a/./b', 'a\\b', 'C:/x', 'a\nb', 'a\tb']) test(`unsafe path rejected ${JSON.stringify(path)}`, () => assert.equal(validPath(path), false));
test('spaces and Unicode paths retained exactly', () => assert.equal(validPath('src/a spaced λ.ts'), true));
for (const [pattern, yes, no] of [['**/*.ts', 'a.ts', 'a.js'], ['frontend/**', 'frontend/a/b.ts', 'backend/a.ts'], ['a/?/*.cs', 'a/b/x.cs', 'a/b/c/x.cs']]) {
  test(`glob ${pattern}`, () => { assert.ok(glob(pattern).test(yes)); assert.ok(!glob(pattern).test(no)); });
}
for (const pattern of ['[ab].cs', '!a', '{a,b}', '../*']) test(`unsupported pattern ${pattern}`, () => assert.throws(() => glob(pattern)));

test('valid contracts', () => assert.equal(validateContracts(graph()).version, 1));
for (const mutation of [g => g.components.shared.deps.push('backend'), g => g.components.backend.deps.push('missing'),
  g => g.tasks.backend.components.push('missing'), g => g.tasks.backend.environmentKeys = ['os'],
  g => g.tasks.backend.command = [], g => g.tasks.backend.ttlSeconds = 0, g => g.globalInputs = []]) {
  test(`invalid graph ${String(mutation)}`, () => { const g = graph(); mutation(g); assert.throws(() => validateContracts(g)); });
}
test('reverse transitive propagation includes integration consumer', () => assert.deepEqual(expandAffected(graph(), [], ['shared/types.json']).selected, ['backend', 'e2e', 'frontend']));
test('selection only adds to canonical floor', () => assert.deepEqual(expandAffected(graph(), ['docs'], ['backend/main.cs']).selected, ['backend', 'docs', 'e2e']));
test('unknown path escalates all', () => assert.equal(expandAffected(graph(), [], ['brand-new/file.ts']).selected.length, 4));
test('bad path escalates all', () => assert.equal(expandAffected(graph(), [], ['../file']).fallback, 'invalid-path'));
test('unknown canonical lane fails closed', () => assert.throws(() => expandAffected(graph(), ['not-real'], [])));
test('global input affects every lane', () => assert.equal(expandAffected(graph(), [], ['lock.json']).selected.length, 4));
test('empty change cannot discard canonical selected tasks', () => assert.deepEqual(expandAffected(graph(), ['backend'], []).selected, ['backend']));

test('backend survives frontend-only revision including changed overall tree/commit', () => {
  const r = request(), original = fingerprint(r); r.state.entries[1].oid = oid('b'); r.state.commit = oid('3'); r.state.tree = oid('4');
  assert.equal(fingerprint(r).key, original.key); assert.ok(original.reusable);
});
test('input enumeration is order independent', () => { const r = request(), k = fingerprint(r).key; r.state.entries.reverse(); assert.equal(fingerprint(r).key, k); });
const mutations = {
  source: r => r.state.entries[0].oid = oid('b'),
  test: r => r.state.entries.push(entry('backend/tests/new.cs', 'c')),
  delete: r => r.state.entries.shift(),
  rename: r => r.state.entries[0].path = 'backend/renamed.cs',
  mode: r => r.state.entries[0].mode = '100755',
  lock: r => r.state.entries[4].oid = oid('b'),
  shared: r => r.state.entries[2].oid = oid('b'),
  policy: r => r.policyDigest = `sha256:${'b'.repeat(64)}`,
  command: r => r.graph.tasks.backend.command.push('--different'),
  image: r => r.environment.image = '20260909.2',
  dependencies: r => r.environment.dependencies = 'new-resolved-digest',
  event: r => r.context.config = 'debug',
  repo: r => r.repositoryId = '9999',
  closure: r => r.graph.components.backend.deps.push('frontend')
};
for (const [name, mutate] of Object.entries(mutations)) test(`${name} invalidates fingerprint`, () => { const r = request(), k = fingerprint(r).key; mutate(r); assert.notEqual(fingerprint(r).key, k); });
for (const [name, mutate] of Object.entries({ unknownImage: r => r.environment.image = 'ubuntu-latest', floatingSDK: r => r.environment.node = '24.x',
  noContext: r => delete r.context.config, unreviewed: r => r.graph.tasks.backend.reviewed = false,
  alwaysFresh: r => r.graph.tasks.backend.reuse = 'never', missingRequired: r => r.graph.tasks.backend.requiredInputs = ['not-here'],
  symlink: r => r.state.entries.push(entry('unrelated/link', 'a', '120000')), submodule: r => r.state.entries.push(entry('module', 'a', '160000')) })) {
  test(`${name} disables result reuse`, () => { const r = request(); mutate(r); assert.equal(fingerprint(r).reusable, false); });
}
for (const [name, mutate] of Object.entries({ incomplete: r => r.state.complete = false, duplicate: r => r.state.entries.push(r.state.entries[0]),
  malformed: r => r.state.entries[0].path = '../x', wrongMode: r => r.state.entries[0].mode = '040000',
  wrongType: r => r.state.entries[0].type = 'commit', wrongRepo: r => r.repositoryId = 'owner/name' })) {
  test(`${name} snapshot/input throws rather than creating cheap plan`, () => { const r = request(); mutate(r); assert.throws(() => fingerprint(r)); });
}

test('NUL tree parser preserves spaces', () => assert.equal(parseTree(`100644 blob ${oid('a')}\tfile with spaces\0`)[0].path, 'file with spaces'));
for (const data of [`100644 blob ${oid('a')}\ta`, `100644 blob ${oid('a')}\ta\nb\0`, `100644 commit ${oid('a')}\ta\0`, Buffer.from([255, 0])]) {
  test(`malformed inventory ${JSON.stringify(String(data).slice(0, 20))}`, () => assert.throws(() => parseTree(data)));
}
test('endpoint diff includes both rename sides and permission changes', () => {
  const a = state(), b = structuredClone(a); b.entries[0].path = 'backend/new.cs'; b.entries[1].mode = '100755';
  assert.deepEqual(changedPaths(a, b), ['backend/main.cs', 'backend/new.cs', 'frontend/main.ts']);
});
test('real Git snapshots hash immutable objects, ignoring dirty working tree', () => {
  const dir = mkdtempSync(join(tmpdir(), 'ci-snapshot-'));
  const git = (...args) => execFileSync('git', ['-C', dir, ...args], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
  try {
    git('init'); git('config', 'user.name', 'CI Fixture'); git('config', 'user.email', 'fixture@example.invalid');
    mkdirSync(join(dir, 'backend')); writeFileSync(join(dir, 'backend', 'a spaced.cs'), 'one'); git('add', '.'); git('commit', '-m', 'first');
    const sha = git('rev-parse', 'HEAD'), first = snapshot(dir, sha);
    writeFileSync(join(dir, 'backend', 'a spaced.cs'), 'uncommitted'); assert.deepEqual(snapshot(dir, sha), first);
    git('add', '.'); git('commit', '-m', 'second'); const second = snapshot(dir, git('rev-parse', 'HEAD'));
    assert.deepEqual(changedPaths(first, second), ['backend/a spaced.cs']);
    assert.throws(() => snapshot(dir, 'HEAD')); assert.throws(() => snapshot(dir, '--help'));
  } finally { rmSync(dir, { recursive: true, force: true }); }
});

test('duplicate comparison base cannot hide changed inputs', () => {
  const a = state(), b = state(); a.entries.push(a.entries[0]); assert.throws(() => changedPaths(a, b));
});
test('malformed comparison base cannot imply no changes', () => {
  const a = state(), b = state(); a.entries[0].path = '../hidden'; assert.throws(() => changedPaths(a, b));
});
test('nonempty commit binding required on both comparison endpoints', () => {
  const a = state(), b = state(); a.commit = 'HEAD'; assert.throws(() => changedPaths(a, b));
});
