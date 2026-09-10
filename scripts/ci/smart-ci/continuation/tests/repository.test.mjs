import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { execFileSync } from 'node:child_process';
import { CONTROL_FLOOR, validateManifest, starterManifest, adviseRepository, inspectRepository, readJsonBlob } from '../adapters/repository.mjs';
import { options, runCli } from '../cli.mjs';
import { graph, state, oid } from './fixtures.mjs';
function manifest() {
  return { format: 'ci.repository-adapter.v1', repositoryId: '1234', description: 'Fictional polyglot fixture', contracts: graph(),
    policy: { alwaysTasks: ['docs'], controlPaths: [...CONTROL_FLOOR], rules: [
      { patterns: ['backend/**'], tasks: ['backend'] }, { patterns: ['frontend/**'], tasks: ['frontend'] },
      { patterns: ['shared/**'], tasks: [] }, { patterns: ['docs/**'], tasks: ['docs'] }, { patterns: ['lock.json'], tasks: ['backend', 'frontend', 'e2e'] }
    ] } };
}
function request() { return { manifest: manifest(), baseState: state(), candidateState: state() }; }
for (const kind of ['node', 'dotnet', 'python']) test(`${kind} starter is conservative and unreviewed`, () => {
  const m = starterManifest({ repositoryId: '1234', kind, root: 'apps/product' });
  assert.equal(validateManifest(m), m); assert.equal(m.contracts.tasks.tests.reviewed, false);
  assert.ok(m.contracts.tasks.tests.command.includes('apps/product')); assert.ok(CONTROL_FLOOR.every(p => m.policy.controlPaths.includes(p)));
});
for (const [name, mutate] of Object.entries({ wrongVersion: m => m.format = 'v2', wrongRepository: m => m.repositoryId = 'owner/name',
  missingFloor: m => m.policy.controlPaths.pop(), unknownField: m => m.eval = 'untrusted()',
  unknownTask: m => m.policy.rules[0].tasks.push('other'), duplicateTask: m => m.policy.rules[0].tasks.push('backend'),
  unsupportedPattern: m => m.policy.rules[0].patterns = ['!secret/**'], duplicateAlways: m => m.policy.alwaysTasks.push('docs'),
  cyclicInputs: m => m.contracts.components.shared.deps = ['backend'] })) {
  test(`manifest rejects ${name}`, () => { const m = manifest(); mutate(m); assert.throws(() => validateManifest(m)); });
}
test('related-test floor and transitive integration checks are additive', () => {
  const r = request(); r.candidateState.entries[0].oid = oid('b'); const a = adviseRepository(r);
  assert.equal(a.authority, 'none'); assert.ok(a.plan.tasks.every(t => t.action === 'run'));
  assert.equal(a.plan.tasks.find(t => t.taskId === 'e2e').proposed, 'run');
  assert.equal(a.plan.tasks.find(t => t.taskId === 'frontend').proposed, 'unaffected');
});
test('external canonical floor cannot be reduced', () => {
  const r = request(); r.canonicalSelected = ['frontend']; const a = adviseRepository(r);
  assert.equal(a.plan.tasks.find(t => t.taskId === 'frontend').proposed, 'run');
});
for (const [name, change] of Object.entries({ control: r => r.candidateState.entries[3].oid = oid('b'),
  unknown: r => r.candidateState.entries.push({ ...r.candidateState.entries[0], path: 'unknown/new' }),
  release: r => r.event = 'release', force: r => r.forceFull = true })) {
  test(`${name} escalates rather than guessing`, () => {
    const r = request(); change(r); const a = adviseRepository(r);
    assert.ok(a.plan.fullQualification); assert.ok(a.plan.tasks.every(t => t.proposed === 'run'));
  });
}
for (const root of ['../x', '/x', 'a/**', 'a\\b']) test(`unsafe starter root ${root}`, () => assert.throws(() => starterManifest({ repositoryId: '1234', kind: 'node', root })));
test('unsupported kind fails', () => assert.throws(() => starterManifest({ repositoryId: '1234', kind: 'auto' })));
for (const args of [['--a'], ['--a', '1', '--a', '2'], ['--unknown', 'x'], ['--a', '--b']]) {
  test(`strict CLI options ${args}`, () => assert.throws(() => options(args, ['--a'])));
}
test('CLI has no activation path', () => assert.throws(() => runCli(['enforce'])));
test('CLI writes a new starter, validates it and refuses overwrite', () => {
  const dir = mkdtempSync(join(tmpdir(), 'ci-adapter-cli-')), path = join(dir, 'config.json');
  try {
    const args = ['init', '--kind', 'node', '--repository-id', '1234', '--out', path];
    runCli(args); assert.equal(runCli(['validate', '--manifest', path]).valid, true);
    const before = readFileSync(path, 'utf8'); assert.throws(() => runCli(args)); assert.equal(readFileSync(path, 'utf8'), before);
    writeFileSync(path, ' '.repeat(1024 * 1024 + 1)); assert.throws(() => runCli(['validate', '--manifest', path]), /budget/);
  } finally { rmSync(dir, { recursive: true, force: true }); }
});
test('immutable base policy cannot be weakened by candidate or dirty working tree', () => {
  const dir = mkdtempSync(join(tmpdir(), 'ci-adapter-git-'));
  const git = (...args) => execFileSync('git', ['-C', dir, ...args], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
  try {
    git('init'); git('config', 'user.name', 'CI Fixture'); git('config', 'user.email', 'fixture@example.invalid');
    mkdirSync(join(dir, '.ci')); mkdirSync(join(dir, 'backend'));
    writeFileSync(join(dir, '.ci', 'continuation.json'), JSON.stringify(manifest())); writeFileSync(join(dir, 'backend', 'a.cs'), 'first');
    git('add', '.'); git('commit', '-m', 'trusted policy'); const base = git('rev-parse', 'HEAD');
    writeFileSync(join(dir, '.ci', 'continuation.json'), '{"format":"malicious"}');
    git('add', '.'); git('commit', '-m', 'candidate policy'); const candidate = git('rev-parse', 'HEAD');
    writeFileSync(join(dir, '.ci', 'continuation.json'), 'not JSON');
    const a = inspectRepository({ repo: dir, base, candidate, repositoryId: '1234' });
    assert.equal(a.configSource.commit, base); assert.ok(a.escalationReasons.includes('control-path'));
    assert.ok(a.plan.tasks.every(t => t.action === 'run')); assert.equal(a.authority, 'none');
    assert.throws(() => inspectRepository({ repo: dir, base, candidate, repositoryId: '9999' }), /binding/);
    assert.throws(() => readJsonBlob(dir, base, '.ci/continuation.json', 10));
    assert.throws(() => readJsonBlob(dir, 'HEAD', '.ci/continuation.json'));
    assert.throws(() => inspectRepository({ repo: dir, base, candidate, repositoryId: '1234', configPath: 'backend/config.json' }), /protected/);
    const out = join(dir, 'advisory.json'); runCli(['plan', '--repo', dir, '--base', base, '--candidate', candidate, '--repository-id', '1234', '--out', out]);
    assert.equal(JSON.parse(readFileSync(out)).configSource.commit, base);
  } finally { rmSync(dir, { recursive: true, force: true }); }
});
