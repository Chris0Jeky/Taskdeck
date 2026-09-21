import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { inventoryWorkflowRunners, loadWorkflowSources, reviewedRunnerSurface } from './workflow-runner-inventory.mjs';

const path = (name) => `.github/workflows/${name}.yml`;
const file = (name, text) => ({ path: path(name), text });
const job = (name, body) => `  ${name}:\n${body}`;
const workflow = (name, body) => file(name, `on: [push, workflow_call]\njobs:\n${body}`);
const runner = (name, selector, extra = '') => workflow(name, job('build', `    runs-on: ${selector}\n${extra}    steps:\n      - run: echo ok\n`));
const caller = (name, target, extra = '') => workflow(name, job('call', `    uses: ./${path(target)}\n${extra}`));
const key = (name, id = 'build') => `${path(name)}#${id}`;
const inventory = (...files) => inventoryWorkflowRunners(files);

function assertIncomplete(files, code) {
  const result = inventoryWorkflowRunners(files);
  assert.equal(result.graphComplete, false);
  assert.ok(result.diagnostics.some((entry) => entry.code === code), JSON.stringify(result.diagnostics));
}

test('literal and opaque runner sites are discovered, never inferred from a step script', () => {
  const result = inventory(
    runner('linux', 'ubuntu-latest', '    env:\n      NOT_A_SELECTOR: windows-latest\n'),
    runner('windows', "'windows-latest' # a comment"),
    runner('matrix', '${{ matrix.os }}', '    strategy:\n      matrix:\n        os: [ubuntu-latest, windows-latest]\n'),
  );
  assert.equal(result.graphComplete, true);
  assert.equal(result.runners.length, 3);
  assert.deepEqual(result.runners.map((entry) => [entry.id, entry.classification]), [
    [key('linux'), 'linux-literal'], [key('matrix'), 'opaque'], [key('windows'), 'windows-literal'],
  ]);
  assert.equal(result.runners[1].strategy.includes('windows-latest'), true);
});

for (const [shape, selector] of [
  ['expression', '${{ inputs.runner }}'],
  ['flow labels', '[self-hosted, Windows, X64]'],
  ['block labels', '\n      - ubuntu-latest\n      - custom-label'],
  ['group', '\n      group: windows-runners\n      labels: X64'],
  ['alias', '*sharedRunner'],
  ['folded scalar', '>\n      windows-latest'],
  ['custom label', 'my-linux-looking-runner'],
  ['unreviewed platform', 'macos-latest'],
]) {
  test(`${shape} selector stays in the conservative candidate set`, () => {
    const result = inventory(runner('fixture', selector));
    assert.equal(result.graphComplete, true);
    const surface = reviewedRunnerSurface(result);
    assert.equal(surface.candidates.length, 1);
    assert.equal(surface.candidates[0].classification, 'opaque');
  });
}

test('conditions and matrix exclusions cannot erase a Windows candidate', () => {
  const result = inventory(runner('fixture', '${{ matrix.os }}',
    '    if: false\n    strategy:\n      matrix:\n        os: [ubuntu-latest, windows-latest]\n        exclude:\n          - os: windows-latest\n        include:\n          - os: windows-latest\n'));
  const [entry] = reviewedRunnerSurface(result).candidates;
  assert.equal(entry.condition, 'false');
  assert.match(entry.strategy, /exclude:/);
  assert.match(entry.strategy, /include:/);
});

test('transitive reusable callers preserve distinct job routes, conditions and inputs', () => {
  const result = inventory(
    runner('leaf', '${{ matrix.os }}'),
    caller('middle', 'leaf', '    if: false\n    with:\n      platform: linux\n'),
    workflow('root', job('first', `    uses: ./${path('middle')}\n`) + job('second', `    uses: ./${path('middle')}\n    needs: first\n`)),
  );
  assert.equal(result.graphComplete, true);
  const [entry] = reviewedRunnerSurface(result).candidates;
  assert.deepEqual(entry.callers.map((edge) => edge.id), [key('middle', 'call'), key('root', 'first'), key('root', 'second')]);
  assert.equal(entry.callers[0].condition, 'false');
  assert.match(entry.callers[0].inputs, /platform: linux/);
  assert.equal(entry.callers[2].needs, 'first');
  assert.equal(entry.callers[0].events, '[push, workflow_call]');
});

test('identical job IDs in different workflows remain separate', () => {
  const result = inventory(runner('a', 'windows-latest'), runner('b', 'windows-latest'));
  assert.deepEqual(result.runners.map((entry) => entry.id), [key('a'), key('b')]);
});

test('input order and LF versus CRLF do not change the reviewed surface', () => {
  const files = [runner('leaf', 'windows-latest'), caller('root', 'leaf')];
  const first = reviewedRunnerSurface(inventoryWorkflowRunners(files));
  const second = reviewedRunnerSurface(inventoryWorkflowRunners(files.reverse().map((entry) => ({ ...entry, text: entry.text.replace(/\n/g, '\r\n') }))));
  assert.deepEqual(first, second);
});

test('private step contents do not leak into the control projection', () => {
  const source = runner('fixture', 'windows-latest');
  source.text += '      - run: |\n          jobs:\n            hidden:\n              runs-on: PRIVATE_STEP_SENTINEL\n';
  const result = inventory(source);
  assert.equal(result.graphComplete, true);
  assert.equal(result.runners.length, 1);
  assert.doesNotMatch(JSON.stringify(result), /PRIVATE_STEP_SENTINEL/);
});

for (const [name, text, code] of [
  ['inline jobs', 'jobs: { build: { runs-on: windows-latest } }\n', 'unsupported-jobs-mapping'],
  ['aliased jobs', 'jobs: *shared\n', 'unsupported-jobs-mapping'],
  ['aliased job', 'jobs:\n  build: *shared\n', 'unsupported-job-mapping'],
  ['flow job', 'jobs:\n  build: { runs-on: windows-latest }\n', 'unsupported-job-mapping'],
  ['quoted jobs', '"jobs":\n  build:\n    runs-on: windows-latest\n', 'unsupported-mapping'],
  ['quoted job', 'jobs:\n  "build":\n    runs-on: windows-latest\n', 'unsupported-mapping'],
  ['quoted selector key', 'jobs:\n  build:\n    "runs-on": windows-latest\n', 'unsupported-mapping'],
  ['job merge', 'jobs:\n  build:\n    <<: *defaults\n    runs-on: ubuntu-latest\n', 'unsupported-mapping'],
  ['root merge', '<<: *defaults\njobs:\n  build:\n    runs-on: ubuntu-latest\n', 'unsupported-mapping'],
  ['duplicate root', 'jobs:\n  build:\n    runs-on: ubuntu-latest\njobs:\n  hidden:\n    runs-on: windows-latest\n', 'duplicate-key'],
  ['duplicate job', 'jobs:\n  build:\n    runs-on: ubuntu-latest\n  build:\n    runs-on: windows-latest\n', 'duplicate-key'],
  ['duplicate selector', 'jobs:\n  build:\n    runs-on: ubuntu-latest\n    runs-on: windows-latest\n', 'duplicate-key'],
  ['tab indentation', 'jobs:\n\tbuild:\n\t  runs-on: windows-latest\n', 'unsupported-indentation'],
  ['alternate indentation', 'jobs:\n    build:\n        runs-on: windows-latest\n', 'unsupported-indentation'],
  ['document boundary', '---\njobs:\n  build:\n    runs-on: ubuntu-latest\n', 'unsupported-mapping'],
  ['missing runner', 'jobs:\n  build:\n    steps: []\n', 'runner-or-call-required'],
  ['runner and call', `jobs:\n  build:\n    runs-on: ubuntu-latest\n    uses: ./${path('leaf')}\n`, 'ambiguous-job'],
]) {
  test(`${name} cannot silently become a complete graph`, () => assertIncomplete([file('fixture', text)], code));
}

test('empty or incomplete workflow sets never look qualified', () => {
  assertIncomplete([], 'empty-source');
  assertIncomplete([file('fixture', 'name: Empty\n')], 'jobs-required');
  assertIncomplete([file('fixture', 'jobs:\n')], 'jobs-required');
  assertIncomplete([caller('root', 'missing')], 'missing-workflow');
});

test('external reusable workflows are recorded and incomplete, not silently omitted', () => {
  const result = inventory(workflow('root', job('remote', '    uses: other/repo/.github/workflows/build.yml@abc\n')));
  assert.equal(result.graphComplete, false);
  assert.equal(result.calls.length, 1);
  assert.equal(result.diagnostics[0].code, 'unresolved-workflow');
});

test('a step-level action is not a reusable workflow call', () => {
  const source = runner('linux', 'ubuntu-latest');
  source.text += '      - uses: owner/repo/.github/workflows/not-a-job.yml@abc\n';
  assert.equal(inventory(source).calls.length, 0);
});

test('cyclic local workflows cannot produce a complete graph', () => {
  assertIncomplete([caller('a', 'b'), caller('b', 'a')], 'workflow-cycle');
  assertIncomplete([caller('self', 'self')], 'workflow-cycle');
});

test('duplicate and non-canonical source paths fail closed', () => {
  assertIncomplete([runner('a', 'ubuntu-latest'), runner('a', 'windows-latest')], 'duplicate-file');
  assertIncomplete([{ path: '../hidden.yml', text: runner('a', 'windows-latest').text }], 'invalid-source');
});

test('source limits fail closed before discovery', () => {
  assertIncomplete(Array.from({ length: 257 }, (_, index) => runner(`file-${index}`, 'ubuntu-latest')), 'source-limit');
  assertIncomplete([file('large', ' '.repeat(2 * 1024 * 1024 + 1))], 'source-limit');
});

test('large repeated trigger projections fail closed before report expansion', () => {
  const jobs = Array.from({ length: 512 }, (_, index) =>
    `  job-${index}:\n    runs-on: windows-latest\n`,
  ).join('');
  const result = inventoryWorkflowRunners([
    file('large-events', `on:\n  description: ${'x'.repeat(20_000)}\njobs:\n${jobs}`),
  ]);
  assert.equal(result.graphComplete, false);
  assert.equal(result.runners.length, 0);
  assert.ok(result.diagnostics.some((entry) => entry.code === 'projection-limit'));
});

test('large caller projections fail closed before reviewed-surface expansion', () => {
  const jobs = Array.from({ length: 400 }, (_, index) =>
    `  job-${index}:\n    runs-on: windows-latest\n`,
  ).join('');
  const result = inventoryWorkflowRunners([
    file('leaf', `on: [push, workflow_call]\njobs:\n${jobs}`),
    caller('root', 'leaf', `    with:\n      description: ${'x'.repeat(20_000)}\n`),
  ]);
  assert.equal(result.graphComplete, true);
  const surface = reviewedRunnerSurface(result);
  assert.equal(surface.graphComplete, false);
  assert.deepEqual(surface.candidates, []);
});

test('large caller-free projections fail closed before reviewed-surface expansion', () => {
  const inventory = {
    graphComplete: true,
    runners: Array.from({ length: 1100 }, (_, index) => ({
      id: `.github/workflows/large-${index}.yml#build`,
      file: `.github/workflows/large-${index}.yml`,
      job: 'build',
      line: 1,
      events: 'x'.repeat(4096),
      condition: null,
      needs: null,
      strategy: null,
      selector: 'windows-latest',
      classification: 'windows-literal',
    })),
    calls: [],
  };

  const surface = reviewedRunnerSurface(inventory);
  assert.equal(surface.graphComplete, false);
  assert.deepEqual(surface.candidates, []);
});

test('caps parser diagnostics before appending per-line errors', () => {
  const malformed = Array.from({ length: 10_000 }, (_, index) => `? invalid-${index}`).join('\n');
  const result = inventory(file('diagnostic-flood', malformed));

  assert.equal(result.graphComplete, false);
  assert.ok(result.diagnostics.some((entry) => entry.code === 'diagnostic-limit'));
  assert.ok(result.diagnostics.length <= 4097);
});

test('the reviewed runner surface detects a new Windows job and a new caller', () => {
  const leaf = runner('leaf', 'windows-latest');
  const baseline = reviewedRunnerSurface(inventory(leaf));
  assert.notDeepEqual(reviewedRunnerSurface(inventory(leaf, runner('new', 'windows-latest'))), baseline);
  assert.notDeepEqual(reviewedRunnerSurface(inventory(leaf, caller('new', 'leaf'))), baseline);
  const changed = { ...leaf, text: leaf.text.replace('windows-latest', '${{ vars.runner }}') };
  assert.notDeepEqual(reviewedRunnerSurface(inventory(changed)), baseline);
});

test('the current repository Windows/opaque runner sites and routes match the reviewed inventory', () => {
  const files = loadWorkflowSources(fileURLToPath(new URL('../../../.github/workflows/', import.meta.url)));
  const result = inventoryWorkflowRunners(files);
  assert.equal(result.graphComplete, true, JSON.stringify(result.diagnostics));
  const surface = reviewedRunnerSurface(result);
  assert.equal(surface.candidates.length, 5, 'expected five baseline Windows or opaque runner sites');
  const expected = JSON.parse(readFileSync(new URL('./test-support/workflow-runner-inventory.snapshot.json', import.meta.url), 'utf8'));
  assert.deepEqual(surface, expected, 'runner topology changed: review the new site/route and update the inventory deliberately');
});

test('CLI reports incomplete input with a nonzero exit and no qualification flag', (t) => {
  const root = mkdtempSync(join(tmpdir(), 'td-workflow-inventory-'));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  writeFileSync(join(root, 'bad.yml'), 'jobs: *unknown\n');
  const script = fileURLToPath(new URL('./workflow-runner-inventory.mjs', import.meta.url));
  const result = spawnSync(process.execPath, [script, '--workflows', root], { encoding: 'utf8', timeout: 10_000 });
  assert.equal(result.error, undefined);
  assert.equal(result.status, 2);
  const report = JSON.parse(result.stdout);
  assert.equal(report.graphComplete, false);
  assert.equal(Object.hasOwn(report, 'qualification'), false);
});


test('block control values preserve internal blank and hash-prefixed scalar lines', () => {
  const extra = '    with:\n      description: |\n        before\n\n        # literal value, not a YAML comment\n        after\n';
  const result = inventory(caller('root', 'leaf', extra), runner('leaf', 'windows-latest'));
  const [entry] = reviewedRunnerSurface(result).candidates;
  assert.match(entry.callers[0].inputs, /before\n\n  # literal value, not a YAML comment\n  after/);
});

test('keep-chomp block scalars preserve trailing blank lines', () => {
  const extra = [
    '    with:',
    '      description: |+',
    '        before',
    '',
    '',
  ].join('\n');
  const result = inventory(caller('root', 'leaf', extra), runner('leaf', 'windows-latest'));
  const [entry] = reviewedRunnerSurface(result).candidates;
  assert.match(entry.callers[0].inputs, /description: \|\+\n  before\n\n$/);
});

test('keep-chomp indicators after YAML properties preserve trailing blank lines', () => {
  for (const value of ['&saved |+', '!!str |+', '&saved !!str |+']) {
    const extra = [
      '    with:',
      `      description: ${value}`,
      '        before',
      '',
      '',
    ].join('\n');
    const result = inventory(caller('root', 'leaf', extra), runner('leaf', 'windows-latest'));
    const [entry] = reviewedRunnerSurface(result).candidates;
    assert.match(entry.callers[0].inputs, new RegExp(`description: ${value.replace(/[|+]/g, '\\$&')}\\n  before\\n\\n$`));
  }
});

test('input alias and folded reusable references stay explicitly unresolved', () => {
  for (const value of ['*reference', '>\n      ./.github/workflows/leaf.yml']) {
    assertIncomplete([workflow('root', job('call', `    uses: ${value}\n`)), runner('leaf', 'windows-latest')], 'unresolved-workflow');
  }
});

test('unknown source value types fail closed', () => {
  assert.equal(inventoryWorkflowRunners(null).graphComplete, false);
  assertIncomplete([null], 'invalid-source');
  assertIncomplete([{ path: path('file'), text: {} }], 'invalid-source');
});

test('source loader includes both YAML extensions and refuses directory or symlink entries', (t) => {
  const root = mkdtempSync(join(tmpdir(), 'td-inventory-loader-'));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  writeFileSync(join(root, 'a.yml'), runner('a', 'ubuntu-latest').text);
  writeFileSync(join(root, 'b.yaml'), runner('b', 'windows-latest').text);
  writeFileSync(join(root, 'not-a-workflow.txt'), 'not workflow source');
  assert.deepEqual(loadWorkflowSources(root).map((entry) => entry.path), [path('a'), '.github/workflows/b.yaml']);
  mkdirSync(join(root, 'directory.yml'));
  assert.throws(() => loadWorkflowSources(root), /Unsupported workflow source/);
  rmSync(join(root, 'directory.yml'), { recursive: true });
  mkdirSync(join(root, 'target'));
  symlinkSync(join(root, 'target'), join(root, 'linked.yml'), 'junction');
  assert.throws(() => loadWorkflowSources(root), /Unsupported workflow source/);
});

test('a differently-cased YAML extension is not silently omitted', (t) => {
  const root = mkdtempSync(join(tmpdir(), 'td-inventory-extension-'));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  writeFileSync(join(root, 'upper.YML'), runner('upper', 'windows-latest').text);
  assertIncomplete(loadWorkflowSources(root), 'invalid-source');
});

test('importing the inventory has no filesystem or CLI side effects', () => {
  const url = new URL('./workflow-runner-inventory.mjs', import.meta.url).href;
  const result = spawnSync(process.execPath, ['--input-type=module', '-e', `await import(${JSON.stringify(url)})`], { encoding: 'utf8', timeout: 10_000 });
  assert.equal(result.error, undefined);
  assert.equal(result.status, 0);
  assert.equal(result.stdout, '');
  assert.equal(result.stderr, '');
});

test('a comment after a plain Linux selector does not create an opaque runner', () => {
  const result = inventory(runner('fixture', 'ubuntu-latest', '    # Docker is installed on this runner\n'));
  assert.equal(result.graphComplete, true);
  assert.equal(result.runners[0].classification, 'linux-literal');
});

test('an attached hash stays part of the runner scalar', () => {
  const result = inventory(runner('fixture', 'ubuntu-latest#custom'));
  assert.equal(result.graphComplete, true);
  assert.equal(result.runners[0].selector, 'ubuntu-latest#custom');
  assert.equal(result.runners[0].classification, 'opaque');
});
