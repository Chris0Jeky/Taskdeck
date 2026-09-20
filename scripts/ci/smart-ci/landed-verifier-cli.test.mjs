import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { makeEvidence, policyText, repository, sha } from './test-support/landed-evidence.mjs';

const verifierPath = fileURLToPath(new URL('./landed-verifier.mjs', import.meta.url));

function fixture(t, evidence = makeEvidence()) {
  const root = mkdtempSync(join(tmpdir(), 'taskdeck-landed-cli-'));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  const paths = {
    root, input: join(root, 'evidence.json'), policy: join(root, 'policy.json'),
    out: join(root, 'verdict.json'), output: join(root, 'github-output.txt'),
  };
  writeFileSync(paths.input, JSON.stringify([evidence]));
  writeFileSync(paths.policy, policyText);
  return paths;
}

function invoke(paths, overrides = {}, options = {}) {
  const flags = {
    '--repo': repository, '--head-sha': sha('9'), '--head-tree-sha': sha('4'),
    '--policy': paths.policy, '--input': paths.input,
    '--landing-kind': 'pull-request', '--landing-pr': '2327',
    '--out': paths.out, '--github-output': paths.output, ...overrides,
  };
  const env = { ...process.env };
  for (const key of ['GITHUB_REPOSITORY', 'GITHUB_SHA', 'GITHUB_OUTPUT', 'GITHUB_STEP_SUMMARY']) delete env[key];
  const args = Object.entries(flags).filter(([, value]) => value !== null).flat();
  const result = spawnSync(process.execPath, [options.script ?? verifierPath, ...args, ...(options.extraArgs ?? [])], {
    cwd: paths.root, encoding: 'utf8', timeout: 15_000, env: { ...env, ...options.env },
  });
  assert.equal(result.error, undefined);
  return result;
}

function outputs(paths) {
  assert.ok(existsSync(paths.output), 'the CLI must explicitly deny bounded execution before fallible work');
  return Object.fromEntries(readFileSync(paths.output, 'utf8').trim().split('\n').map((line) => line.split('=')));
}

function assertFull(paths) {
  const result = outputs(paths);
  assert.equal(result.qualification, 'full');
  assert.equal(result.receipt_artifact_id, '');
  assert.equal(result.receipt_workflow_run_id, '');
  if (existsSync(paths.out)) {
    const verdict = JSON.parse(readFileSync(paths.out, 'utf8'));
    assert.equal(verdict.schemaVersion, 1);
    assert.equal(verdict.kind, 'smart-ci-landed-verdict');
    assert.equal(verdict.qualification, 'full');
    assert.equal(verdict.receipt, null);
  }
  assert.equal(result.bounded, 'false');
}

test('CLI bounded execution is an explicit opt-in after successful output generation', (t) => {
  const paths = fixture(t);
  assert.equal(invoke(paths).status, 0);
  assert.equal(outputs(paths).bounded, 'true');
  assert.equal(outputs(paths).qualification, 'bounded');
  const verdict = JSON.parse(readFileSync(paths.out, 'utf8'));
  assert.equal(verdict.kind, 'smart-ci-landed-verdict');
  assert.equal(verdict.schemaVersion, 1);
});

test('CLI creates a nested summary directory before authorizing bounded work', (t) => {
  const paths = fixture(t);
  const summary = join(paths.root, 'new', 'nested', 'summary.md');
  assert.equal(invoke(paths, { '--summary': summary }).status, 0);
  assert.equal(outputs(paths).bounded, 'true');
  assert.match(readFileSync(summary, 'utf8'), /Qualification: \*\*bounded\*\*/);
});

for (const shape of ['missing', 'malformed-json', 'not-an-array', 'directory']) {
  test(`CLI requires full qualification for ${shape} evidence`, (t) => {
    const paths = fixture(t);
    if (shape === 'missing') rmSync(paths.input);
    if (shape === 'malformed-json') writeFileSync(paths.input, 'not-json PRIVATE_SENTINEL');
    if (shape === 'not-an-array') writeFileSync(paths.input, '{}');
    if (shape === 'directory') { rmSync(paths.input); mkdirSync(paths.input); }
    const result = invoke(paths);
    assert.equal(result.status, 0);
    assertFull(paths);
    assert.doesNotMatch(result.stdout + result.stderr, /PRIVATE_SENTINEL/);
  });
}

for (const shape of ['missing', 'directory']) {
  test(`CLI requires full qualification for ${shape} policy`, (t) => {
    const paths = fixture(t);
    rmSync(paths.policy);
    if (shape === 'directory') mkdirSync(paths.policy);
    assert.equal(invoke(paths).status, 0);
    assertFull(paths);
  });
}

for (const args of [['--unknown-PRIVATE_SENTINEL'], ['--landing-pr', 'not-a-number'], ['--head-sha']]) {
  test(`CLI argument failure leaves explicit full outputs (${args[0]})`, (t) => {
    const paths = fixture(t);
    const result = invoke(paths, {}, { extraArgs: args });
    assert.notEqual(result.status, 0);
    assertFull(paths);
    assert.doesNotMatch(result.stdout + result.stderr, /PRIVATE_SENTINEL/);
  });
}

test('CLI parse failure before the output option still emits a safe default', (t) => {
  const paths = fixture(t);
  const result = invoke(paths, {}, { script: verifierPath, extraArgs: ['--unknown'] });
  assert.notEqual(result.status, 0);
  assertFull(paths);
  rmSync(paths.output);
  const second = spawnSync(process.execPath, [verifierPath, '--unknown', '--github-output', paths.output], {
    cwd: paths.root, encoding: 'utf8', timeout: 15_000,
    env: { ...process.env, GITHUB_OUTPUT: '', GITHUB_REPOSITORY: '' },
  });
  assert.equal(second.error, undefined);
  assert.notEqual(second.status, 0);
  assertFull(paths);
});

for (const destination of ['--summary', '--out']) {
  test(`CLI ${destination} write failure revokes bounded outputs and persisted verdict`, (t) => {
    const paths = fixture(t);
    const blocked = join(paths.root, 'file-not-directory');
    writeFileSync(blocked, 'sentinel');
    const overrides = { [destination]: join(blocked, 'result') };
    const result = invoke(paths, overrides);
    assert.notEqual(result.status, 0);
    assertFull(paths);
    assert.equal(readFileSync(blocked, 'utf8'), 'sentinel');
    assert.doesNotMatch(readFileSync(paths.output, 'utf8'), /^bounded=true$/m);
  });
}

test('an unwritable GitHub output sink is an error, never a bounded JSON verdict', (t) => {
  const paths = fixture(t);
  mkdirSync(paths.output);
  const result = invoke(paths);
  assert.notEqual(result.status, 0);
  assert.ok(existsSync(paths.out));
  assert.equal(JSON.parse(readFileSync(paths.out, 'utf8')).qualification, 'full');
});

test('CLI does not silently assume Taskdeck repository identity outside Actions', (t) => {
  const paths = fixture(t);
  assert.equal(invoke(paths, { '--repo': null }).status, 0);
  assertFull(paths);
});

test('CLI obtains explicit repository and output bindings from Actions environment', (t) => {
  const paths = fixture(t);
  const result = invoke(paths, { '--repo': null, '--github-output': null }, {
    env: { GITHUB_REPOSITORY: repository, GITHUB_OUTPUT: paths.output },
  });
  assert.equal(result.status, 0);
  assert.equal(outputs(paths).bounded, 'true');
});

test('CLI help never grants bounded execution', (t) => {
  const paths = fixture(t);
  assert.equal(invoke(paths, {}, { extraArgs: ['--help'] }).status, 0);
  assertFull(paths);
});

test('CLI invocation through a directory symlink or Windows junction actually runs', (t) => {
  const paths = fixture(t);
  const alias = join(paths.root, 'alias');
  symlinkSync(dirname(verifierPath), alias, 'junction');
  const result = invoke(paths, {}, { script: join(alias, 'landed-verifier.mjs') });
  assert.equal(result.status, 0);
  assert.equal(outputs(paths).bounded, 'true');
});

test('importing the decision module has no CLI filesystem or process side effects', (t) => {
  const paths = fixture(t);
  const result = spawnSync(process.execPath, ['--input-type=module', '-e', `await import(${JSON.stringify(pathToFileURL(verifierPath).href)})`], {
    cwd: paths.root, encoding: 'utf8', timeout: 15_000, env: { ...process.env, GITHUB_OUTPUT: paths.output },
  });
  assert.equal(result.error, undefined);
  assert.equal(result.status, 0);
  assert.equal(result.stdout, '');
  assert.equal(existsSync(paths.output), false);
  assert.equal(existsSync(paths.out), false);
});

function git(root, ...args) {
  return execFileSync('git', ['-C', root, ...args], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
}

function twoCommitRepo(paths) {
  git(paths.root, 'init', '--initial-branch=main');
  git(paths.root, 'config', 'user.name', 'Taskdeck CI fixture');
  git(paths.root, 'config', 'user.email', 'fixture@example.invalid');
  writeFileSync(join(paths.root, 'tracked.txt'), 'first\n');
  git(paths.root, 'add', 'tracked.txt');
  git(paths.root, '-c', 'commit.gpgsign=false', 'commit', '-m', 'first');
  const first = { sha: git(paths.root, 'rev-parse', 'HEAD'), tree: git(paths.root, 'rev-parse', 'HEAD^{tree}') };
  writeFileSync(join(paths.root, 'tracked.txt'), 'second\n');
  git(paths.root, 'add', 'tracked.txt');
  git(paths.root, '-c', 'commit.gpgsign=false', 'commit', '-m', 'second');
  const second = { sha: git(paths.root, 'rev-parse', 'HEAD'), tree: git(paths.root, 'rev-parse', 'HEAD^{tree}') };
  return { first, second };
}

test('implicit tree resolution binds the requested commit, not the moving checkout HEAD', (t) => {
  const paths = fixture(t);
  const { first, second } = twoCommitRepo(paths);
  assert.notEqual(first.tree, second.tree);
  writeFileSync(paths.input, JSON.stringify([makeEvidence({ mergeTreeSha: first.tree })]));
  assert.equal(invoke(paths, { '--head-sha': first.sha, '--head-tree-sha': null }).status, 0);
  assert.equal(JSON.parse(readFileSync(paths.out, 'utf8')).headTreeSha, first.tree);
  assert.equal(outputs(paths).bounded, 'true');
});

test('a newer checkout receipt cannot qualify a different requested commit', (t) => {
  const paths = fixture(t);
  const { first, second } = twoCommitRepo(paths);
  writeFileSync(paths.input, JSON.stringify([makeEvidence({ mergeTreeSha: second.tree })]));
  assert.equal(invoke(paths, { '--head-sha': first.sha, '--head-tree-sha': null }).status, 0);
  assertFull(paths);
});

for (const requestedHead of ['HEAD', '--help', sha('f')]) {
  test(`invalid or absent requested commit never borrows HEAD's tree: ${requestedHead}`, (t) => {
    const paths = fixture(t);
    const { second } = twoCommitRepo(paths);
    writeFileSync(paths.input, JSON.stringify([makeEvidence({ mergeTreeSha: second.tree })]));
    invoke(paths, { '--head-sha': requestedHead, '--head-tree-sha': null });
    assertFull(paths);
    assert.equal(JSON.parse(readFileSync(paths.out, 'utf8')).headTreeSha, null);
  });
}

for (const objectKind of ['tree', 'tag', 'blob']) {
  test(`a requested ${objectKind} object is not a commit qualification identity`, (t) => {
    const paths = fixture(t);
    const { second } = twoCommitRepo(paths);
    let objectSha = second.tree;
    if (objectKind === 'tag') {
      git(paths.root, '-c', 'tag.gpgsign=false', 'tag', '-a', 'annotated', '-m', 'fixture', second.sha);
      objectSha = git(paths.root, 'rev-parse', 'refs/tags/annotated');
    } else if (objectKind === 'blob') {
      objectSha = git(paths.root, 'rev-parse', `${second.sha}:tracked.txt`);
    }
    assert.equal(git(paths.root, 'cat-file', '-t', objectSha), objectKind);
    writeFileSync(paths.input, JSON.stringify([makeEvidence({ mergeTreeSha: second.tree })]));
    const result = invoke(paths, { '--head-sha': objectSha, '--head-tree-sha': null });
    assert.equal(result.status, 0);
    assertFull(paths);
    assert.equal(JSON.parse(readFileSync(paths.out, 'utf8')).headTreeSha, null);
  });
}
