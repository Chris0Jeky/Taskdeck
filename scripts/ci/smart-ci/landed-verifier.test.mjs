import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import {
  mkdtempSync,
  readFileSync,
  rmSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import {
  decideLandedQualification,
} from './landed-verifier.mjs';
import { policyDigest } from './lib/plan.mjs';

const verifierPath = fileURLToPath(new URL('./landed-verifier.mjs', import.meta.url));
const policyPath = fileURLToPath(new URL('../../../ci/policy.v1.json', import.meta.url));
const currentPolicyDigest = policyDigest(readFileSync(policyPath, 'utf8'));

function sha(character) {
  return character.repeat(40);
}

function makeEvidence({
  artifactId = 101,
  runId = 201,
  pullRequest = 2327,
  baseSha = sha('1'),
  headSha = sha('2'),
  mergeSha = sha('3'),
  mergeTreeSha = sha('4'),
  digest = currentPolicyDigest,
  createdAt = '2026-09-17T20:00:00Z',
  updatedAt = createdAt,
  artifactExpired = false,
  artifactName = null,
  workflowPath = '.github/workflows/smart-ci-shadow.yml',
  workflowEvent = 'pull_request_target',
  workflowStatus = 'completed',
  workflowConclusion = 'success',
  receiptOverrides = {},
} = {}) {
  const receipt = {
    schemaVersion: 1,
    kind: 'smart-ci-gate-receipt',
    mode: 'shadow',
    ok: true,
    wouldFail: false,
    failures: [],
    notes: [],
    policyId: 'taskdeck-smart-ci-v1',
    policyDigest: digest,
    event: {
      name: 'pull_request_target',
      repository: 'Chris0Jeky/Taskdeck',
      pullRequest,
      ref: 'main',
    },
    baseSha,
    headSha,
    mergeSha,
    mergeTreeSha,
    mergeBaseSha: baseSha,
    mergeBaseTipSha: null,
    risk: 'R4',
    trust: 'T2',
    escalated: false,
    selected: [],
    skipped: [],
    generatedAtUtc: updatedAt,
    ...receiptOverrides,
  };
  return {
    artifact: {
      id: artifactId,
      name: artifactName ?? `smart-ci-receipt-${pullRequest}-${headSha}`,
      expired: artifactExpired,
      createdAt,
      updatedAt,
    },
    workflowRun: {
      id: runId,
      path: workflowPath,
      event: workflowEvent,
      status: workflowStatus,
      conclusion: workflowConclusion,
    },
    receipt,
  };
}

function decide(evidence, overrides = {}) {
  return decideLandedQualification({
    repository: 'Chris0Jeky/Taskdeck',
    headSha: sha('9'),
    headTreeSha: sha('4'),
    expectedPolicyDigest: currentPolicyDigest,
    evidence,
    ...overrides,
  });
}

test('normal PR merge selects the newest exact authoritative receipt for bounded qualification', () => {
  const older = makeEvidence({ artifactId: 101, runId: 201, updatedAt: '2026-09-17T20:00:00Z' });
  const newer = makeEvidence({ artifactId: 102, runId: 202, updatedAt: '2026-09-17T20:05:00Z' });

  const verdict = decide([older, newer]);

  assert.equal(verdict.qualification, 'bounded');
  assert.equal(verdict.reason, 'qualified-receipt');
  assert.equal(verdict.receipt.artifactId, 102);
  assert.equal(verdict.receipt.workflowRunId, 202);
  assert.equal(verdict.receipt.pullRequest, 2327);
  assert.equal(verdict.receipt.mergeTreeSha, sha('4'));
  assert.equal(verdict.diagnostics.length, 0);
});

test('direct push with no receipt fails closed to full hosted qualification', () => {
  const verdict = decide([]);

  assert.equal(verdict.qualification, 'full');
  assert.equal(verdict.reason, 'no-qualified-receipt');
  assert.equal(verdict.receipt, null);
});

test('base movement that changes the landed tree requires full requalification', () => {
  const evidence = makeEvidence({ mergeTreeSha: sha('5') });

  const verdict = decide([evidence]);

  assert.equal(verdict.qualification, 'full');
  assert.equal(verdict.reason, 'no-qualified-receipt');
  assert.ok(verdict.diagnostics.some((entry) => entry.code === 'tree-mismatch'));
});

test('expired, policy-mismatched and non-authoritative receipts cannot authorize bounded work', () => {
  const verdict = decide([
    makeEvidence({ artifactId: 1, artifactExpired: true }),
    makeEvidence({ artifactId: 2, digest: sha('a') }),
    makeEvidence({ artifactId: 3, workflowEvent: 'pull_request' }),
    makeEvidence({ artifactId: 4, workflowConclusion: 'failure' }),
    makeEvidence({ artifactId: 5, workflowPath: '.github/workflows/ci-required.yml' }),
  ]);

  assert.equal(verdict.qualification, 'full');
  assert.equal(verdict.reason, 'no-qualified-receipt');
  assert.deepEqual(
    new Set(verdict.diagnostics.map((entry) => entry.code)),
    new Set([
      'artifact-expired',
      'policy-digest-mismatch',
      'workflow-event-mismatch',
      'workflow-conclusion-mismatch',
      'workflow-path-mismatch',
    ]),
  );
});

test('receipt identity and artifact name must agree', () => {
  const evidence = makeEvidence({
    artifactName: `smart-ci-receipt-999-${sha('2')}`,
  });

  const verdict = decide([evidence]);

  assert.equal(verdict.qualification, 'full');
  assert.ok(verdict.diagnostics.some((entry) => entry.code === 'artifact-name-mismatch'));
});

test('malformed or would-fail gate receipts require full qualification', () => {
  const verdict = decide([
    makeEvidence({ artifactId: 1, receiptOverrides: { mergeSha: 'not-a-sha' } }),
    makeEvidence({ artifactId: 2, receiptOverrides: { ok: false, wouldFail: true } }),
    makeEvidence({ artifactId: 3, receiptOverrides: { kind: 'other-kind' } }),
  ]);

  assert.equal(verdict.qualification, 'full');
  assert.deepEqual(
    new Set(verdict.diagnostics.map((entry) => entry.code)),
    new Set(['receipt-invalid', 'receipt-not-green', 'receipt-kind-mismatch']),
  );
});

test('conflicting exact-tree receipt identities fail closed instead of choosing one', () => {
  const first = makeEvidence({ artifactId: 1, pullRequest: 2327, headSha: sha('2'), mergeSha: sha('3') });
  const second = makeEvidence({ artifactId: 2, pullRequest: 2328, headSha: sha('5'), mergeSha: sha('6') });

  const verdict = decide([first, second]);

  assert.equal(verdict.qualification, 'full');
  assert.equal(verdict.reason, 'ambiguous-qualified-receipts');
  assert.equal(verdict.receipt, null);
  assert.equal(verdict.candidates, 2);
});

test('CLI writes a content-free verdict and GitHub outputs', () => {
  const root = mkdtempSync(join(tmpdir(), 'taskdeck-landed-verifier-'));
  try {
    const inputPath = join(root, 'evidence.json');
    const verdictPath = join(root, 'verdict.json');
    const outputPath = join(root, 'github-output.txt');
    writeFileSync(inputPath, `${JSON.stringify([makeEvidence()], null, 2)}\n`);

    execFileSync(process.execPath, [
      verifierPath,
      '--repo', 'Chris0Jeky/Taskdeck',
      '--head-sha', sha('9'),
      '--head-tree-sha', sha('4'),
      '--policy', policyPath,
      '--input', inputPath,
      '--out', verdictPath,
      '--github-output', outputPath,
    ], { stdio: ['ignore', 'pipe', 'pipe'] });

    const verdict = JSON.parse(readFileSync(verdictPath, 'utf8'));
    assert.equal(verdict.qualification, 'bounded');
    assert.equal(verdict.receipt.artifactId, 101);
    const outputs = readFileSync(outputPath, 'utf8');
    assert.match(outputs, /^qualification=bounded$/m);
    assert.match(outputs, /^reason=qualified-receipt$/m);
    assert.match(outputs, /^receipt_artifact_id=101$/m);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
