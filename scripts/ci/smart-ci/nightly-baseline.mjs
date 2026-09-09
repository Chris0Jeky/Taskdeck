#!/usr/bin/env node
// CI-10 #2334: hosted adapter for the pure nightly coordinator.
//
// A baseline is complete only when authenticated Actions metadata proves that CI Nightly and
// Nightly Quality Signals both completed successfully on the same `main` head, every required
// real job completed successfully on the latest attempt, and the CI Nightly run published a
// small plan receipt bound to that head and its Git tree. Missing or ambiguous evidence is an
// unavailable baseline; the caller then gives the pure coordinator no receipt/diff, which selects
// a full sweep. This adapter never turns the plan into job-level `if:` conditions.

import { execFileSync, spawnSync } from 'node:child_process';
import {
  appendFileSync,
  existsSync,
  lstatSync,
  mkdirSync,
  readFileSync,
  readdirSync,
  realpathSync,
  writeFileSync,
} from 'node:fs';
import { dirname, join, relative, resolve, sep } from 'node:path';
import { NIGHTLY_COORDINATOR_KIND, NIGHTLY_COORDINATOR_SCHEMA_VERSION, NIGHTLY_DEEP_SUITES } from './nightly-coordinator.mjs';

export const NIGHTLY_WORKFLOW = Object.freeze({
  file: 'ci-nightly.yml',
  path: '.github/workflows/ci-nightly.yml',
  name: 'CI Nightly',
});
export const QUALITY_WORKFLOW = Object.freeze({
  file: 'nightly-quality.yml',
  path: '.github/workflows/nightly-quality.yml',
  name: 'Nightly Quality Signals',
});
export const NIGHTLY_PLAN_ARTIFACT = 'nightly-coordinator-plan';
export const MAX_ARTIFACT_BYTES = 1024 * 1024;
export const MAX_RUN_PAGES = 2;
export const MAX_JOB_PAGES = 2;
export const MAX_CANDIDATES = 14;
export const NIGHTLY_SUITE_IDS = Object.freeze([
  'openapi-guardrail',
  'developer-portal',
  'backend-solution',
  'e2e-smoke',
  'load-concurrency-harness',
  'performance-regression-gate',
  'e2e-cross-browser',
  'container-images',
  'sast-scanning',
]);
export const QUALITY_SUITE_IDS = Object.freeze([
  'backend-coverage',
  'frontend-coverage',
  'dependency-security-signals',
]);

// These are the actual check-job names returned by the Actions jobs API. The mapping is kept by
// suite so a workflow/matrix change cannot quietly satisfy "all jobs green" while omitting a deep
// suite. The workflow contract test derives the outer names and browser matrix from checked-in YAML.
export const REQUIRED_REAL_JOBS = Object.freeze({
  'openapi-guardrail': Object.freeze(['OpenAPI Guardrail / OpenAPI Guardrail']),
  'developer-portal': Object.freeze(['Developer Portal / Generate Developer Portal']),
  'backend-solution': Object.freeze(['Backend Solution Regression / Backend Solution Regression']),
  'e2e-smoke': Object.freeze(['E2E Smoke / E2E Smoke']),
  'load-concurrency-harness': Object.freeze(['Load and Concurrency Harness / Load and Concurrency Harness']),
  'performance-regression-gate': Object.freeze(['Performance Regression Gate / Performance Regression Gate']),
  'e2e-cross-browser': Object.freeze([
    'E2E Cross-Browser Matrix / E2E (chromium)',
    'E2E Cross-Browser Matrix / E2E (firefox)',
    'E2E Cross-Browser Matrix / E2E (webkit)',
    'E2E Cross-Browser Matrix / E2E (mobile-chrome)',
    'E2E Cross-Browser Matrix / E2E (mobile-safari)',
  ]),
  'container-images': Object.freeze(['Container Images Regression / Container Images']),
  'sast-scanning': Object.freeze(['SAST Scanning (Semgrep) / SAST Scan (Semgrep)']),
  'backend-coverage': Object.freeze(['Backend Coverage (Domain + Application)']),
  'frontend-coverage': Object.freeze(['Frontend Coverage']),
  'dependency-security-signals': Object.freeze([
    'Dependency and Security Signals (Non-blocking) / Dependency Security Signals',
  ]),
});

const OBSERVER_JOB_NAME = 'Nightly coordinator observation';
const ALLOWED_EVENTS = Object.freeze(['schedule', 'workflow_dispatch']);
const API = 'https://api.github.com';

export class BaselineUnavailable extends Error {
  constructor(code) {
    super(code);
    this.name = 'BaselineUnavailable';
    this.code = code;
  }
}

function unavailable(code) {
  throw new BaselineUnavailable(code);
}

function isObject(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

export function isSha(value) {
  return /^[0-9a-f]{40}$/i.test(String(value ?? ''));
}

export function isExplicitIsoTimestamp(value) {
  return typeof value === 'string'
    && /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/i.test(value)
    && !Number.isNaN(Date.parse(value));
}

function normaliseSha(value) {
  return isSha(value) ? String(value).toLowerCase() : null;
}

function validRepo(value) {
  if (typeof value !== 'string' || !/^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/.test(value)) return false;
  return value.split('/').every((part) => part !== '.' && part !== '..');
}

function writeJson(path, value) {
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, `${JSON.stringify(value, null, 2)}\n`);
}

function stableCode(error) {
  return error instanceof BaselineUnavailable ? error.code : 'adapter-error';
}

/** Validate immutable workflow identity and the main/same-repository trust boundary. */
export function workflowRunErrors(run, { repo, branch, workflow }) {
  const errors = [];
  if (!isObject(run) || !Number.isInteger(run.id) || run.id <= 0) errors.push('run-id-invalid');
  if (!isObject(run) || run.name !== workflow.name) errors.push('workflow-name-mismatch');
  if (!isObject(run) || run.path !== workflow.path) errors.push('workflow-path-mismatch');
  if (!isObject(run) || !ALLOWED_EVENTS.includes(run.event)) errors.push('workflow-event-invalid');
  if (!isObject(run) || run.head_branch !== branch) errors.push('head-branch-mismatch');
  if (!isObject(run) || run.repository?.full_name !== repo) errors.push('repository-mismatch');
  if (!isObject(run) || run.head_repository?.full_name !== repo) errors.push('head-repository-mismatch');
  if (!isObject(run) || run.status !== 'completed') errors.push('run-not-completed');
  if (!isObject(run) || run.conclusion !== 'success') errors.push('run-not-success');
  if (!isObject(run) || !isSha(run.head_sha)) errors.push('run-head-invalid');
  if (!isObject(run) || !Number.isInteger(run.run_attempt) || run.run_attempt < 1) errors.push('run-attempt-invalid');
  if (!isObject(run) || !isExplicitIsoTimestamp(run.updated_at)) errors.push('run-updated-at-invalid');
  return [...new Set(errors)].sort();
}

function requiredNamesForSuites(suites) {
  return suites.flatMap((suite) => REQUIRED_REAL_JOBS[suite] ?? []);
}

/** Require the canonical real-job set and success on every returned latest-attempt job. */
export function jobSetErrors(jobs, run, suites, { allowedExtraNames = [], requiredExtraNames = [] } = {}) {
  const errors = [];
  if (!Array.isArray(jobs) || jobs.length === 0) return ['jobs-empty'];
  const requiredNames = requiredNamesForSuites(suites);
  const allowed = new Set([...requiredNames, ...allowedExtraNames, ...requiredExtraNames]);
  const counts = new Map();
  for (const job of jobs) {
    const name = isObject(job) && typeof job.name === 'string' ? job.name : '';
    counts.set(name, (counts.get(name) ?? 0) + 1);
    if (!allowed.has(name)) errors.push(`unexpected-job:${name || 'unnamed'}`);
    if (!isObject(job) || job.run_id !== run.id) errors.push(`job-run-mismatch:${name || 'unnamed'}`);
    if (!isObject(job) || job.run_attempt !== run.run_attempt) errors.push(`job-attempt-mismatch:${name || 'unnamed'}`);
    if (!isObject(job) || normaliseSha(job.head_sha) !== normaliseSha(run.head_sha)) errors.push(`job-head-mismatch:${name || 'unnamed'}`);
    if (!isObject(job) || job.status !== 'completed') errors.push(`job-not-completed:${name || 'unnamed'}`);
    if (!isObject(job) || job.conclusion !== 'success') errors.push(`job-not-success:${name || 'unnamed'}`);
    if (!isObject(job) || !isExplicitIsoTimestamp(job.completed_at)) errors.push(`job-completed-at-invalid:${name || 'unnamed'}`);
  }
  for (const name of requiredNames) {
    if ((counts.get(name) ?? 0) === 0) errors.push(`required-job-missing:${name}`);
    if ((counts.get(name) ?? 0) > 1) errors.push(`required-job-duplicate:${name}`);
  }
  for (const name of allowedExtraNames) {
    if ((counts.get(name) ?? 0) > 1) errors.push(`extra-job-duplicate:${name}`);
  }
  for (const name of requiredExtraNames) {
    if ((counts.get(name) ?? 0) === 0) errors.push(`required-job-missing:${name}`);
    if ((counts.get(name) ?? 0) > 1) errors.push(`required-job-duplicate:${name}`);
  }
  return [...new Set(errors)].sort();
}

export function artifactErrors(artifact, run) {
  const errors = [];
  if (!isObject(artifact) || !Number.isInteger(artifact.id) || artifact.id <= 0) errors.push('artifact-id-invalid');
  if (!isObject(artifact) || artifact.name !== NIGHTLY_PLAN_ARTIFACT) errors.push('artifact-name-mismatch');
  if (!isObject(artifact) || artifact.expired !== false) errors.push('artifact-expired');
  if (!isObject(artifact) || !Number.isInteger(artifact.size_in_bytes)
    || artifact.size_in_bytes <= 0 || artifact.size_in_bytes > MAX_ARTIFACT_BYTES) errors.push('artifact-size-invalid');
  if (!isObject(artifact) || artifact.workflow_run?.id !== run.id) errors.push('artifact-run-mismatch');
  if (!isObject(artifact) || normaliseSha(artifact.workflow_run?.head_sha) !== normaliseSha(run.head_sha)) {
    errors.push('artifact-head-mismatch');
  }
  return errors.sort();
}

function pagePath(path, page) {
  return `${path}${path.includes('?') ? '&' : '?'}per_page=100&page=${page}`;
}

/** Read a bounded REST collection; truncation is an unavailable baseline, never partial truth. */
export async function paginate(requestJson, path, key, maxPages) {
  const items = [];
  let total = null;
  for (let page = 1; page <= maxPages; page += 1) {
    const payload = await requestJson(pagePath(path, page));
    if (!isObject(payload) || !Array.isArray(payload[key]) || !Number.isInteger(payload.total_count)) {
      unavailable('metadata-shape-invalid');
    }
    if (total === null) total = payload.total_count;
    if (payload.total_count !== total) unavailable('metadata-count-changed');
    items.push(...payload[key]);
    if (items.length > total) unavailable('metadata-count-invalid');
    const ids = items.map((item) => item?.id);
    if (ids.some((id) => !Number.isInteger(id) || id <= 0) || new Set(ids).size !== ids.length) {
      unavailable('metadata-id-invalid');
    }
    if (items.length >= total || payload[key].length < 100) return items;
  }
  unavailable('metadata-pagination-limit');
}

function sortRunsNewestFirst(runs) {
  return [...runs].sort((left, right) => Date.parse(right.updated_at ?? '') - Date.parse(left.updated_at ?? ''));
}

function completionTime(runs) {
  const timestamps = runs.map((run) => run.updated_at).filter(isExplicitIsoTimestamp);
  if (timestamps.length !== runs.length) unavailable('completion-time-invalid');
  return new Date(Math.max(...timestamps.map(Date.parse))).toISOString();
}

/**
 * Find the newest complete pair. Detailed metadata is loaded lazily and bounded to 14 candidate
 * nightly runs, which covers the artifact's 14-day retention window without unbounded API work.
 */
export async function selectQualifiedPair({
  repo,
  branch,
  currentRunId,
  nightlyRuns,
  qualityRuns,
  loadJobs,
  loadArtifacts,
  loadCommit,
}) {
  const qualitiesByHead = new Map();
  for (const run of sortRunsNewestFirst(qualityRuns)) {
    if (workflowRunErrors(run, { repo, branch, workflow: QUALITY_WORKFLOW }).length > 0) continue;
    const key = normaliseSha(run.head_sha);
    if (!qualitiesByHead.has(key)) qualitiesByHead.set(key, []);
    qualitiesByHead.get(key).push(run);
  }

  const candidates = sortRunsNewestFirst(nightlyRuns)
    .filter((run) => run.id !== currentRunId)
    .slice(0, MAX_CANDIDATES);
  for (const nightly of candidates) {
    if (workflowRunErrors(nightly, { repo, branch, workflow: NIGHTLY_WORKFLOW }).length > 0) continue;
    const qualityCandidates = qualitiesByHead.get(normaliseSha(nightly.head_sha)) ?? [];
    for (const quality of qualityCandidates) {
      const [nightlyJobs, qualityJobs, artifacts] = await Promise.all([
        loadJobs(nightly),
        loadJobs(quality),
        loadArtifacts(nightly),
      ]);
      if (jobSetErrors(nightlyJobs, nightly, NIGHTLY_SUITE_IDS, { requiredExtraNames: [OBSERVER_JOB_NAME] }).length > 0) continue;
      if (jobSetErrors(qualityJobs, quality, QUALITY_SUITE_IDS).length > 0) continue;
      const matchingArtifacts = artifacts.filter((artifact) => artifact?.name === NIGHTLY_PLAN_ARTIFACT);
      if (matchingArtifacts.length !== 1 || artifactErrors(matchingArtifacts[0], nightly).length > 0) continue;
      const commit = await loadCommit(nightly.head_sha);
      const treeSha = normaliseSha(commit?.tree?.sha);
      if (normaliseSha(commit?.sha) !== normaliseSha(nightly.head_sha) || treeSha === null) continue;
      return {
        available: true,
        repo,
        branch,
        headSha: normaliseSha(nightly.head_sha),
        treeSha,
        completedAtUtc: completionTime([nightly, quality]),
        nightlyRunId: nightly.id,
        nightlyRunAttempt: nightly.run_attempt,
        qualityRunId: quality.id,
        qualityRunAttempt: quality.run_attempt,
        artifactId: matchingArtifacts[0].id,
        artifactName: NIGHTLY_PLAN_ARTIFACT,
      };
    }
  }
  unavailable('no-complete-nightly-pair');
}

export function planReceiptErrors(receipt, selection) {
  const errors = [];
  if (!isObject(receipt) || receipt.schemaVersion !== NIGHTLY_COORDINATOR_SCHEMA_VERSION) errors.push('receipt-schema-invalid');
  if (!isObject(receipt) || receipt.kind !== NIGHTLY_COORDINATOR_KIND) errors.push('receipt-kind-invalid');
  if (!isObject(receipt) || normaliseSha(receipt.current?.headSha) !== selection.headSha) errors.push('receipt-head-mismatch');
  if (!isObject(receipt) || normaliseSha(receipt.current?.treeSha) !== selection.treeSha) errors.push('receipt-tree-mismatch');
  if (!isObject(receipt) || !isExplicitIsoTimestamp(receipt.generatedAtUtc)) errors.push('receipt-time-invalid');
  if (!isObject(receipt) || !['no-change', 'affected', 'weekly-full', 'full-sweep'].includes(receipt.verdict)) {
    errors.push('receipt-verdict-invalid');
  }
  if (!isObject(receipt) || !Array.isArray(receipt.reasons) || receipt.reasons.length === 0) errors.push('receipt-reasons-invalid');
  const selected = Array.isArray(receipt?.selectedSuites) ? receipt.selectedSuites : [];
  const skipped = Array.isArray(receipt?.skippedSuites)
    ? receipt.skippedSuites.map((entry) => entry?.suite)
    : [];
  const all = [...selected, ...skipped];
  if (selected.length !== new Set(selected).size || skipped.length !== new Set(skipped).size) errors.push('receipt-suite-duplicate');
  if (all.length !== NIGHTLY_DEEP_SUITES.length
    || new Set(all).size !== NIGHTLY_DEEP_SUITES.length
    || NIGHTLY_DEEP_SUITES.some((suite) => !all.includes(suite))) errors.push('receipt-suite-coverage-invalid');
  return [...new Set(errors)].sort();
}

/** Read only the exact expected small JSON file from the extracted artifact directory. */
export function readPlanArtifact(artifactDir, selection) {
  const root = realpathSync(artifactDir);
  const entries = readdirSync(root);
  if (entries.length !== 1 || entries[0] !== 'nightly-plan.json') unavailable('artifact-files-invalid');
  const receiptPath = join(root, 'nightly-plan.json');
  const file = lstatSync(receiptPath);
  if (!file.isFile() || file.isSymbolicLink() || file.size <= 0 || file.size > MAX_ARTIFACT_BYTES) {
    unavailable('artifact-file-invalid');
  }
  const actual = realpathSync(receiptPath);
  const relativePath = relative(root, actual);
  if (relativePath.startsWith(`..${sep}`) || relativePath === '..' || resolve(actual) !== resolve(root, 'nightly-plan.json')) {
    unavailable('artifact-path-invalid');
  }
  let receipt;
  try {
    receipt = JSON.parse(readFileSync(actual, 'utf8'));
  } catch {
    unavailable('artifact-json-invalid');
  }
  const errors = planReceiptErrors(receipt, selection);
  if (errors.length > 0) unavailable(errors[0]);
  return receipt;
}

function defaultGit(workspace, args) {
  const result = spawnSync('git', args, { cwd: workspace, encoding: 'utf8', windowsHide: true });
  return { status: result.status, stdout: result.stdout ?? '' };
}

export function collectCurrentGitIdentity({ workspace, expectedCurrentHead, git = defaultGit }) {
  const headResult = git(workspace, ['rev-parse', 'HEAD']);
  const treeResult = git(workspace, ['rev-parse', 'HEAD^{tree}']);
  const headSha = normaliseSha(headResult.stdout.trim());
  const treeSha = normaliseSha(treeResult.stdout.trim());
  if (headResult.status !== 0 || treeResult.status !== 0 || headSha === null || treeSha === null) {
    unavailable('current-git-identity-invalid');
  }
  if (headSha !== normaliseSha(expectedCurrentHead)) unavailable('current-head-mismatch');
  return { headSha, treeSha };
}

/** Bind the hosted checkout and diff to the accepted run/commit metadata. */
export function collectGitEvidence({ workspace, expectedCurrentHead, baseline, git = defaultGit }) {
  const run = (args) => git(workspace, args);
  const { headSha, treeSha } = collectCurrentGitIdentity({ workspace, expectedCurrentHead, git });

  const baselineTreeResult = run(['rev-parse', `${baseline.headSha}^{tree}`]);
  if (baselineTreeResult.status !== 0 || normaliseSha(baselineTreeResult.stdout.trim()) !== baseline.treeSha) {
    unavailable('baseline-tree-unreachable');
  }
  const ancestor = run(['merge-base', '--is-ancestor', baseline.headSha, headSha]);
  if (ancestor.status !== 0) unavailable('baseline-head-not-ancestor');
  const diffResult = run(['diff', '--name-status', '--find-renames', baseline.headSha, headSha]);
  if (diffResult.status !== 0) unavailable('baseline-diff-unavailable');
  const changedFilesText = diffResult.stdout;
  const hasChanges = changedFilesText.trim().length > 0;
  if (!hasChanges && treeSha !== baseline.treeSha) unavailable('empty-diff-tree-mismatch');
  if (hasChanges && treeSha === baseline.treeSha) unavailable('nonempty-diff-identical-tree');
  return { headSha, treeSha, changedFilesText };
}

function resolveToken() {
  const token = process.env.GH_TOKEN || process.env.GITHUB_TOKEN;
  if (!token) unavailable('token-missing');
  return token;
}

function createRequestJson(token) {
  return async (path) => {
    const response = await fetch(`${API}${path}`, {
      headers: {
        Accept: 'application/vnd.github+json',
        Authorization: `Bearer ${token}`,
        'X-GitHub-Api-Version': '2022-11-28',
      },
    });
    if (!response.ok) unavailable(`metadata-http-${response.status}`);
    try {
      return await response.json();
    } catch {
      unavailable('metadata-json-invalid');
    }
  };
}

export async function resolveFromGitHub({ repo, branch, currentRunId, requestJson }) {
  const encodedRepo = repo.split('/').map(encodeURIComponent).join('/');
  const runPath = (workflow) => `/repos/${encodedRepo}/actions/workflows/${encodeURIComponent(workflow.file)}`
    + `/runs?branch=${encodeURIComponent(branch)}&status=success`;
  const [nightlyRuns, qualityRuns] = await Promise.all([
    paginate(requestJson, runPath(NIGHTLY_WORKFLOW), 'workflow_runs', MAX_RUN_PAGES),
    paginate(requestJson, runPath(QUALITY_WORKFLOW), 'workflow_runs', MAX_RUN_PAGES),
  ]);
  const loadJobs = async (run) => paginate(
    requestJson,
    `/repos/${encodedRepo}/actions/runs/${run.id}/jobs?filter=latest`,
    'jobs',
    MAX_JOB_PAGES,
  );
  const loadArtifacts = async (run) => paginate(
    requestJson,
    `/repos/${encodedRepo}/actions/runs/${run.id}/artifacts`,
    'artifacts',
    1,
  );
  const loadCommit = (sha) => requestJson(`/repos/${encodedRepo}/git/commits/${sha}`);
  return selectQualifiedPair({
    repo, branch, currentRunId, nightlyRuns, qualityRuns, loadJobs, loadArtifacts, loadCommit,
  });
}

function downloadArtifact(selection, repo, artifactDir, token) {
  if (existsSync(artifactDir)) unavailable('artifact-directory-exists');
  mkdirSync(dirname(artifactDir), { recursive: true });
  try {
    execFileSync('gh', [
      'run', 'download', String(selection.nightlyRunId),
      '--repo', repo,
      '--name', selection.artifactName,
      '--dir', artifactDir,
    ], {
      encoding: 'utf8',
      env: { ...process.env, GH_TOKEN: token },
      stdio: ['ignore', 'pipe', 'pipe'],
      windowsHide: true,
    });
  } catch {
    unavailable('artifact-download-failed');
  }
}

export function parseArgs(argv) {
  const args = {
    repo: null,
    branch: 'main',
    currentRunId: null,
    currentHead: null,
    currentRef: null,
    currentEvent: null,
    currentWorkflow: null,
    workspace: null,
    artifactDir: null,
    lastReceipt: null,
    changedFiles: null,
    evidence: null,
    summary: null,
    githubOutput: null,
  };
  for (let index = 0; index < argv.length; index += 1) {
    const flag = argv[index];
    const next = () => argv[++index];
    switch (flag) {
      case '--repo': args.repo = next(); break;
      case '--branch': args.branch = next(); break;
      case '--current-run-id': args.currentRunId = Number(next()); break;
      case '--current-head': args.currentHead = next(); break;
      case '--current-ref': args.currentRef = next(); break;
      case '--current-event': args.currentEvent = next(); break;
      case '--current-workflow': args.currentWorkflow = next(); break;
      case '--workspace': args.workspace = next(); break;
      case '--artifact-dir': args.artifactDir = next(); break;
      case '--last-receipt': args.lastReceipt = next(); break;
      case '--changed-files': args.changedFiles = next(); break;
      case '--evidence': args.evidence = next(); break;
      case '--summary': args.summary = next(); break;
      case '--github-output': args.githubOutput = next(); break;
      default: throw new Error(`Unknown argument: ${flag}`);
    }
  }
  return args;
}

function validateCliContext(args) {
  if (!validRepo(args.repo)) unavailable('current-repository-invalid');
  if (args.branch !== 'main') unavailable('current-branch-invalid');
  if (!Number.isInteger(args.currentRunId) || args.currentRunId <= 0) unavailable('current-run-id-invalid');
  if (!isSha(args.currentHead)) unavailable('current-context-head-invalid');
  if (args.currentRef !== `refs/heads/${args.branch}`) unavailable('current-ref-mismatch');
  if (!ALLOWED_EVENTS.includes(args.currentEvent)) unavailable('current-event-invalid');
  if (args.currentWorkflow !== NIGHTLY_WORKFLOW.name) unavailable('current-workflow-mismatch');
  for (const path of [args.workspace, args.artifactDir, args.lastReceipt, args.changedFiles, args.evidence]) {
    if (typeof path !== 'string' || path.length === 0) unavailable('output-path-missing');
  }
}

function renderObservationSummary(evidence) {
  const lines = [
    '# Nightly baseline observation',
    '',
    `- Baseline: **${evidence.status}**`,
    `- Reason: \`${evidence.reason}\``,
  ];
  if (evidence.baseline) {
    lines.push(
      `- Qualified head: \`${evidence.baseline.headSha.slice(0, 12)}\``,
      `- CI Nightly run: \`${evidence.baseline.nightlyRunId}\``,
      `- Nightly Quality run: \`${evidence.baseline.qualityRunId}\``,
    );
  }
  return `${lines.join('\n')}\n\n`;
}

export async function runObservation(args) {
  const generatedAtUtc = new Date().toISOString();
  let current = { headSha: normaliseSha(args.currentHead), treeSha: null };
  let selection = null;
  let reason = 'complete-pair-accepted';
  try {
    validateCliContext(args);
    current = collectCurrentGitIdentity({
      workspace: args.workspace,
      expectedCurrentHead: args.currentHead,
    });
    const token = resolveToken();
    selection = await resolveFromGitHub({
      repo: args.repo,
      branch: args.branch,
      currentRunId: args.currentRunId,
      requestJson: createRequestJson(token),
    });
    downloadArtifact(selection, args.repo, args.artifactDir, token);
    readPlanArtifact(args.artifactDir, selection);
    const gitEvidence = collectGitEvidence({
      workspace: args.workspace,
      expectedCurrentHead: args.currentHead,
      baseline: selection,
    });
    current = { headSha: gitEvidence.headSha, treeSha: gitEvidence.treeSha };
    writeJson(args.lastReceipt, {
      headSha: selection.headSha,
      treeSha: selection.treeSha,
      completedAtUtc: selection.completedAtUtc,
      complete: true,
    });
    mkdirSync(dirname(args.changedFiles), { recursive: true });
    writeFileSync(args.changedFiles, gitEvidence.changedFilesText);
  } catch (error) {
    reason = stableCode(error);
  }
  const available = reason === 'complete-pair-accepted';
  const evidence = {
    schemaVersion: 1,
    kind: 'nightly-baseline-observation',
    generatedAtUtc,
    status: available ? 'available' : 'unavailable',
    reason,
    current,
    baseline: available ? selection : null,
  };
  writeJson(args.evidence, evidence);
  if (args.summary) appendFileSync(args.summary, renderObservationSummary(evidence));
  if (args.githubOutput) {
    appendFileSync(args.githubOutput, `available=${available}\n`);
    appendFileSync(args.githubOutput, `head_sha=${current.headSha ?? ''}\n`);
    appendFileSync(args.githubOutput, `tree_sha=${current.treeSha ?? ''}\n`);
    appendFileSync(args.githubOutput, `generated_at_utc=${generatedAtUtc}\n`);
  }
  return evidence;
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  const evidence = await runObservation(args);
  process.stdout.write(`${JSON.stringify(evidence, null, 2)}\n`);
}

if (process.argv[1] && /nightly-baseline\.mjs$/.test(process.argv[1])) {
  main().catch(() => { process.exitCode = 1; });
}
