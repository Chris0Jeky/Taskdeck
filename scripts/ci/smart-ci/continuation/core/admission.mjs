import { planContinuation } from './planner.mjs';
import { assertProducer } from './evidence.mjs';
import { canonical, hash, invariant, isGitId } from './primitives.mjs';

/**
 * Auxiliary admissibility check, not Taskdeck's canonical gate. All inputs/producer
 * contracts and the verifyFresh callback must originate in a protected controller.
 * The metadata-only GitHub observer cannot satisfy verifyFresh.
 */
export async function evaluateAdmission({ input, claimedPlan, executions = [], producerContracts = {}, verifyFresh = async () => false }) {
  try {
    invariant(input?.mode === 'enforce', 'explicit reviewed enforcement integration required');
    const plan = planContinuation(input);
    invariant(!plan.planningError, 'planning failure requires full qualification');
    invariant(canonical(plan) === canonical(claimedPlan), 'plan differs from protected recomputation');
    invariant(Array.isArray(executions), 'execution inventory required');
    const records = new Map();
    for (const record of executions) {
      invariant(record && !records.has(record.taskId) && plan.tasks.some(t => t.taskId === record.taskId), 'duplicate or unknown execution');
      records.set(record.taskId, record);
    }
    const satisfied = [];
    for (const task of plan.tasks) {
      const record = records.get(task.taskId);
      // Contradictory observations cannot be hidden behind a reused/unaffected decision.
      if (record) invariant(task.action === 'run', 'unexpected execution contradicts frozen decision');
      if (task.action === 'reuse') { satisfied.push({ taskId: task.taskId, kind: 'reuse', origin: task.origin }); continue; }
      if (task.action === 'unaffected') { invariant(input.selectionQualified === true && input.graph.tasks[task.taskId].reviewed === true, 'unqualified input-based omission'); satisfied.push({ taskId: task.taskId, kind: 'policy-unaffected' }); continue; }
      invariant(record, `missing required execution: ${task.taskId}`);
      invariant(['runId', 'jobId'].every(k => typeof record[k] === 'string' && /^[1-9]\d*$/.test(record[k])), 'execution run/job identity required');
      const contract = producerContracts[task.taskId];
      invariant(contract && isGitId(contract.workflowRevision) && typeof contract.requiresTests === 'boolean', 'reviewed producer contract required');
      const spec = input.graph.tasks[task.taskId];
      const expected = { ...contract, repositoryId: input.repositoryId, commit: input.candidateState.commit, tree: input.candidateState.tree,
        taskId: task.taskId, inputKey: task.inputKey, graphDigest: plan.binding.graphDigest, policyDigest: input.policyDigest,
        platform: spec.platform, commandDigest: hash(spec.command), environmentDigest: hash(input.environments?.[task.taskId] ?? {}) };
      assertProducer(expected, record);
      // Copy inputs: a verifier may inspect data but cannot mutate this verdict's bindings.
      let verified = false;
      try { verified = await verifyFresh(structuredClone(expected), structuredClone(record)); }
      catch { throw new Error('fresh execution verifier unavailable'); }
      invariant(verified === true, 'fresh execution provenance not established');
      satisfied.push({ taskId: task.taskId, kind: 'fresh' });
    }
    return { format: 'ci.admission.v1', authority: 'none', admissible: true, binding: plan.binding, decisionDigest: plan.decisionDigest, satisfied };
  } catch (error) {
    return { format: 'ci.admission.v1', authority: 'none', admissible: false, reason: error.message, satisfied: [] };
  }
}
