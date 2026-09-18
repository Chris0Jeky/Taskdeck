import { closure, compare, invariant } from './primitives.mjs';

/**
 * DAG readiness simulator / provider-neutral scheduling primitive. Never posts a check.
 * Barriers order work, not input dependencies: graph.components models input closure.
 */
export function nextWave(tasks, states, { slots = 4, stopOnFailure = true } = {}) {
  invariant(Number.isSafeInteger(slots) && slots > 0, 'positive slots required');
  const nodes = Object.fromEntries(tasks.map(task => [task.id, { deps: task.needs }]));
  invariant(tasks.length > 0 && Object.keys(nodes).length === tasks.length, 'empty or duplicate task set');
  for (const task of tasks) {
    invariant(Array.isArray(task.needs), 'explicit dependencies required');
    closure(task.id, nodes);
    invariant(Number.isFinite(task.seconds) && task.seconds > 0, 'positive cost estimate required');
    invariant(Number.isFinite(task.failureProbability) && task.failureProbability >= 0 && task.failureProbability <= 1, 'invalid failure estimate');
  }
  for (const [id, state] of Object.entries(states)) {
    invariant(Object.hasOwn(nodes, id), `unknown execution state: ${id}`);
    invariant(['pending', 'running', 'success', 'failure', 'cancelled', 'reused'].includes(state.status), 'unknown status');
    if (state.status === 'reused') invariant(state.verified === true, 'unverified reuse cannot satisfy a dependency');
  }
  const status = id => states[id]?.status ?? 'pending';
  const satisfied = id => ['success', 'reused'].includes(status(id));
  const hardFailure = tasks.some(t => ['failure', 'cancelled'].includes(status(t.id)));
  const running = tasks.filter(t => status(t.id) === 'running').length;
  const pending = tasks.filter(t => status(t.id) === 'pending');
  const ready = pending.filter(t => t.needs.every(satisfied));
  // Failure probability / duration optimizes feedback order, NEVER test omission.
  ready.sort((a, b) => (b.failureProbability / b.seconds - a.failureProbability / a.seconds) || compare(a.id, b.id));
  const launch = hardFailure && stopOnFailure ? [] : ready.slice(0, Math.max(0, slots - running)).map(t => t.id);
  return { launch, blocked: pending.filter(t => !launch.includes(t.id)).map(t => t.id).sort(),
    halt: hardFailure && stopOnFailure, allSelectedSatisfied: tasks.every(t => satisfied(t.id)) };
}

/** Deterministic longest-processing-time packing, keeping isolation-affinity groups together. */
export function shardTests(tests, shardCount) {
  invariant(Number.isSafeInteger(shardCount) && shardCount > 0, 'positive shard count required');
  invariant(Array.isArray(tests) && tests.length > 0, 'nonempty discovered inventory required');
  const seen = new Set(), groups = new Map();
  for (const test of tests) {
    invariant(typeof test.id === 'string' && test.id.length > 0 && !seen.has(test.id), 'duplicate or invalid test id'); seen.add(test.id);
    invariant(Number.isFinite(test.seconds) && test.seconds > 0, 'positive test duration required');
    invariant(typeof test.affinity === 'string' && test.affinity.length > 0, 'explicit isolation affinity required');
    if (!groups.has(test.affinity)) groups.set(test.affinity, { id: test.affinity, tests: [], seconds: 0 });
    const group = groups.get(test.affinity); group.tests.push(test.id); group.seconds += test.seconds;
  }
  const count = Math.min(shardCount, groups.size); // Never create a zero-test "green" shard.
  const shards = Array.from({ length: count }, (_, id) => ({ id, tests: [], seconds: 0, affinities: [] }));
  for (const group of [...groups.values()].sort((a, b) => b.seconds - a.seconds || compare(a.id, b.id))) {
    const shard = [...shards].sort((a, b) => a.seconds - b.seconds || a.id - b.id)[0];
    shard.tests.push(...group.tests.sort()); shard.affinities.push(group.id); shard.seconds += group.seconds;
  }
  return shards;
}

/** Selection/sharding must partition the discovered test IDs, not silently lose a test. */
export function validatePartition(discovered, shards) {
  invariant(new Set(discovered).size === discovered.length && discovered.length > 0, 'invalid discovery');
  const actual = shards.flatMap(s => s.tests);
  invariant(shards.length > 0 && shards.every(s => s.tests.length > 0), 'empty shard');
  invariant(new Set(actual).size === actual.length, 'duplicate assignment');
  invariant(actual.length === discovered.length && actual.every(id => discovered.includes(id)), 'incomplete partition');
  return true;
}
