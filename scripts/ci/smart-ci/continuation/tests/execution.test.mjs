import test from 'node:test';
import assert from 'node:assert/strict';
import { nextWave, shardTests, validatePartition } from '../core/execution.mjs';
import { auditSelection, sampledAudit, zeroMissUpperBound, requiresFullQualification } from '../core/audit.mjs';
const tasks = () => [
  { id: 'control', needs: [], seconds: 10, failureProbability: 0.1 },
  { id: 'backend', needs: ['control'], seconds: 100, failureProbability: 0.2 },
  { id: 'frontend', needs: ['control'], seconds: 50, failureProbability: 0.2 },
  { id: 'e2e', needs: ['backend', 'frontend'], seconds: 200, failureProbability: 0.1 }
];
test('only control launches initially', () => assert.deepEqual(nextWave(tasks(), {}).launch, ['control']));
test('semantic work launches together after control', () => assert.deepEqual(nextWave(tasks(), { control: { status: 'success' } }).launch, ['frontend', 'backend']));
test('failure blocks expensive tail; successful sibling stays successful', () => {
  const states = { control: { status: 'success' }, backend: { status: 'success' }, frontend: { status: 'failure' } };
  const before = structuredClone(states), wave = nextWave(tasks(), states);
  assert.ok(wave.halt); assert.equal(wave.allSelectedSatisfied, false); assert.deepEqual(wave.launch, []); assert.deepEqual(states, before);
});
test('verified reused prerequisite counts but skipped unknown state does not', () => {
  const s = { control: { status: 'success' }, backend: { status: 'reused', verified: true }, frontend: { status: 'success' } };
  assert.deepEqual(nextWave(tasks(), s).launch, ['e2e']); s.backend.verified = false; assert.throws(() => nextWave(tasks(), s));
});
test('every selected task needed before satisfying execution', () => {
  const s = Object.fromEntries(tasks().map(t => [t.id, { status: 'success' }])); assert.ok(nextWave(tasks(), s).allSelectedSatisfied);
  delete s.e2e; assert.equal(nextWave(tasks(), s).allSelectedSatisfied, false);
});
test('slots account for already running tasks', () => assert.deepEqual(nextWave(tasks(), { control: { status: 'success' }, backend: { status: 'running' } }, { slots: 1 }).launch, []));
test('ordering estimates cannot remove ready tests', () => { const t = tasks(); t[1].failureProbability = 0; assert.equal(nextWave(t, { control: { status: 'success' } }).launch.length, 2); });
for (const mutate of [t => t[0].needs.push('e2e'), t => t[0].needs.push('missing'), t => t.push(t[0]), t => t[0].failureProbability = 2, t => t[0].seconds = 0]) {
  test(`invalid DAG ${String(mutate)}`, () => { const t = tasks(); mutate(t); assert.throws(() => nextWave(t, {})); });
}
test('unknown/skipped state is not success', () => assert.throws(() => nextWave(tasks(), { frontend: { status: 'skipped' } })));
test('cancelled task halts tail', () => assert.ok(nextWave(tasks(), { control: { status: 'cancelled' } }).halt));
const inventory = () => [ { id: 'a', seconds: 10, affinity: 'db' }, { id: 'b', seconds: 20, affinity: 'db' },
  { id: 'c', seconds: 25, affinity: 'solo-c' }, { id: 'd', seconds: 5, affinity: 'solo-d' } ];
test('shard affinity and exact inventory preserved', () => {
  const t = inventory(), shards = shardTests(t, 2); assert.ok(validatePartition(t.map(x => x.id), shards));
  assert.ok(shards.some(s => s.tests.includes('a') && s.tests.includes('b'))); assert.deepEqual(shards.map(s => s.seconds), [30, 30]);
});
test('sharding deterministic under input enumeration changes', () => assert.deepEqual(shardTests(inventory(), 2), shardTests(inventory().reverse(), 2)));
test('no empty shards even for large capacity', () => assert.equal(shardTests(inventory(), 20).length, 3));
test('empty discovery fails', () => assert.throws(() => shardTests([], 2)));
test('duplicate discovery fails', () => assert.throws(() => shardTests([...inventory(), inventory()[0]], 2)));
test('missing test assignment fails', () => assert.throws(() => validatePartition(['a', 'b'], [{ tests: ['a'] }])));
test('duplicate test assignment fails', () => assert.throws(() => validatePartition(['a', 'b'], [{ tests: ['a', 'a'] }])));
test('unknown test assignment fails', () => assert.throws(() => validatePartition(['a', 'b'], [{ tests: ['a', 'z'] }])));
const audit = () => ({ selected: ['a'], universe: ['a', 'b'], identitiesVerified: true,
  outcomes: [{ taskId: 'a', attempt: 1, conclusion: 'success' }, { taskId: 'b', attempt: 1, conclusion: 'failure' }] });
test('missed failure trips circuit', () => { const a = auditSelection(audit()); assert.deepEqual(a.misses, ['b']); assert.ok(a.forceFull); });
test('later successful rerun cannot erase recall miss', () => { const a = audit(); a.outcomes.push({ taskId: 'b', attempt: 2, conclusion: 'success' }); assert.equal(auditSelection(a).status, 'miss'); });
test('incomplete oracle is unusable not zero-miss evidence', () => { const a = audit(); a.outcomes.pop(); assert.equal(auditSelection(a).status, 'unusable'); });
test('unbound oracle is unusable', () => { const a = audit(); a.identitiesVerified = false; assert.equal(auditSelection(a).status, 'unusable'); });
test('all-green oracle is only an observation', () => { const a = audit(); a.outcomes[1].conclusion = 'success'; assert.equal(auditSelection(a).status, 'observed'); });
test('20 zero-miss samples do not establish rare-event safety', () => { assert.ok(zeroMissUpperBound(20) > 0.13); assert.ok(zeroMissUpperBound(300) < 0.01); });
test('zero sample is not confidence evidence', () => assert.throws(() => zeroMissUpperBound(0)));
test('audit sampling deterministic and exact at boundaries', () => {
  const a = { repositoryId: '1', tree: 'abc', epoch: '1', secret: 'x'.repeat(32), rate: 0.1 };
  assert.equal(sampledAudit(a), sampledAudit(a)); assert.equal(sampledAudit({ ...a, rate: 0 }), false); assert.equal(sampledAudit({ ...a, rate: 1 }), true);
});
test('weak audit salt rejected', () => assert.throws(() => sampledAudit({ secret: 'a' })));
const fresh = () => ({ now: 100, policyDigest: 'policy', risk: 'R2', event: 'pr', force: false,
  baseline: { completedAt: 90, maxAgeSeconds: 20, conclusion: 'success', complete: true, policyDigest: 'policy' } });
test('fresh complete baseline permits normal policy, not automatic skipping', () => assert.equal(requiresFullQualification(fresh()).required, false));
for (const [name, mutate] of Object.entries({ release: x => x.event = 'release', weekly: x => x.event = 'weekly', control: x => x.risk = 'R4',
  force: x => x.force = true, failed: x => x.baseline.conclusion = 'failure', incomplete: x => x.baseline.complete = false,
  absent: x => x.baseline = null, expired: x => x.now = 110, future: x => x.now = 80, policy: x => x.policyDigest = 'new' })) {
  test(`${name} requires full qualification`, () => { const x = fresh(); mutate(x); assert.ok(requiresFullQualification(x).required); });
}
