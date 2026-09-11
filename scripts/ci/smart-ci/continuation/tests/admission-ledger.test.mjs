import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, writeFileSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { evaluateAdmission } from '../core/admission.mjs';
import { planContinuation } from '../core/planner.mjs';
import { hash } from '../core/primitives.mjs';
import { GENESIS, decodeLedger, appendLedger, ledgerState, qualificationRequired } from '../core/ledger.mjs';
import { sample, oid, digest } from '../examples/sample-model.mjs';
function admission() {
  const input = { ...sample(), mode: 'enforce', qualifiedTasks: [] }, claimedPlan = planContinuation(input);
  const producerContracts = {}, executions = [];
  for (const t of claimedPlan.tasks) {
    producerContracts[t.taskId] = { workflowId: '1', workflowPath: '.github/workflows/test.yml', workflowRevision: oid('a'), requiresTests: true };
    executions.push({ ...producerContracts[t.taskId], repositoryId: input.repositoryId, commit: input.candidateState.commit, tree: input.candidateState.tree,
      taskId: t.taskId, inputKey: t.inputKey, graphDigest: claimedPlan.binding.graphDigest, policyDigest: input.policyDigest,
      platform: input.graph.tasks[t.taskId].platform, commandDigest: hash(input.graph.tasks[t.taskId].command), environmentDigest: hash(input.environments[t.taskId]),
      runId: '2', jobId: String(executions.length + 1), workflowDefinitionTrusted: true, inputManifestRecomputed: true, status: 'completed', conclusion: 'success', attempt: 1,
      earlierFailure: false, allowFailure: false, coverageComplete: true, executedChecks: 1, executedTests: 4 });
  }
  return { input, claimedPlan, producerContracts, executions, verifyFresh: async () => true };
}
test('all current tasks require independently verified execution', async () => { const a = await evaluateAdmission(admission()); assert.ok(a.admissible); assert.equal(a.authority, 'none'); assert.equal(a.satisfied.length, 4); });
test('metadata flags alone cannot supply missing provenance verifier', async () => { const a = admission(); delete a.verifyFresh; assert.equal((await evaluateAdmission(a)).admissible, false); });
for (const [name, mutate] of Object.entries({ missing: a => a.executions.pop(), duplicate: a => a.executions.push(a.executions[0]), unknown: a => a.executions[0].taskId = 'unknown',
  failed: a => a.executions[0].conclusion = 'failure', skipped: a => a.executions[0].conclusion = 'skipped', cancelled: a => a.executions[0].conclusion = 'cancelled',
  wrongSha: a => a.executions[0].commit = oid('b'), wrongTree: a => a.executions[0].tree = oid('b'), wrongPolicy: a => a.executions[0].policyDigest = digest('b'),
  empty: a => a.executions[0].executedTests = 0, retried: a => a.executions[0].attempt = 2, oldFailure: a => a.executions[0].earlierFailure = true,
  forgedPlan: a => a.claimedPlan.tasks[0].action = 'unaffected', wrongDigest: a => a.claimedPlan.decisionDigest = digest('b'),
  policyChanged: a => a.input.policyDigest = digest('b'), observationMode: a => a.input.mode = 'observe', noContract: a => a.producerContracts = {},
  incompleteInput: a => a.input.candidateState.complete = false, falseVerifier: a => a.verifyFresh = async () => false,
  errorVerifier: a => a.verifyFresh = async () => { throw new Error('offline'); } })) {
  test(`admission rejects ${name}`, async () => { const a = admission(); mutate(a); assert.equal((await evaluateAdmission(a)).admissible, false); });
}
const full = (at = 10) => ({ kind: 'full', at, baselineId: digest('a'), policyDigest: digest('f'), tree: oid('a'), covers: ['backend', 'frontend'], complete: true, conclusion: 'success' });
function temp(fn) { const dir = mkdtempSync(join(tmpdir(), 'ci-ledger-')); try { return fn(dir); } finally { rmSync(dir, { recursive: true, force: true }); } }
function load(dir, anchor) { return decodeLedger(readFileSync(join(dir, 'events.jsonl'), 'utf8'), anchor); }
test('append CAS, complete chain, revocation and merge exposure persist', () => temp(dir => {
  let anchor = appendLedger(dir, GENESIS, full()).digest;
  anchor = appendLedger(dir, anchor, { kind: 'revoke', at: 11, inputKey: digest('b'), reason: 'oracle failed' }).digest;
  anchor = appendLedger(dir, anchor, { kind: 'merge', at: 12, tree: oid('c'), policyDigest: digest('f') }).digest;
  const s = ledgerState(load(dir, anchor)); assert.deepEqual(s.revokedInputKeys, [digest('b')]); assert.equal(s.mergesSinceFull, 1);
  assert.throws(() => appendLedger(dir, GENESIS, full(13)), /anchor/); assert.ok(!existsSync(join(dir, 'writer.lock')));
}));
test('held writer lock fails without deleting another writer lock', () => temp(dir => {
  writeFileSync(join(dir, 'writer.lock'), 'another writer'); assert.throws(() => appendLedger(dir, GENESIS, full()), /busy/); assert.ok(existsSync(join(dir, 'writer.lock')));
}));
test('crash tail, corruption and stale anchors fail closed', () => temp(dir => {
  const a = appendLedger(dir, GENESIS, full()), text = readFileSync(join(dir, 'events.jsonl'), 'utf8');
  assert.throws(() => decodeLedger(text.slice(0, -1), a.digest)); assert.throws(() => decodeLedger(text.replace('backend', 'tampered'), a.digest));
  assert.throws(() => decodeLedger('', a.digest)); assert.throws(() => decodeLedger(text, GENESIS));
}));
test('clock rollback and invalid event are rejected', () => temp(dir => {
  const a = appendLedger(dir, GENESIS, full()); assert.throws(() => appendLedger(dir, a.digest, full(9))); assert.throws(() => appendLedger(dir, a.digest, { kind: 'waive', at: 11 }));
}));
test('circuit recovery needs a newer full baseline covering that task', () => temp(dir => {
  let anchor = appendLedger(dir, GENESIS, { kind: 'trip', at: 5, taskId: 'backend', reason: 'miss' }).digest;
  anchor = appendLedger(dir, anchor, full(10)).digest;
  anchor = appendLedger(dir, anchor, { kind: 'recover', at: 11, taskId: 'backend', baselineId: digest('a') }).digest;
  assert.deepEqual(ledgerState(load(dir, anchor)).disabledTasks, []);
}));
test('an old baseline cannot erase a later circuit trip', () => temp(dir => {
  let anchor = appendLedger(dir, GENESIS, full(10)).digest;
  anchor = appendLedger(dir, anchor, { kind: 'trip', at: 11, taskId: 'backend', reason: 'miss' }).digest;
  assert.throws(() => appendLedger(dir, anchor, { kind: 'recover', at: 12, taskId: 'backend', baselineId: digest('a') }), /newer/);
  assert.deepEqual(ledgerState(load(dir, anchor)).disabledTasks, ['backend']);
}));
function qualification() { return { state: { anchor: digest('a'), baseline: full(10), mergesSinceFull: 1, disabledTasks: [] }, anchorVerified: true, now: 11, policyDigest: digest('f'), universe: ['backend','frontend'], maxAgeSeconds: 10, maxMerges: 3 }; }
test('verified age/exposure permits normal policy, not automatic success', () => assert.equal(qualificationRequired(qualification()).required, false));
for (const [name, mutate] of Object.entries({ unverified: a => a.anchorVerified = false, stale: a => a.now = 20, future: a => a.now = 9,
  exposure: a => a.state.mergesSinceFull = 3, policy: a => a.policyDigest = digest('b'), partial: a => a.state.baseline.covers = ['backend'],
  circuit: a => a.state.disabledTasks = ['backend'], mandatory: a => a.mandatory = true, noState: a => a.state = null, invalidBudget: a => a.maxMerges = 0 })) {
  test(`full qualification on ${name}`, () => { const a = qualification(); mutate(a); assert.ok(qualificationRequired(a).required); });
}

test('missing execution job identity cannot satisfy admission', async () => { const a = admission(); delete a.executions[0].jobId; assert.equal((await evaluateAdmission(a)).admissible, false); });
test('verifier exceptions cannot leak provider credential messages', async () => { const a = admission(); a.verifyFresh = async () => { throw new Error('sensitive-token'); }; const v = await evaluateAdmission(a); assert.equal(v.admissible, false); assert.ok(!v.reason.includes('sensitive')); });
test('input-based omission needs explicit selection qualification', async () => {
  const a = admission(); a.input.canonicalSelected = []; a.claimedPlan = planContinuation(a.input); a.executions = [];
  assert.equal((await evaluateAdmission(a)).admissible, false);
  a.input.selectionQualified = true; a.claimedPlan = planContinuation(a.input); assert.ok((await evaluateAdmission(a)).admissible);
});
test('duplicate full baseline identity is rejected before persistence', () => temp(dir => {
  const r = appendLedger(dir, GENESIS, full()); assert.throws(() => appendLedger(dir, r.digest, full(11)), /duplicate/);
  assert.equal(load(dir, r.digest).records.length, 1);
}));
