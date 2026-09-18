import test from 'node:test';
import assert from 'node:assert/strict';
import { generateKeyPairSync } from 'node:crypto';
import { sample, oid } from '../examples/sample-model.mjs';
import { planContinuation } from '../core/planner.mjs';
import { fingerprint } from '../core/contracts.mjs';
import { signRecord } from '../core/evidence.mjs';
const keys = generateKeyPairSync('ed25519');
function scenario() {
  const i = sample(), fp = fingerprint({ graph: i.graph, taskId: 'backend', state: i.baseState,
    environment: i.environments.backend, context: i.context, policyDigest: i.policyDigest, repositoryId: i.repositoryId });
  i.evidence = { backend: [signRecord({ version: 1, issuer: 'test', keyId: 'test', repositoryId: i.repositoryId, taskId: 'backend',
    inputKey: fp.key, policyDigest: fp.policyDigest, graphDigest: fp.graphDigest, platform: fp.platform,
    commit: i.baseState.commit, tree: i.baseState.tree, workflowId: '1', runId: '2', jobId: '3', attempt: 1,
    conclusion: 'success', executedTests: 5, completedAt: 1000, expiresAt: 4600 }, keys.privateKey)] };
  i.verification = { now: 1100, trustedKeys: { test: keys.publicKey }, issuer: 'test', ledgerComplete: true, revokedInputKeys: [], disabledTasks: [] };
  i.candidateState.commit = oid('3'); i.candidateState.tree = oid('4'); i.candidateState.entries[1].oid = oid('b');
  return i;
}
test('observation runs everything but records potential reuse', () => {
  const p = planContinuation(scenario()); assert.ok(p.tasks.every(t => t.action === 'run')); assert.equal(p.tasks.find(t => t.taskId === 'backend').proposed, 'reuse');
});
test('qualified generic enforcement reuses unchanged backend, not changed frontend or E2E', () => {
  const p = planContinuation({ ...scenario(), mode: 'enforce' });
  assert.equal(p.tasks.find(t => t.taskId === 'backend').action, 'reuse'); assert.equal(p.tasks.find(t => t.taskId === 'frontend').action, 'run');
  assert.equal(p.tasks.find(t => t.taskId === 'e2e').action, 'run');
});
test('full qualification ignores all reuse', () => assert.ok(planContinuation({ ...scenario(), mode: 'enforce', fullQualification: true }).tasks.every(t => t.action === 'run')));
test('lack of qualification forces fresh backend', () => { const i = scenario(); i.qualifiedTasks = []; assert.equal(planContinuation({ ...i, mode: 'enforce' }).tasks.find(t => t.taskId === 'backend').action, 'run'); });
test('unknown path requires all fresh work', () => { const i = scenario(); i.candidateState.entries.push({ ...i.candidateState.entries[0], path: 'new/thing' }); assert.ok(planContinuation({ ...i, mode: 'enforce' }).tasks.every(t => t.action === 'run')); });
test('read-only decision bound to current exact candidate', () => { const i = scenario(), p = planContinuation(i); assert.equal(p.binding.commit, i.candidateState.commit); assert.equal(p.binding.tree, i.candidateState.tree); });
for (const mutate of [i => i.baseState.complete = false, i => i.candidateState.complete = false, i => i.graph.tasks.backend.command = [],
  i => i.canonicalSelected.push('wrong'), i => delete i.graph.tasks.docs, i => i.evidence.backend = {}, i => i.policyDigest = 'wrong']) {
  test(`fallback runs trusted full universe: ${String(mutate)}`, () => { const i = scenario(); mutate(i); const p = planContinuation({ ...i, mode: 'enforce' }); assert.ok(p.planningError); assert.equal(p.tasks.length, i.universe.length); assert.ok(p.tasks.every(t => t.action === 'run')); });
}
test('missing trusted universe is a hard error, not an empty green plan', () => assert.throws(() => planContinuation({ universe: [] })));
test('unaffected proposal never removes canonical requirement', () => { const i = scenario(); i.canonicalSelected = ['backend']; i.qualifiedTasks = []; const p = planContinuation({ ...i, mode: 'enforce' }); assert.equal(p.tasks.find(t => t.taskId === 'backend').action, 'run'); assert.equal(p.tasks.find(t => t.taskId === 'docs').action, 'unaffected'); });

test('tampered evidence schedules fresh work with a rejection reason', () => {
  const i = scenario(); i.evidence.backend[0].record.expiresAt += 1;
  const task = planContinuation({ ...i, mode: 'enforce' }).tasks.find(t => t.taskId === 'backend');
  assert.equal(task.action, 'run'); assert.equal(task.reason, 'invalid signature');
});
