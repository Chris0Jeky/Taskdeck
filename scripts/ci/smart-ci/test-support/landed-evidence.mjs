// Generate fixtures through the real planner and gate CLI, not hand-written green receipts.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { buildPlan, policyDigest } from '../lib/plan.mjs';

export const repository = 'Chris0Jeky/Taskdeck';
const checkedInPolicy = JSON.parse(readFileSync(new URL('../../../../ci/policy.v1.json', import.meta.url), 'utf8'));
export const policyText = JSON.stringify({ ...checkedInPolicy, mode: 'enforce' });
export const currentPolicyDigest = policyDigest(policyText);
const gatePath = fileURLToPath(new URL('../evaluate-gate.mjs', import.meta.url));
export const sha = (character) => character.repeat(40);

export function makeEvidence({
  artifactId = 101,
  runId = 201,
  artifactWorkflowRunId = runId,
  pullRequest = 2327,
  baseSha = sha('1'),
  headSha = sha('2'),
  mergeSha = sha('3'),
  mergeTreeSha = sha('4'),
  digest = null,
  createdAt = '2026-09-17T20:00:00Z',
  updatedAt = createdAt,
  artifactExpired = false,
  artifactName = null,
  workflowPath = '.github/workflows/smart-ci-shadow.yml',
  workflowEvent = 'pull_request_target',
  workflowStatus = 'completed',
  workflowConclusion = 'success',
  mode = 'enforce',
  supplyResults = true,
  receiptOverrides = {},
} = {}) {
  const root = mkdtempSync(join(tmpdir(), 'taskdeck-landed-fixture-'));
  let receipt;
  try {
    const fixturePolicy = { ...checkedInPolicy, mode };
    const text = JSON.stringify(fixturePolicy);
    const plan = buildPlan({
      eventName: 'pull_request_target', repository, pullRequestNumber: pullRequest,
      ref: 'main', isDraft: false, baseSha, headSha, mergeSha, mergeTreeSha,
      mergeRefQualification: 'qualified', mergeBaseSha: baseSha, mergeBaseTipSha: null,
      actorLogin: 'Chris0Jeky', actorType: 'User', authorAssociation: 'OWNER',
      isFork: false, labels: [], changedFiles: ['docs/ci/SMART_CI.md'],
      changedFilesAvailable: true, executionMode: 'hosted',
    }, fixturePolicy, policyDigest(text));
    const policyPath = join(root, 'policy.json');
    const planPath = join(root, 'plan.json');
    const resultsPath = join(root, 'results.json');
    const receiptPath = join(root, 'receipt.json');
    writeFileSync(policyPath, text);
    writeFileSync(planPath, JSON.stringify(plan));
    writeFileSync(resultsPath, JSON.stringify(Object.fromEntries(plan.selected.map((entry) => [entry.checkName, { conclusion: 'success', headSha }]))));
    const result = spawnSync(process.execPath, [
      gatePath, '--plan', planPath, '--policy', policyPath, '--mode', mode,
      '--expected-head', headSha, '--expected-base', baseSha, '--plan-job-result', 'success',
      '--receipt', receiptPath, ...(supplyResults ? ['--results', resultsPath] : []),
    ], { encoding: 'utf8', timeout: 15_000 });
    assert.equal(result.error, undefined);
    assert.equal(result.status, mode === 'enforce' && !supplyResults ? 1 : 0, result.stderr || result.stdout);
    receipt = JSON.parse(readFileSync(receiptPath, 'utf8'));
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
  return {
    artifact: {
      id: artifactId, repository,
      name: artifactName ?? `smart-ci-receipt-${pullRequest}-${headSha}`,
      workflowRunId: artifactWorkflowRunId, expired: artifactExpired, createdAt, updatedAt,
    },
    workflowRun: {
      id: runId, repository, path: workflowPath, event: workflowEvent,
      status: workflowStatus, conclusion: workflowConclusion,
    },
    receipt: { ...receipt, ...(digest === null ? {} : { policyDigest: digest }), ...receiptOverrides },
  };
}
