import { generateKeyPairSync } from 'node:crypto';
import { fingerprint } from '../core/contracts.mjs';
import { signRecord } from '../core/evidence.mjs';
import { planContinuation } from '../core/planner.mjs';
import { nextWave, shardTests } from '../core/execution.mjs';
import { sample, oid } from './sample-model.mjs';

// Entirely synthetic: no network, credentials, Taskdeck execution, or remote status writes.
const { privateKey, publicKey } = generateKeyPairSync('ed25519');
const input = sample();
const fp = fingerprint({ graph: input.graph, taskId: 'backend', state: input.baseState,
  environment: input.environments.backend, context: input.context, policyDigest: input.policyDigest, repositoryId: input.repositoryId });
const successfulBackend = signRecord({ version: 1, issuer: 'demo-collector', keyId: 'ephemeral', repositoryId: input.repositoryId,
  taskId: 'backend', inputKey: fp.key, policyDigest: fp.policyDigest, graphDigest: fp.graphDigest, platform: fp.platform,
  commit: input.baseState.commit, tree: input.baseState.tree, workflowId: '1', runId: '2', jobId: '3', attempt: 1,
  conclusion: 'success', executedTests: 400, completedAt: 1000, expiresAt: 4600 }, privateKey);
input.evidence = { backend: [successfulBackend] };
input.verification = { now: 1100, trustedKeys: { ephemeral: publicKey }, issuer: 'demo-collector', ledgerComplete: true,
  revokedInputKeys: [], disabledTasks: [] };
input.candidateState.commit = oid('3'); input.candidateState.tree = oid('4'); input.candidateState.entries[1].oid = oid('b');
function summarize(plan) { return { mode: plan.mode, error: plan.planningError, tasks: plan.tasks.map(({ taskId, action, proposed, reason }) => ({ taskId, action, proposed, reason })) }; }
const observation = planContinuation(input);
const qualifiedIntegration = planContinuation({ ...input, mode: 'enforce' });
const lockChanged = structuredClone({ ...input, verification: undefined });
lockChanged.verification = input.verification; lockChanged.candidateState.entries[4].oid = oid('c');
const lockInvalidation = planContinuation({ ...lockChanged, mode: 'enforce' });
const fullSweep = planContinuation({ ...input, mode: 'enforce', fullQualification: true });
const tampered = { ...input, evidence: structuredClone(input.evidence) };
tampered.evidence.backend[0].record.expiresAt += 3600;
const tamperMiss = planContinuation({ ...tampered, mode: 'enforce' });
const tail = nextWave([
  { id: 'frontend', needs: [], seconds: 50, failureProbability: 0.1 },
  { id: 'backend', needs: [], seconds: 100, failureProbability: 0.1 },
  { id: 'e2e', needs: ['frontend', 'backend'], seconds: 300, failureProbability: 0.1 }
], { frontend: { status: 'failure' }, backend: { status: 'success' } });
const output = { note: 'Synthetic reference demonstration. No product tests or hosted CI executed.',
  observation: summarize(observation), qualifiedIntegration: summarize(qualifiedIntegration), lockInvalidation: summarize(lockInvalidation),
  fullSweep: summarize(fullSweep), tamperMiss: summarize(tamperMiss), failedFrontendStopsTail: tail,
  affinityAwareShards: shardTests([{ id: 'sqlite-A', seconds: 20, affinity: 'shared-db' }, { id: 'sqlite-B', seconds: 30, affinity: 'shared-db' },
    { id: 'pure-C', seconds: 40, affinity: 'pure-C' }, { id: 'pure-D', seconds: 10, affinity: 'pure-D' }], 2) };
console.log(JSON.stringify(output, null, 2));
