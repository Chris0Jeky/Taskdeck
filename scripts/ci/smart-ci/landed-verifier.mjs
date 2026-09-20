#!/usr/bin/env node
// CI-03 (#2327, ADR-0066): decide whether a landed main commit may use the
// bounded verifier path. This module is deliberately content-free and fail-closed.
// It consumes already-collected Smart CI receipt evidence plus trusted landing
// classification; collection and workflow topology remain separate concerns.

import { execFileSync } from 'node:child_process';
import {
  appendFileSync,
  existsSync,
  mkdirSync,
  readFileSync,
  realpathSync,
  writeFileSync,
  writeSync,
} from 'node:fs';
import { dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { policyDigest } from './lib/plan.mjs';

const RECEIPT_ARTIFACT = /^smart-ci-receipt-(\d+)-([0-9a-f]{40})$/i;
const AUTHORITY_WORKFLOW = '.github/workflows/smart-ci-shadow.yml';
const AUTHORITY_EVENT = 'pull_request_target';

function isObject(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function validSha(value) {
  return /^[0-9a-f]{40}$/i.test(String(value ?? ''));
}

function validPolicyDigest(value) {
  return /^sha256:[0-9a-f]{64}$/i.test(String(value ?? ''));
}

function validRepository(value) {
  return /^[\w.-]+\/[\w.-]+$/.test(String(value ?? ''));
}

function positiveInteger(value) {
  return Number.isInteger(value) && value > 0;
}

function lower(value) {
  return String(value).toLowerCase();
}

function timestamp(value) {
  const parsed = Date.parse(value ?? '');
  return Number.isNaN(parsed) ? 0 : parsed;
}

function normaliseLanding(landing) {
  if (!isObject(landing)) return null;
  if (landing.kind === 'direct-push') {
    return { kind: 'direct-push', pullRequest: null };
  }
  if (landing.kind === 'pull-request' && positiveInteger(landing.pullRequest)) {
    return { kind: 'pull-request', pullRequest: landing.pullRequest };
  }
  return null;
}

function diagnostic(artifactId, code) {
  return {
    artifactId: positiveInteger(artifactId) ? artifactId : null,
    code,
  };
}

function reject(evidence, code) {
  return {
    accepted: false,
    diagnostic: diagnostic(evidence && evidence.artifact ? evidence.artifact.id : null, code),
  };
}

function evaluateEvidence(evidence, target) {
  if (!isObject(evidence) || !isObject(evidence.artifact)) return reject(evidence, 'evidence-invalid');
  const { artifact, workflowRun, receipt } = evidence;
  if (!positiveInteger(artifact.id) || typeof artifact.name !== 'string') return reject(evidence, 'artifact-invalid');
  if (!positiveInteger(artifact.workflowRunId)) return reject(evidence, 'artifact-producer-run-unknown');
  if (artifact.expired === true) return reject(evidence, 'artifact-expired');
  if (artifact.expired !== false) return reject(evidence, 'artifact-expiry-unknown');
  const observedAt = Math.max(timestamp(artifact.updatedAt), timestamp(artifact.createdAt));
  if (observedAt === 0) return reject(evidence, 'artifact-time-invalid');

  if (!isObject(workflowRun) || !positiveInteger(workflowRun.id)) return reject(evidence, 'workflow-run-invalid');
  if (artifact.workflowRunId !== workflowRun.id) return reject(evidence, 'artifact-workflow-run-mismatch');
  // These identities must come from the collector's repository-scoped API reads,
  // not from self-declared fields inside the downloaded receipt.
  if (artifact.repository !== target.repository || workflowRun.repository !== target.repository) {
    return reject(evidence, 'repository-mismatch');
  }
  if (workflowRun.path !== AUTHORITY_WORKFLOW) return reject(evidence, 'workflow-path-mismatch');
  if (workflowRun.event !== AUTHORITY_EVENT) return reject(evidence, 'workflow-event-mismatch');
  if (workflowRun.status !== 'completed' || workflowRun.conclusion !== 'success') {
    return reject(evidence, 'workflow-conclusion-mismatch');
  }

  if (!isObject(receipt)) return reject(evidence, 'receipt-invalid');
  if (receipt.kind !== 'smart-ci-gate-receipt' || receipt.schemaVersion !== 1) {
    return reject(evidence, 'receipt-kind-mismatch');
  }
  if (receipt.ok !== true || receipt.wouldFail !== false) return reject(evidence, 'receipt-not-green');
  if (!Array.isArray(receipt.failures) || receipt.failures.length !== 0) return reject(evidence, 'receipt-invalid');
  // Shadow receipts may be green after checking only the plan, with no lane results.
  if (receipt.mode !== 'enforce') return reject(evidence, 'receipt-not-enforced');
  if (!Array.isArray(receipt.selected) || receipt.selected.length === 0) {
    return reject(evidence, 'receipt-invalid');
  }
  if (!isObject(receipt.event)
    || receipt.event.name !== AUTHORITY_EVENT
    || receipt.event.repository !== target.repository
    || !positiveInteger(receipt.event.pullRequest)) {
    return reject(evidence, 'receipt-invalid');
  }
  if (receipt.event.pullRequest !== target.landing.pullRequest) {
    return reject(evidence, 'landing-pr-mismatch');
  }
  if (![receipt.baseSha, receipt.headSha, receipt.mergeSha, receipt.mergeTreeSha].every(validSha)) {
    return reject(evidence, 'receipt-invalid');
  }
  if (!validPolicyDigest(receipt.policyDigest)) return reject(evidence, 'receipt-invalid');

  const artifactMatch = RECEIPT_ARTIFACT.exec(artifact.name);
  if (!artifactMatch
    || Number(artifactMatch[1]) !== receipt.event.pullRequest
    || lower(artifactMatch[2]) !== lower(receipt.headSha)) {
    return reject(evidence, 'artifact-name-mismatch');
  }
  if (lower(receipt.policyDigest) !== lower(target.expectedPolicyDigest)) {
    return reject(evidence, 'policy-digest-mismatch');
  }
  if (lower(receipt.mergeTreeSha) !== lower(target.headTreeSha)) {
    return reject(evidence, 'tree-mismatch');
  }

  const identity = [
    receipt.event.pullRequest,
    lower(receipt.baseSha),
    lower(receipt.headSha),
    lower(receipt.mergeSha),
    lower(receipt.mergeTreeSha),
    lower(receipt.policyDigest),
  ].join(':');
  return {
    accepted: true,
    identity,
    observedAt,
    artifactId: artifact.id,
    receipt: {
      artifactId: artifact.id,
      artifactName: artifact.name,
      workflowRunId: workflowRun.id,
      pullRequest: receipt.event.pullRequest,
      baseSha: lower(receipt.baseSha),
      headSha: lower(receipt.headSha),
      mergeSha: lower(receipt.mergeSha),
      mergeTreeSha: lower(receipt.mergeTreeSha),
      policyDigest: lower(receipt.policyDigest),
      mode: receipt.mode,
    },
  };
}

function fullVerdict(target, reason, diagnostics = [], candidates = 0) {
  return {
    schemaVersion: 1,
    kind: 'smart-ci-landed-verdict',
    repository: validRepository(target.repository) ? target.repository : null,
    headSha: validSha(target.headSha) ? lower(target.headSha) : null,
    headTreeSha: validSha(target.headTreeSha) ? lower(target.headTreeSha) : null,
    expectedPolicyDigest: validPolicyDigest(target.expectedPolicyDigest)
      ? lower(target.expectedPolicyDigest)
      : null,
    landing: normaliseLanding(target.landing),
    qualification: 'full',
    reason,
    candidates,
    receipt: null,
    diagnostics,
  };
}

/**
 * Decide whether the landed commit may use the bounded verification path.
 *
 * Evidence is authoritative only when trusted landing classification identifies a
 * normal PR merge and the artifact/run repository identities, producer run,
 * producer workflow, event, successful conclusion, enforced receipt identity,
 * current policy digest, associated PR, and landed tree all agree. Missing or
 * conflicting facts fall back to full hosted qualification.
 */
export function decideLandedQualification({
  repository,
  headSha,
  headTreeSha,
  expectedPolicyDigest,
  landing = null,
  evidence = [],
}) {
  const normalisedLanding = normaliseLanding(landing);
  const target = {
    repository,
    headSha,
    headTreeSha,
    expectedPolicyDigest,
    landing: normalisedLanding,
  };
  if (!validRepository(repository)
    || !validSha(headSha)
    || !validSha(headTreeSha)
    || !validPolicyDigest(expectedPolicyDigest)
    || !Array.isArray(evidence)) {
    return fullVerdict(target, 'verifier-input-invalid');
  }
  if (!normalisedLanding) return fullVerdict(target, 'landing-unverified');
  if (normalisedLanding.kind === 'direct-push') return fullVerdict(target, 'direct-push');

  const diagnostics = [];
  const accepted = [];
  for (const item of evidence) {
    const result = evaluateEvidence(item, target);
    if (result.accepted) accepted.push(result);
    else diagnostics.push(result.diagnostic);
  }

  if (accepted.length === 0) return fullVerdict(target, 'no-qualified-receipt', diagnostics);

  const identities = new Map();
  for (const candidate of accepted) {
    const existing = identities.get(candidate.identity);
    if (!existing
      || candidate.observedAt > existing.observedAt
      || (candidate.observedAt === existing.observedAt && candidate.artifactId > existing.artifactId)) {
      identities.set(candidate.identity, candidate);
    }
  }
  if (identities.size !== 1) {
    return fullVerdict(target, 'ambiguous-qualified-receipts', diagnostics, identities.size);
  }

  const [selected] = identities.values();
  return {
    schemaVersion: 1,
    kind: 'smart-ci-landed-verdict',
    repository,
    headSha: lower(headSha),
    headTreeSha: lower(headTreeSha),
    expectedPolicyDigest: lower(expectedPolicyDigest),
    landing: normalisedLanding,
    qualification: 'bounded',
    reason: 'qualified-receipt',
    candidates: 1,
    receipt: selected.receipt,
    diagnostics,
  };
}

function defaultArgs() {
  return {
    repo: process.env.GITHUB_REPOSITORY ?? null,
    headSha: process.env.GITHUB_SHA ?? null,
    headTreeSha: null,
    policy: 'ci/policy.v1.json',
    input: null,
    landingKind: null,
    landingPr: null,
    out: 'artifacts/landed-verdict.json',
    summary: null,
    githubOutput: process.env.GITHUB_OUTPUT ?? null,
  };
}

function parseArgs(argv, args) {
  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index];
    const next = () => {
      index += 1;
      if (index >= argv.length || argv[index].startsWith('--')) throw new Error('Missing argument value');
      return argv[index];
    };
    switch (arg) {
      case '--repo': args.repo = next(); break;
      case '--head-sha': args.headSha = next(); break;
      case '--head-tree-sha': args.headTreeSha = next(); break;
      case '--policy': args.policy = next(); break;
      case '--input': args.input = next(); break;
      case '--landing-kind': args.landingKind = next(); break;
      case '--landing-pr': {
        const value = Number(next());
        if (!positiveInteger(value)) throw new Error('--landing-pr must be a positive integer');
        args.landingPr = value;
        break;
      }
      case '--out': next(); break; // Pre-scanned for the failure verdict as well.
      case '--summary': args.summary = next(); break;
      case '--github-output': next(); break; // Pre-scanned so even an earlier parse error can deny bounded work.
      case '--help':
        args.help = true;
        return args;
      default: throw new Error(`Unknown argument: ${arg}`);
    }
  }
  if (args.landingKind !== null && !['pull-request', 'direct-push'].includes(args.landingKind)) {
    throw new Error('--landing-kind must be pull-request or direct-push');
  }
  if (args.landingKind === 'pull-request' && !positiveInteger(args.landingPr)) {
    throw new Error('--landing-pr is required for pull-request landings');
  }
  if (args.landingKind === 'direct-push' && args.landingPr !== null) {
    throw new Error('--landing-pr must be omitted for direct-push landings');
  }
  return args;
}

function renderSummary(verdict) {
  const lines = [
    '## Smart CI landed verifier',
    '',
    `- Qualification: **${verdict.qualification}**`,
    `- Reason: \`${verdict.reason}\``,
    `- Landing: \`${verdict.landing ? verdict.landing.kind : 'unverified'}\`${verdict.landing && verdict.landing.pullRequest ? ` (PR #${verdict.landing.pullRequest})` : ''}`,
    `- Landed commit: \`${verdict.headSha ?? 'invalid'}\``,
    `- Landed tree: \`${verdict.headTreeSha ?? 'invalid'}\``,
    `- Qualified receipt: ${verdict.receipt ? `artifact \`${verdict.receipt.artifactId}\`, PR #${verdict.receipt.pullRequest}` : 'none'}`,
    `- Rejected evidence objects: ${verdict.diagnostics.length}`,
    '',
  ];
  return `${lines.join('\n')}\n`;
}

function writeJson(path, value) {
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, `${JSON.stringify(value, null, 2)}\n`);
}

function appendOutputs(path, verdict) {
  if (!path) return;
  mkdirSync(dirname(path), { recursive: true });
  // One append, with the affirmative flag last: partial writes cannot grant it early.
  appendFileSync(path, [
    `qualification=${verdict.qualification}`,
    `reason=${verdict.reason}`,
    `landing_kind=${verdict.landing ? verdict.landing.kind : ''}`,
    `landing_pr=${verdict.landing && verdict.landing.pullRequest ? verdict.landing.pullRequest : ''}`,
    `receipt_artifact_id=${verdict.receipt ? verdict.receipt.artifactId : ''}`,
    `receipt_workflow_run_id=${verdict.receipt ? verdict.receipt.workflowRunId : ''}`,
    `bounded=${verdict.qualification === 'bounded' ? 'true' : 'false'}`,
    '',
  ].join('\n'));
}

function resolveTreeSha(explicit, headSha) {
  if (explicit) return explicit;
  if (typeof headSha !== 'string' || !validSha(headSha)) return null;
  try {
    const options = { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'], timeout: 10_000 };
    // ^{tree} also peels trees and tags. Only the exact supplied commit is eligible.
    if (execFileSync('git', ['cat-file', '-t', headSha], options).trim() !== 'commit') return null;
    return execFileSync('git', ['rev-parse', `${headSha}^{tree}`], options).trim();
  } catch {
    return null;
  }
}

function evaluateInputs(args) {
  let expectedPolicyDigest = null;
  let evidence = [];
  const inputDiagnostics = [];
  try {
    expectedPolicyDigest = policyDigest(readFileSync(args.policy, 'utf8'));
  } catch {
    inputDiagnostics.push(diagnostic(null, 'policy-input-unavailable'));
  }
  try {
    if (args.input && existsSync(args.input)) {
      const parsed = JSON.parse(readFileSync(args.input, 'utf8'));
      if (Array.isArray(parsed)) evidence = parsed;
      else inputDiagnostics.push(diagnostic(null, 'evidence-input-invalid'));
    } else {
      inputDiagnostics.push(diagnostic(null, 'evidence-input-unavailable'));
    }
  } catch {
    inputDiagnostics.push(diagnostic(null, 'evidence-input-invalid'));
  }

  const landing = args.landingKind === null
    ? null
    : { kind: args.landingKind, pullRequest: args.landingPr };
  const verdict = decideLandedQualification({
    repository: args.repo,
    headSha: args.headSha,
    headTreeSha: resolveTreeSha(args.headTreeSha, args.headSha),
    expectedPolicyDigest,
    landing,
    evidence,
  });
  if (inputDiagnostics.length > 0) {
    verdict.qualification = 'full';
    verdict.reason = 'verifier-input-unavailable';
    verdict.receipt = null;
    verdict.candidates = 0;
    verdict.diagnostics = [...inputDiagnostics, ...verdict.diagnostics];
  }

  return verdict;
}

function pathOptionBeforeParsing(argv, option, fallback) {
  // This limited scan only locates denial output destinations. It grants no authority and
  // does not treat malformed arguments as valid CLI input.
  let path = fallback;
  for (let index = 0; index < argv.length - 1; index += 1) {
    if (argv[index] === option && !argv[index + 1].startsWith('--')) {
      path = argv[index + 1];
      index += 1;
    }
  }
  return path;
}

function failureVerdict(args) {
  return fullVerdict({ repository: args.repo, headSha: args.headSha }, 'verifier-error');
}

function main() {
  const argv = process.argv.slice(2);
  const args = defaultArgs();
  args.githubOutput = pathOptionBeforeParsing(argv, '--github-output', args.githubOutput);
  args.out = pathOptionBeforeParsing(argv, '--out', args.out);
  let initialOutputFailed = false;
  try {
    appendOutputs(args.githubOutput, failureVerdict(args));
  } catch {
    // Still parse paths so the error handler can persist a full verdict at --out.
    initialOutputFailed = true;
  }

  try {
    parseArgs(argv, args);
    if (initialOutputFailed) throw new Error('Output channel unavailable');
    if (args.help) {
      writeSync(1, 'usage: landed-verifier.mjs [--repo owner/name] --head-sha <sha> [--head-tree-sha <sha>] --policy <file> --input <evidence.json> --landing-kind pull-request|direct-push [--landing-pr N] [--out <verdict.json>] [--summary <file>] [--github-output <file>]\n');
      return;
    }
    const verdict = evaluateInputs(args);
    writeJson(args.out, verdict);
    const summary = renderSummary(verdict);
    if (args.summary) {
      mkdirSync(dirname(args.summary), { recursive: true });
      appendFileSync(args.summary, summary);
    }
    writeSync(1, summary);
    // The safe default precedes all reporting; publish true only after it succeeds.
    appendOutputs(args.githubOutput, verdict);
  } catch {
    const verdict = failureVerdict(args);
    // Either sink can itself be unavailable. Never print paths or raw input here.
    try { writeJson(args.out, verdict); } catch { /* Unwritable verdict sink. */ }
    try { appendOutputs(args.githubOutput, verdict); } catch { /* Unwritable output sink. */ }
    try { writeSync(2, 'Smart CI landed verifier failed; full qualification is required.\n'); } catch { /* Closed stderr. */ }
    process.exitCode = 1;
  }
}

function isMainModule() {
  if (!process.argv[1]) return false;
  try {
    return realpathSync(process.argv[1]) === realpathSync(fileURLToPath(import.meta.url));
  } catch {
    return false;
  }
}

if (isMainModule()) main();
