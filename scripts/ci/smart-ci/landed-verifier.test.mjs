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
import { currentPolicyDigest, makeEvidence, policyText, sha } from './test-support/landed-evidence.mjs';

const verifierPath = fileURLToPath(new URL('./landed-verifier.mjs', import.meta.url));

function decide(evidence, overrides = {}) {
  return decideLandedQualification({
    repository: 'Chris0Jeky/Taskdeck',
    headSha: sha('9'),
    headTreeSha: sha('4'),
    expectedPolicyDigest: currentPolicyDigest,
    landing: { kind: 'pull-request', pullRequest: 2327 },
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
  assert.deepEqual(verdict.landing, { kind: 'pull-request', pullRequest: 2327 });
  assert.equal(verdict.receipt.artifactId, 102);
  assert.equal(verdict.receipt.workflowRunId, 202);
  assert.equal(verdict.receipt.pullRequest, 2327);
  assert.equal(verdict.receipt.mergeTreeSha, sha('4'));
  assert.equal(verdict.diagnostics.length, 0);
});

test('direct push reusing an otherwise-qualified tree still requires full hosted qualification', () => {
  const verdict = decide([makeEvidence()], {
    landing: { kind: 'direct-push' },
  });

  assert.equal(verdict.qualification, 'full');
  assert.equal(verdict.reason, 'direct-push');
  assert.equal(verdict.receipt, null);
  assert.deepEqual(verdict.landing, { kind: 'direct-push', pullRequest: null });
});

test('missing trusted landing classification fails closed even when a tree receipt exists', () => {
  const verdict = decide([makeEvidence()], { landing: null });

  assert.equal(verdict.qualification, 'full');
  assert.equal(verdict.reason, 'landing-unverified');
  assert.equal(verdict.receipt, null);
});

test('normal merge with no matching receipt fails closed to full hosted qualification', () => {
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

test('artifact producer run identity is mandatory and must match the authority run', () => {
  const missingProducer = makeEvidence({ artifactId: 1 });
  delete missingProducer.artifact.workflowRunId;
  const mismatchedProducer = makeEvidence({
    artifactId: 2,
    runId: 202,
    artifactWorkflowRunId: 999,
  });

  const verdict = decide([missingProducer, mismatchedProducer]);

  assert.equal(verdict.qualification, 'full');
  assert.equal(verdict.reason, 'no-qualified-receipt');
  assert.deepEqual(
    new Set(verdict.diagnostics.map((entry) => entry.code)),
    new Set(['artifact-producer-run-unknown', 'artifact-workflow-run-mismatch']),
  );
});

test('expired, expiry-unknown, policy-mismatched and non-authoritative receipts cannot authorize bounded work', () => {
  const expiryUnknown = makeEvidence({ artifactId: 2 });
  delete expiryUnknown.artifact.expired;
  const verdict = decide([
    makeEvidence({ artifactId: 1, artifactExpired: true }),
    expiryUnknown,
    makeEvidence({ artifactId: 3, digest: `sha256:${'a'.repeat(64)}` }),
    makeEvidence({ artifactId: 4, workflowEvent: 'pull_request' }),
    makeEvidence({ artifactId: 5, workflowConclusion: 'failure' }),
    makeEvidence({ artifactId: 6, workflowPath: '.github/workflows/ci-required.yml' }),
  ]);

  assert.equal(verdict.qualification, 'full');
  assert.equal(verdict.reason, 'no-qualified-receipt');
  assert.deepEqual(
    new Set(verdict.diagnostics.map((entry) => entry.code)),
    new Set([
      'artifact-expired',
      'artifact-expiry-unknown',
      'policy-digest-mismatch',
      'workflow-event-mismatch',
      'workflow-conclusion-mismatch',
      'workflow-path-mismatch',
    ]),
  );
});

test('receipt identity, landing PR and artifact name must agree', () => {
  const wrongArtifact = makeEvidence({
    artifactId: 1,
    artifactName: `smart-ci-receipt-999-${sha('2')}`,
  });
  const wrongLandingPr = makeEvidence({ artifactId: 2, pullRequest: 2328 });

  const verdict = decide([wrongArtifact, wrongLandingPr]);

  assert.equal(verdict.qualification, 'full');
  assert.deepEqual(
    new Set(verdict.diagnostics.map((entry) => entry.code)),
    new Set(['artifact-name-mismatch', 'landing-pr-mismatch']),
  );
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

test('conflicting exact-tree identities for the associated PR fail closed instead of choosing one', () => {
  const first = makeEvidence({ artifactId: 1, pullRequest: 2327, headSha: sha('2'), mergeSha: sha('3') });
  const second = makeEvidence({ artifactId: 2, pullRequest: 2327, headSha: sha('5'), mergeSha: sha('6') });

  const verdict = decide([first, second]);

  assert.equal(verdict.qualification, 'full');
  assert.equal(verdict.reason, 'ambiguous-qualified-receipts');
  assert.equal(verdict.receipt, null);
  assert.equal(verdict.candidates, 2);
});

test('CLI writes a content-free verdict, landing binding and GitHub outputs', () => {
  const root = mkdtempSync(join(tmpdir(), 'taskdeck-landed-verifier-'));
  try {
    const policyPath = join(root, 'policy.json');
    writeFileSync(policyPath, policyText);
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
      '--landing-kind', 'pull-request',
      '--landing-pr', '2327',
      '--out', verdictPath,
      '--github-output', outputPath,
    ], { stdio: ['ignore', 'pipe', 'pipe'] });

    const verdict = JSON.parse(readFileSync(verdictPath, 'utf8'));
    assert.equal(verdict.qualification, 'bounded');
    assert.deepEqual(verdict.landing, { kind: 'pull-request', pullRequest: 2327 });
    assert.equal(verdict.receipt.artifactId, 101);
    assert.equal(verdict.receipt.workflowRunId, 201);
    const outputs = readFileSync(outputPath, 'utf8');
    assert.match(outputs, /^qualification=bounded$/m);
    assert.match(outputs, /^reason=qualified-receipt$/m);
    assert.match(outputs, /^landing_kind=pull-request$/m);
    assert.match(outputs, /^landing_pr=2327$/m);
    assert.match(outputs, /^receipt_artifact_id=101$/m);
    assert.match(outputs, /^receipt_workflow_run_id=201$/m);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});


test('a real planner-only shadow receipt cannot qualify a landed commit', () => {
  const evidence = makeEvidence({ mode: 'shadow', supplyResults: false });
  assert.equal(evidence.receipt.ok, true);
  assert.equal(evidence.receipt.wouldFail, false);
  assert.ok(evidence.receipt.selected.length > 0);
  const verdict = decide([evidence], { expectedPolicyDigest: evidence.receipt.policyDigest });
  assert.equal(verdict.qualification, 'full');
  assert.equal(verdict.receipt, null);
  assert.ok(verdict.diagnostics.some((entry) => entry.code === 'receipt-not-enforced'));
});

test('a real enforce receipt with missing lane evidence fails closed', () => {
  const evidence = makeEvidence({ supplyResults: false });
  assert.equal(evidence.receipt.mode, 'enforce');
  assert.equal(evidence.receipt.ok, false);
  assert.equal(evidence.receipt.wouldFail, true);
  assert.equal(decide([evidence]).qualification, 'full');
});

for (const location of ['artifact', 'workflowRun']) {
  for (const value of [undefined, 'SomeoneElse/OtherRepo']) {
    test(`${location} repository must be externally bound (${value ?? 'missing'})`, () => {
      const evidence = makeEvidence();
      if (value === undefined) delete evidence[location].repository;
      else evidence[location].repository = value;
      const verdict = decide([evidence]);
      assert.equal(verdict.qualification, 'full');
      assert.ok(verdict.diagnostics.some((entry) => entry.code === 'repository-mismatch'));
    });
  }
}

for (const receiptOverrides of [
  { ok: true, wouldFail: true, failures: [{ code: 'selected-not-success' }] },
  { ok: true, wouldFail: false, failures: [{ code: 'selected-not-success' }] },
  { selected: [] },
  { selected: null },
]) {
  test(`green-looking incomplete receipt is rejected: ${JSON.stringify(receiptOverrides)}`, () => {
    const verdict = decide([makeEvidence({ receiptOverrides })]);
    assert.equal(verdict.qualification, 'full');
    assert.equal(verdict.receipt, null);
  });
}
