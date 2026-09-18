import { changedPaths } from './snapshot.mjs';
import { expandAffected, fingerprint, validateContracts } from './contracts.mjs';
import { verifyEvidence } from './evidence.mjs';
import { hash, invariant } from './primitives.mjs';

/**
 * Provider-neutral continuation planner. Consumes a trusted canonical selection floor.
 * Default observe mode executes the complete universe and reports only hypothetical reuse.
 * Enforcement here is for reviewed integrations/examples, NOT enabled by Taskdeck's adapter.
 * The returned object is not a CI success receipt or a required-check verdict.
 */
export function planContinuation(input) {
  const { universe, graph, canonicalSelected, baseState, candidateState, mode = 'observe' } = input;
  invariant(Array.isArray(universe) && universe.length > 0 && new Set(universe).size === universe.length &&
    universe.every(id => typeof id === 'string' && /^[a-z0-9-]+$/.test(id)), 'trusted task universe required');
  invariant(['observe', 'enforce'].includes(mode), 'unsupported continuation mode');
  const fallback = reason => ({ format: 'ci.continuation.v1', mode, fallback: reason, planningError: true,
    tasks: [...universe].sort().map(taskId => ({ taskId, action: 'run', proposed: 'run', reason: 'conservative-full-fallback' })) });
  try {
    validateContracts(graph);
    invariant(Object.keys(graph.tasks).length === universe.length && universe.every(id => Object.hasOwn(graph.tasks, id)), 'graph/universe mismatch');
    invariant(Array.isArray(canonicalSelected) && new Set(canonicalSelected).size === canonicalSelected.length && canonicalSelected.every(id => universe.includes(id)), 'canonical floor mismatch');
    const paths = changedPaths(baseState, candidateState), impact = expandAffected(graph, canonicalSelected, paths);
    const full = input.fullQualification === true || impact.fallback !== null;
    const selected = new Set(full ? universe : impact.selected);
    const enabled = input.qualifiedTasks ?? [];
    invariant(Array.isArray(enabled) && enabled.every(id => universe.includes(id)), 'invalid qualified task list');
    const tasks = [];
    for (const taskId of [...universe].sort()) {
      const fp = fingerprint({ graph, taskId, state: candidateState, environment: input.environments?.[taskId] ?? {},
        context: input.context ?? {}, policyDigest: input.policyDigest, repositoryId: input.repositoryId });
      let proposed = 'run', reason = full ? 'full-qualification' : 'missing-or-invalid-evidence', origin = null;
      if (!selected.has(taskId)) { proposed = 'unaffected'; reason = 'canonical-floor-plus-input-closure'; }
      else if (!full && enabled.includes(taskId)) {
        const envelopes = input.evidence?.[taskId] ?? [];
        invariant(Array.isArray(envelopes), 'evidence list required');
        for (const envelope of envelopes) {
          const verdict = verifyEvidence(envelope, fp, { ...input.verification, fullQualification: false });
          if (verdict.reusable) { proposed = 'reuse'; reason = verdict.reason; origin = verdict.origin; break; }
          reason = verdict.reason;
        }
      } else if (!full) reason = 'task-not-qualified-for-reuse';
      // Observation NEVER reduces work. Disabled task-level reuse does not override the
      // canonical selector; qualification of path selection is the canonical controller's job.
      const action = mode === 'observe' ? 'run' : proposed;
      tasks.push({ taskId, action, proposed, reason, inputKey: fp.key, inputCount: fp.inputCount,
        reusableContract: fp.reusable, contractReasons: fp.reasons, origin });
    }
    const binding = { repositoryId: input.repositoryId, commit: candidateState.commit, tree: candidateState.tree,
      baseCommit: baseState.commit, policyDigest: input.policyDigest, graphDigest: hash(graph) };
    return { format: 'ci.continuation.v1', mode, fallback: impact.fallback, planningError: false, fullQualification: full,
      binding, decisionDigest: hash({ binding, mode, tasks }), tasks };
  } catch (error) { return fallback(error.message); }
}
