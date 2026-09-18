import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';
import { taskdeckContracts, adviseTaskdeck, canonicalPolicyDigest } from '../adapters/taskdeck.mjs';
import { inputPatterns } from '../core/contracts.mjs';
import { matches } from '../core/primitives.mjs';
import { stageWorkflow } from '../tools/stage-taskdeck.mjs';
import { state, oid, entry } from './fixtures.mjs';

// Synthetic fixtures modeled on the reviewed topology, NOT copies of a live product test result.
const LANES = ['smart-ci-plan', 'smart-ci-self-test', 'docs-governance', 'worktree-helper-windows',
  'release-workflow-contract', 'backend-architecture', 'backend-unit-linux', 'backend-unit-windows',
  'api-integration-linux', 'api-integration-windows', 'migration-validation', 'frontend-unit-linux',
  'frontend-unit-windows', 'paper-color-audit', 'container-images', 'secret-scan', 'dependency-security', 'sast-scan', 'e2e-smoke'];
function policy() {
  return { schemaVersion: 1, policyId: 'taskdeck.smart-ci.v1', mode: 'shadow', lanes: Object.fromEntries(LANES.map(id => [id, {
    checkName: `Fixture / ${id}`, family: ['secret-scan', 'dependency-security', 'sast-scan'].includes(id) ? 'security' : 'product'
  }])) };
}
function advisory() {
  const p = policy(), policyText = JSON.stringify(p), baseState = state(), candidateState = state();
  candidateState.commit = oid('3'); candidateState.tree = oid('4'); candidateState.entries[0].oid = oid('b');
  baseState.entries.push(entry('backend/Taskdeck.sln')); candidateState.entries.push(entry('backend/Taskdeck.sln'));
  return { policyText, baseState, candidateState, plan: { schemaVersion: 1, policyId: p.policyId, policyDigest: canonicalPolicyDigest(policyText),
    baseSha: baseState.commit, mergeBaseSha: baseState.commit, mergeSha: candidateState.commit, mergeTreeSha: candidateState.tree,
    selected: [{ lane: 'secret-scan' }], skipped: LANES.filter(x => x !== 'secret-scan').map(lane => ({ lane })), trust: 'T1', risk: 'R2', escalated: false } };
}
test('adapter derives all 19 canonical lanes without defining a second selection policy', () => assert.equal(Object.keys(taskdeckContracts(policy()).tasks).length, 19));
test('Linux frontend depends on backend launcher inputs; Windows frontend does not', () => {
  const g = taskdeckContracts(policy()); assert.ok(matches('backend/src/Taskdeck.Api/Program.cs', inputPatterns(g, 'frontend-unit-linux')));
  assert.equal(matches('backend/src/Taskdeck.Api/Program.cs', inputPatterns(g, 'frontend-unit-windows')), false);
});
test('security is always fresh', () => assert.equal(taskdeckContracts(policy()).tasks['secret-scan'].reuse, 'never'));
test('every shipped Taskdeck contract is explicitly unreviewed', () => assert.ok(Object.values(taskdeckContracts(policy()).tasks).every(t => t.reviewed === false)));
test('canonical digest normalizes CRLF, not arbitrary whitespace', () => {
  assert.equal(canonicalPolicyDigest('a\r\nb'), canonicalPolicyDigest('a\nb')); assert.notEqual(canonicalPolicyDigest('a b'), canonicalPolicyDigest('a  b'));
});
test('advisory is additive, source-bound and cannot authorize reuse', () => {
  const a = adviseTaskdeck(advisory()); assert.ok(a.advisorySelected.includes('secret-scan'));
  assert.ok(a.addedOnly.includes('frontend-unit-linux')); assert.ok(a.candidates.every(c => !c.reusable));
  assert.equal(a.authority.startsWith('none'), true);
});
for (const [field, value] of [['mergeSha', oid('9')], ['mergeTreeSha', oid('9')], ['mergeBaseSha', oid('9')], ['policyDigest', 'bad'], ['schemaVersion', 2]]) {
  test(`advisory rejects mismatched ${field}`, () => { const a = advisory(); a.plan[field] = value; assert.throws(() => adviseTaskdeck(a)); });
}
test('advisory rejects duplicate/incomplete lane universe', () => { const a = advisory(); a.plan.skipped[0].lane = 'secret-scan'; assert.throws(() => adviseTaskdeck(a)); });
for (const [field, value] of [['risk', 'R4'], ['escalated', true], ['trust', 'T3'], ['plannerError', { message: 'bad' }]]) {
  test(`${field} requires advisory full plan`, () => { const a = advisory(); a.plan[field] = value; assert.equal(adviseTaskdeck(a).advisorySelected.length, LANES.length); });
}

const JOBS = ['docs-governance', 'release-workflow-contract', 'backend-architecture', 'backend-unit',
  'api-integration', 'migration-validation', 'frontend-unit', 'paper-color-audit', 'container-images',
  'secret-scan', 'dependency-security', 'sast-scan', 'e2e-smoke'];
function workflow() {
  return `name: CI\non:\n  pull_request:\n  push:\n    branches: [main]\npermissions:\n  contents: read\njobs:\n` + JOBS.map(id => {
    let needs = id === 'e2e-smoke' ? '    needs:\n      - docs-governance\n      - backend-architecture\n      - backend-unit\n      - api-integration\n      - migration-validation\n' : '';
    let guard = id === 'secret-scan' ? "    if: ${{ github.event_name == 'pull_request' }}\n" : '';
    return `  ${id}:\n    name: ${id}\n${needs}${guard}    uses: ./.github/workflows/reusable-${id}.yml\n`;
  }).join('');
}
test('staging adds frontend barrier to E2E and retains all original dependencies', () => {
  const a = stageWorkflow(workflow()); assert.ok(a.changes.find(c => c.job === 'e2e-smoke').added.includes('frontend-unit'));
  assert.ok(a.text.includes('      - migration-validation')); assert.equal((a.text.split('\njobs:\n')[1].match(/^  [a-z0-9-]+:\n/gm) ?? []).length, JOBS.length);
});
test('minimal and compute modes differ only in API waiting for full backend matrix', () => {
  assert.equal(stageWorkflow(workflow()).changes.find(c => c.job === 'api-integration').added.includes('backend-unit'), false);
  assert.equal(stageWorkflow(workflow(), 'compute').changes.find(c => c.job === 'api-integration').added.includes('backend-unit'), true);
});
test('staging is idempotent', () => { const a = stageWorkflow(workflow()); assert.equal(stageWorkflow(a.text).text, a.text); assert.deepEqual(stageWorkflow(a.text).changes, []); });
test('staging never creates an unconditional dependency on PR-only secret scan', () => assert.equal(stageWorkflow(workflow()).text.includes('      - secret-scan'), false));
test('existing step changes survive the dependency transform', () => {
  const w = workflow().replace('    name: backend-unit\n', '    name: backend-unit\n    # concurrent agent credential fix stays untouched\n');
  assert.ok(stageWorkflow(w).text.includes('    # concurrent agent credential fix stays untouched'));
});
test('all non-needs lines preserved byte-for-byte', () => {
  const strip = s => s.replace(/^    needs:\n(?:      - [a-z0-9-]+\n)+/gm, '');
  assert.equal(strip(stageWorkflow(workflow()).text), strip(workflow()));
});
for (const [name, mutate] of Object.entries({ crlf: w => w.replaceAll('\n', '\r\n'), missingJob: w => w.replace('  backend-unit:', '  renamed-backend:'),
  newJob: w => w + '  new-job:\n    runs-on: ubuntu-latest\n', flowNeeds: w => w.replace('    needs:\n', '    needs: []\n'),
  anchors: w => w.replace('  backend-unit:\n', '  backend-unit: &backend\n'), cycle: w => w.replace('  backend-architecture:\n', '  backend-architecture:\n    needs:\n      - backend-unit\n'),
  secretBarrier: w => w.replace('      - docs-governance\n', '      - secret-scan\n') })) {
  test(`staging rejects ${name} rather than overwriting unknown workflow shape`, () => assert.throws(() => stageWorkflow(mutate(workflow()))));
}

test('staging rejects duplicated dependency instead of silently normalizing it', () => assert.throws(() => stageWorkflow(workflow().replace('      - docs-governance\n', '      - docs-governance\n      - docs-governance\n'))));

test('staging CLI produces a separate file and refuses overwrite of source or output', () => {
  const dir = mkdtempSync(join(tmpdir(), 'ci-staging-'));
  try {
    mkdirSync(join(dir, '.github', 'workflows'), { recursive: true });
    const source = join(dir, '.github', 'workflows', 'ci-required.yml'), out = join(dir, 'proposed.yml');
    writeFileSync(source, workflow());
    const cli = fileURLToPath(new URL('../tools/stage-taskdeck.mjs', import.meta.url));
    const run = output => execFileSync(process.execPath, [cli, '--repo', dir, '--out', output], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });
    const result = JSON.parse(run(out)); assert.ok(result.changes.length > 0);
    assert.equal(readFileSync(source, 'utf8'), workflow()); assert.equal(readFileSync(out, 'utf8'), stageWorkflow(workflow()).text);
    assert.throws(() => run(out)); assert.throws(() => run(source));
    assert.equal(readFileSync(source, 'utf8'), workflow());
  } finally { rmSync(dir, { recursive: true, force: true }); }
});
