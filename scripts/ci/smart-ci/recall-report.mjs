#!/usr/bin/env node
// CI-02 (#2326, ADR-0066): compare exact-head Smart CI shadow plans with
// failures from the existing full required workflow. The report is read-only
// and fail-closed: missing, ambiguous or mismatched evidence is never counted
// as a successful observation.

import { execFileSync } from 'node:child_process';
import {
  existsSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  readdirSync,
  rmSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { policyDigest, validatePlan, validatePolicy } from './lib/plan.mjs';

const API = 'https://api.github.com';
const SHADOW_WORKFLOW_PATH = '.github/workflows/smart-ci-shadow.yml';
const REQUIRED_WORKFLOW_PATH = '.github/workflows/ci-required.yml';
export const MINIMUM_PR_OBSERVATIONS = 20;
const RUN_CORRELATION_WINDOW_MS = 5 * 60 * 1000;
const NON_FAILURE_CONCLUSIONS = new Set(['success', 'skipped']);
const FAILURE_CONCLUSIONS = new Set([
  'failure',
  'cancelled',
  'timed_out',
  'action_required',
  'startup_failure',
  'stale',
]);
const REQUIRED_RUN_CONCLUSIONS = new Set([
  'success',
  'failure',
  'timed_out',
  'action_required',
  'startup_failure',
  'stale',
]);

function isObject(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function uniqueSorted(values) {
  return [...new Set(values)].sort((a, b) => String(a).localeCompare(String(b)));
}

function validSha(value) {
  return /^[0-9a-f]{40}$/i.test(String(value ?? ''));
}

function validTimestamp(value) {
  return typeof value === 'string' && !Number.isNaN(Date.parse(value));
}

function normaliseTimestamp(value, optionName) {
  if (!validTimestamp(value)) throw new Error(`${optionName} must be an ISO-8601 timestamp`);
  return new Date(value).toISOString();
}

function safeError(error) {
  return String(error && error.message ? error.message : error)
    .replace(/[\r\n]+/g, ' ')
    .slice(0, 300);
}

export function parseArgs(argv) {
  const args = {
    repo: 'Chris0Jeky/Taskdeck',
    since: null,
    until: null,
    minimumObservations: 20,
    policy: 'ci/policy.v1.json',
    input: null,
    outJson: null,
    outMarkdown: null,
    maxPages: 20,
  };
  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index];
    const next = () => {
      index += 1;
      if (index >= argv.length) throw new Error(`${arg} requires a value`);
      return argv[index];
    };
    switch (arg) {
      case '--repo': args.repo = next(); break;
      case '--since': args.since = next(); break;
      case '--until': args.until = next(); break;
      case '--min-observations': args.minimumObservations = Number(next()); break;
      case '--policy': args.policy = next(); break;
      case '--input': args.input = next(); break;
      case '--out-json': args.outJson = next(); break;
      case '--out-md': args.outMarkdown = next(); break;
      case '--max-pages': args.maxPages = Number(next()); break;
      case '--help':
        console.log('usage: recall-report.mjs --since <ISO timestamp> --until <ISO timestamp> [--repo owner/name] [--min-observations 20] [--policy ci/policy.v1.json] [--input observations.json] [--out-json report.json] [--out-md report.md]');
        return { ...args, help: true };
      default: throw new Error(`Unknown argument: ${arg}`);
    }
  }
  if (!/^[\w.-]+\/[\w.-]+$/.test(args.repo)) throw new Error('--repo must be owner/name');
  if (!Number.isInteger(args.minimumObservations) || args.minimumObservations < MINIMUM_PR_OBSERVATIONS) throw new Error(`--min-observations must be an integer >= ${MINIMUM_PR_OBSERVATIONS}`);
  if (!Number.isInteger(args.maxPages) || args.maxPages < 1) throw new Error('--max-pages must be a positive integer');
  args.since = normaliseTimestamp(args.since, '--since');
  args.until = normaliseTimestamp(args.until, '--until');
  if (Date.parse(args.since) > Date.parse(args.until)) throw new Error('--since must not be after --until');
  return args;
}

function resolveToken() {
  const fromEnv = process.env.GH_TOKEN || process.env.GITHUB_TOKEN;
  if (fromEnv) return fromEnv;
  try {
    return execFileSync('gh', ['auth', 'token'], { encoding: 'utf8' }).trim();
  } catch {
    throw new Error('No token: set GH_TOKEN/GITHUB_TOKEN or authenticate `gh`');
  }
}

async function sleep(milliseconds) {
  await new Promise((resolvePromise) => setTimeout(resolvePromise, milliseconds));
}

async function ghFetch(token, url) {
  for (let attempt = 0; attempt < 6; attempt += 1) {
    const response = await fetch(url, {
      headers: {
        Accept: 'application/vnd.github+json',
        Authorization: `Bearer ${token}`,
        'X-GitHub-Api-Version': '2022-11-28',
        'User-Agent': 'taskdeck-smart-ci-recall',
      },
    });
    if (response.status === 403 || response.status === 429) {
      const retryAfter = Number(response.headers.get('retry-after') ?? '0') * 1000;
      const remaining = Number(response.headers.get('x-ratelimit-remaining') ?? '1');
      const reset = Number(response.headers.get('x-ratelimit-reset') ?? '0') * 1000;
      const waitMs = retryAfter || (remaining === 0 && reset
        ? Math.max(1000, reset - Date.now() + 1000)
        : 15000 * (attempt + 1));
      console.error(`rate limited (${response.status}); waiting ${Math.round(waitMs / 1000)}s`);
      await sleep(Math.min(waitMs, 15 * 60 * 1000));
      continue;
    }
    if (response.status >= 500) {
      await sleep(2000 * (attempt + 1));
      continue;
    }
    if (!response.ok) throw new Error(`GitHub API ${response.status} for ${url}: ${await response.text()}`);
    return { json: await response.json(), link: response.headers.get('link') ?? '' };
  }
  throw new Error(`GitHub API gave up after retries: ${url}`);
}

export function nextLink(linkHeader) {
  const match = /<([^>]+)>;\s*rel="next"/.exec(linkHeader ?? '');
  return match ? match[1] : null;
}

async function paginate(token, initialUrl, itemsKey, maxPages) {
  const items = [];
  let url = initialUrl;
  let pages = 0;
  while (url && pages < maxPages) {
    const { json, link } = await ghFetch(token, url);
    const pageItems = itemsKey === null ? json : json[itemsKey];
    if (!Array.isArray(pageItems)) throw new Error(`GitHub listing returned no ${itemsKey ?? 'array'} items: ${initialUrl}`);
    items.push(...pageItems);
    pages += 1;
    url = nextLink(link);
  }
  if (url) throw new Error(`GitHub listing exceeded --max-pages ${maxPages}: ${initialUrl}`);
  return items;
}

async function listMergedPullRequests(token, repo, since, until, maxPages) {
  const selected = [];
  let url = `${API}/repos/${repo}/pulls?state=closed&sort=updated&direction=desc&per_page=100`;
  let pages = 0;
  let reachedOlderItems = false;
  const sinceMs = Date.parse(since);
  const untilMs = Date.parse(until);
  while (url && pages < maxPages && !reachedOlderItems) {
    const { json, link } = await ghFetch(token, url);
    const pulls = Array.isArray(json) ? json : [];
    for (const pull of pulls) {
      const mergedMs = Date.parse(pull.merged_at ?? '');
      if (!Number.isNaN(mergedMs) && mergedMs >= sinceMs && mergedMs <= untilMs) {
        selected.push({
          prNumber: pull.number,
          mergedAt: new Date(mergedMs).toISOString(),
          finalHeadSha: pull.head && pull.head.sha ? pull.head.sha : null,
          headBranch: pull.head && pull.head.ref ? pull.head.ref : null,
          headRepository: pull.head && pull.head.repo ? pull.head.repo.full_name : null,
          baseSha: pull.base && pull.base.sha ? pull.base.sha : null,
          baseBranch: pull.base && pull.base.ref ? pull.base.ref : null,
          baseRepository: pull.base && pull.base.repo ? pull.base.repo.full_name : null,
          mergeCommitSha: pull.merge_commit_sha ?? null,
        });
      }
    }
    pages += 1;
    reachedOlderItems = pulls.length === 0 || pulls.every((pull) => Date.parse(pull.updated_at ?? '') < sinceMs);
    url = nextLink(link);
  }
  if (url && !reachedOlderItems) throw new Error(`pull request listing exceeded --max-pages ${maxPages}`);
  return selected.sort((a, b) => a.prNumber - b.prNumber);
}

function findNamedFiles(root, filename) {
  const found = [];
  const visit = (directory) => {
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      const path = join(directory, entry.name);
      if (entry.isDirectory()) visit(path);
      else if (entry.isFile() && entry.name === filename) found.push(path);
    }
  };
  visit(root);
  return found;
}

function downloadPlanArtifact(repo, runId, artifactName, destination) {
  mkdirSync(destination, { recursive: true });
  try {
    execFileSync('gh', ['run', 'download', String(runId), '--repo', repo, '--name', artifactName, '--dir', destination], {
      encoding: 'utf8',
      stdio: ['ignore', 'pipe', 'pipe'],
    });
  } catch (error) {
    const detail = error && error.stderr ? String(error.stderr) : safeError(error);
    throw new Error(`could not download plan artifact: ${detail.replace(/[\r\n]+/g, ' ').slice(0, 240)}`);
  }
  const plans = findNamedFiles(destination, 'ci-plan.json');
  if (plans.length !== 1) throw new Error(`plan artifact must contain exactly one ci-plan.json (found ${plans.length})`);
  return JSON.parse(readFileSync(plans[0], 'utf8'));
}

function runPullNumbers(run) {
  return uniqueSorted((Array.isArray(run && run.pull_requests) ? run.pull_requests : [])
    .map((pull) => Number(pull && pull.number))
    .filter((number) => Number.isInteger(number) && number > 0));
}

async function fetchGitCommit(token, repo, sha) {
  if (!validSha(sha)) throw new Error('commit lookup needs a valid SHA');
  const commit = (await ghFetch(token, `${API}/repos/${repo}/git/commits/${sha}`)).json;
  return {
    sha: commit.sha ?? null,
    treeSha: commit.tree && commit.tree.sha ? commit.tree.sha : null,
    parents: Array.isArray(commit.parents) ? commit.parents.map((parent) => parent.sha ?? null) : [],
  };
}

async function listAssociatedPullNumbers(token, repo, sha, maxPages) {
  const pulls = await paginate(
    token,
    `${API}/repos/${repo}/commits/${sha}/pulls?per_page=100`,
    null,
    maxPages,
  );
  return uniqueSorted(pulls
    .map((pull) => Number(pull && pull.number))
    .filter((number) => Number.isInteger(number) && number > 0));
}

async function collectPlanEvidence(token, repo, pull, headSha, tempRoot, maxPages) {
  const artifactName = `smart-ci-plan-${pull.prNumber}-${headSha}`;
  const artifactListing = await paginate(
    token,
    `${API}/repos/${repo}/actions/artifacts?name=${encodeURIComponent(artifactName)}&per_page=100`,
    'artifacts',
    maxPages,
  );
  const artifacts = artifactListing.filter((artifact) => artifact.name === artifactName && artifact.expired !== true);
  if (artifacts.length !== 1) throw new Error(`expected one unexpired ${artifactName} artifact, found ${artifacts.length}`);
  const artifact = artifacts[0];
  const runId = artifact.workflow_run && artifact.workflow_run.id;
  if (!Number.isInteger(runId)) throw new Error('plan artifact has no workflow run id');
  const shadowRun = (await ghFetch(token, `${API}/repos/${repo}/actions/runs/${runId}`)).json;
  const plan = downloadPlanArtifact(repo, runId, artifactName, join(tempRoot, `${pull.prNumber}-${headSha}`));
  const planMergeCommit = validSha(plan && plan.mergeSha)
    ? await fetchGitCommit(token, repo, plan.mergeSha)
    : null;
  return {
    artifact: {
      id: artifact.id,
      name: artifact.name,
      expired: artifact.expired === true,
      workflowRunId: runId,
      headSha: artifact.workflow_run ? artifact.workflow_run.head_sha : null,
      headBranch: artifact.workflow_run ? artifact.workflow_run.head_branch : null,
      createdAt: artifact.created_at,
      updatedAt: artifact.updated_at,
    },
    shadowRun: {
      id: shadowRun.id,
      path: shadowRun.path,
      event: shadowRun.event,
      status: shadowRun.status,
      conclusion: shadowRun.conclusion,
      headSha: shadowRun.head_sha,
      headBranch: shadowRun.head_branch,
      headRepository: shadowRun.head_repository ? shadowRun.head_repository.full_name : null,
      createdAt: shadowRun.created_at,
      updatedAt: shadowRun.updated_at,
      pullRequests: runPullNumbers(shadowRun),
    },
    plan,
    planMergeCommit,
  };
}

async function collectPullObservations(token, repo, pull, tempRoot, maxPages, since, until) {
  const baseRaw = { ...pull, repository: repo };
  try {
    if (!Number.isInteger(pull.prNumber) || !validSha(pull.finalHeadSha)) throw new Error('merged PR has no valid final head SHA');
    if (!validSha(pull.baseSha) || !validSha(pull.mergeCommitSha)) throw new Error('merged PR has no valid base/merge SHA binding');
    if (typeof pull.headBranch !== 'string' || pull.headBranch.length === 0 || typeof pull.headRepository !== 'string' || pull.headRepository.length === 0) throw new Error('merged PR has no usable head branch/repository binding');
    if (typeof pull.baseBranch !== 'string' || pull.baseBranch.length === 0 || pull.baseRepository !== repo) throw new Error('merged PR has no usable base branch/repository binding');

    baseRaw.mergeCommit = await fetchGitCommit(token, repo, pull.mergeCommitSha);
    const requiredRuns = await paginate(
      token,
      `${API}/repos/${repo}/actions/workflows/ci-required.yml/runs?event=pull_request&branch=${encodeURIComponent(pull.headBranch)}&per_page=100`,
      'workflow_runs',
      maxPages,
    );
    const mergedMs = Date.parse(pull.mergedAt);
    const sinceMs = Date.parse(since);
    const untilMs = Date.parse(until);
    const branchRuns = requiredRuns
      .filter((run) => run.path === REQUIRED_WORKFLOW_PATH
        && run.event === 'pull_request'
        && run.head_branch === pull.headBranch
        && run.head_repository && run.head_repository.full_name === pull.headRepository
        && Date.parse(run.created_at ?? '') <= Math.min(mergedMs, untilMs))
      .sort((a, b) => Date.parse(a.created_at ?? '') - Date.parse(b.created_at ?? '') || Number(a.id) - Number(b.id));
    const candidates = branchRuns.filter((run) => Date.parse(run.created_at ?? '') >= sinceMs);
    if (candidates.length === 0 && branchRuns.length > 0) return [];

    const planEvidence = new Map();
    const associatedPulls = new Map();
    const observations = [];
    for (const run of candidates) {
      const attempts = Number(run.run_attempt);
      if (!Number.isInteger(attempts) || attempts < 1 || !validSha(run.head_sha)) {
        observations.push({ ...baseRaw, headSha: run.head_sha ?? null, collectionError: 'required workflow run has invalid attempt/head metadata' });
        continue;
      }
      for (let attemptNumber = 1; attemptNumber <= attempts; attemptNumber += 1) {
        const raw = { ...baseRaw, headSha: run.head_sha };
        try {
          const attempt = (await ghFetch(token, `${API}/repos/${repo}/actions/runs/${run.id}/attempts/${attemptNumber}`)).json;
          if (attempt.status !== 'completed' || !REQUIRED_RUN_CONCLUSIONS.has(attempt.conclusion)) continue;
          const attemptUpdatedMs = Date.parse(attempt.updated_at ?? '');
          if (Number.isNaN(attemptUpdatedMs) || attemptUpdatedMs < sinceMs || attemptUpdatedMs > Math.min(mergedMs, untilMs)) continue;

          if (!planEvidence.has(run.head_sha)) {
            planEvidence.set(run.head_sha, collectPlanEvidence(token, repo, pull, run.head_sha, tempRoot, maxPages));
          }
          if (!associatedPulls.has(run.head_sha)) {
            associatedPulls.set(run.head_sha, listAssociatedPullNumbers(token, repo, run.head_sha, maxPages));
          }
          Object.assign(raw, await planEvidence.get(run.head_sha));
          raw.headPullRequests = await associatedPulls.get(run.head_sha);
          raw.requiredRun = {
            id: attempt.id,
            path: attempt.path,
            event: attempt.event,
            status: attempt.status,
            conclusion: attempt.conclusion,
            headSha: attempt.head_sha,
            headBranch: attempt.head_branch,
            headRepository: attempt.head_repository ? attempt.head_repository.full_name : null,
            triggerCreatedAt: run.created_at,
            createdAt: attempt.created_at,
            updatedAt: attempt.updated_at,
            runAttempt: attemptNumber,
            pullRequests: runPullNumbers(attempt),
          };
          raw.jobs = (await paginate(
            token,
            `${API}/repos/${repo}/actions/runs/${run.id}/attempts/${attemptNumber}/jobs?per_page=100`,
            'jobs',
            maxPages,
          )).map((job) => ({
            name: job.name,
            status: job.status,
            conclusion: job.conclusion,
            runId: job.run_id,
            runAttempt: job.run_attempt,
            headSha: job.head_sha,
          }));
        } catch (error) {
          raw.collectionError = safeError(error);
        }
        observations.push(raw);
      }
    }
    if (observations.length === 0) {
      observations.push({ ...baseRaw, collectionError: 'no completed measurable required workflow attempt exists in the observation window' });
    }
    return observations;
  } catch (error) {
    return [{ ...baseRaw, collectionError: safeError(error) }];
  }
}

export async function collectLiveObservations({ repo, since, until, maxPages = 20 }) {
  const token = resolveToken();
  const pulls = await listMergedPullRequests(token, repo, since, until, maxPages);
  const tempRoot = mkdtempSync(join(tmpdir(), 'taskdeck-smart-ci-recall-'));
  try {
    const observations = [];
    for (const pull of pulls) {
      console.error(`collecting PR #${pull.prNumber} at final head ${pull.finalHeadSha}`);
      observations.push(...await collectPullObservations(token, repo, pull, tempRoot, maxPages, since, until));
    }
    return observations;
  } finally {
    const resolvedTemp = resolve(tempRoot);
    const resolvedBase = resolve(tmpdir());
    if (!resolvedTemp.startsWith(`${resolvedBase}\\`) && !resolvedTemp.startsWith(`${resolvedBase}/`)) {
      throw new Error(`refusing to remove unexpected temporary path: ${resolvedTemp}`);
    }
    rmSync(resolvedTemp, { recursive: true, force: true });
  }
}

function laneByCheckName(policy) {
  return new Map(Object.entries(policy.lanes ?? {}).map(([lane, definition]) => [definition.checkName, {
    lane,
    checkName: definition.checkName,
    family: definition.family,
  }]));
}

export function normaliseObservation(raw, policy, options = {}) {
  const errors = [];
  const addError = (code) => errors.push(code);
  if (!isObject(raw)) raw = {};
  if (raw.collectionError) addError(`collection-error:${String(raw.collectionError).slice(0, 240)}`);

  const prNumber = Number(raw.prNumber);
  const mergedAt = raw.mergedAt;
  const headSha = raw.headSha;
  const finalHeadSha = raw.finalHeadSha;
  const baseSha = raw.baseSha;
  const mergeCommitSha = raw.mergeCommitSha;
  if (!Number.isInteger(prNumber) || prNumber < 1) addError('pr-number-invalid');
  if (!validTimestamp(mergedAt)) addError('merged-at-invalid');
  else if (validTimestamp(options.since) && validTimestamp(options.until)
    && (Date.parse(mergedAt) < Date.parse(options.since) || Date.parse(mergedAt) > Date.parse(options.until))) addError('merged-at-outside-window');
  if (!validSha(headSha)) addError('head-sha-invalid');
  if (!validSha(finalHeadSha)) addError('final-head-sha-invalid');
  if (!validSha(baseSha)) addError('base-sha-invalid');
  if (!validSha(mergeCommitSha)) addError('merge-commit-sha-invalid');
  if (options.repository && raw.repository !== options.repository) addError('repository-mismatch');
  if (typeof raw.headBranch !== 'string' || raw.headBranch.length === 0) addError('head-branch-invalid');
  if (typeof raw.headRepository !== 'string' || raw.headRepository.length === 0) addError('head-repository-invalid');
  if (typeof raw.baseBranch !== 'string' || raw.baseBranch.length === 0) addError('base-branch-invalid');
  if (options.repository && raw.baseRepository !== options.repository) addError('base-repository-mismatch');

  const validatePullBindings = (bindings, prefix) => {
    if (!Array.isArray(bindings) || bindings.some((number) => !Number.isInteger(number) || number < 1)) {
      addError(`${prefix}-pr-bindings-invalid`);
    } else if (bindings.length > 0 && !bindings.includes(prNumber)) {
      addError(`${prefix}-pr-mismatch`);
    }
  };
  validatePullBindings(raw.headPullRequests, 'head');

  const validateCommit = (commit, expectedSha, expectedParents, expectedTree, prefix) => {
    if (!isObject(commit)) {
      addError(`${prefix}-missing`);
      return;
    }
    if (commit.sha !== expectedSha) addError(`${prefix}-sha-mismatch`);
    if (!validSha(commit.treeSha)) addError(`${prefix}-tree-invalid`);
    if (!Array.isArray(commit.parents)
      || commit.parents.length !== expectedParents.length
      || commit.parents.some((parent, index) => parent !== expectedParents[index])) addError(`${prefix}-parents-mismatch`);
    if (expectedTree && commit.treeSha !== expectedTree) addError(`${prefix}-tree-mismatch`);
  };
  validateCommit(raw.mergeCommit, mergeCommitSha, [baseSha, finalHeadSha], null, 'merge-commit');

  const expectedArtifactName = `smart-ci-plan-${prNumber}-${headSha}`;
  if (!isObject(raw.artifact)) addError('plan-artifact-missing');
  else {
    if (!Number.isInteger(raw.artifact.id)) addError('plan-artifact-id-invalid');
    if (raw.artifact.name !== expectedArtifactName) addError('plan-artifact-name-mismatch');
    if (raw.artifact.expired === true) addError('plan-artifact-expired');
    else if (raw.artifact.expired !== false) addError('plan-artifact-expiry-invalid');
    if (raw.artifact.headSha !== headSha) addError('plan-artifact-head-sha-mismatch');
    if (raw.artifact.headBranch !== raw.headBranch) addError('plan-artifact-head-branch-mismatch');
    const artifactCreatedMs = Date.parse(raw.artifact.createdAt ?? '');
    const artifactUpdatedMs = Date.parse(raw.artifact.updatedAt ?? '');
    const mergedMs = Date.parse(mergedAt ?? '');
    if (Number.isNaN(artifactCreatedMs)
      || Number.isNaN(artifactUpdatedMs)
      || artifactUpdatedMs < artifactCreatedMs
      || (!Number.isNaN(mergedMs) && artifactUpdatedMs > mergedMs)) addError('plan-artifact-time-invalid');
    if (validTimestamp(options.since) && validTimestamp(options.until)
      && (artifactCreatedMs < Date.parse(options.since) || artifactUpdatedMs > Date.parse(options.until))) addError('plan-artifact-outside-window');
  }

  if (!isObject(raw.shadowRun)) addError('shadow-run-missing');
  else {
    if (!Number.isInteger(raw.shadowRun.id)) addError('shadow-run-id-invalid');
    if (raw.shadowRun.path !== SHADOW_WORKFLOW_PATH) addError('shadow-workflow-mismatch');
    if (raw.shadowRun.event !== 'pull_request_target') addError('shadow-event-mismatch');
    if (raw.shadowRun.status !== 'completed' || raw.shadowRun.conclusion !== 'success') addError('shadow-run-not-successful');
    if (raw.shadowRun.headSha !== headSha) addError('shadow-head-sha-mismatch');
    if (raw.shadowRun.headBranch !== raw.headBranch) addError('shadow-head-branch-mismatch');
    if (raw.shadowRun.headRepository !== raw.headRepository) addError('shadow-head-repository-mismatch');
    validatePullBindings(raw.shadowRun.pullRequests, 'shadow-run');
    const shadowCreatedMs = Date.parse(raw.shadowRun.createdAt ?? '');
    const shadowUpdatedMs = Date.parse(raw.shadowRun.updatedAt ?? '');
    const mergedMs = Date.parse(mergedAt ?? '');
    if (Number.isNaN(shadowCreatedMs)
      || Number.isNaN(shadowUpdatedMs)
      || shadowUpdatedMs < shadowCreatedMs
      || (!Number.isNaN(mergedMs) && (shadowCreatedMs > mergedMs || shadowUpdatedMs > mergedMs))) addError('shadow-run-after-merge');
    if (validTimestamp(options.since) && validTimestamp(options.until)
      && (shadowCreatedMs < Date.parse(options.since) || shadowUpdatedMs > Date.parse(options.until))) addError('shadow-run-outside-window');
    if (raw.artifact && raw.artifact.workflowRunId !== raw.shadowRun.id) addError('plan-artifact-run-mismatch');
    if (isObject(raw.artifact)) {
      const artifactCreatedMs = Date.parse(raw.artifact.createdAt ?? '');
      const artifactUpdatedMs = Date.parse(raw.artifact.updatedAt ?? '');
      if (!Number.isNaN(artifactCreatedMs) && !Number.isNaN(artifactUpdatedMs)
        && !Number.isNaN(shadowCreatedMs) && !Number.isNaN(shadowUpdatedMs)
        && (artifactCreatedMs < shadowCreatedMs || artifactUpdatedMs > shadowUpdatedMs)) addError('plan-artifact-shadow-time-mismatch');
    }
  }

  const plan = raw.plan;
  const planErrors = validatePlan(plan, policy);
  if (planErrors.length > 0) addError(`plan-invalid:${planErrors.join('; ')}`);
  if (isObject(plan)) {
    if (plan.plannerError) addError('planner-error');
    if (plan.policyId !== policy.policyId) addError('policy-id-mismatch');
    if (plan.mode !== policy.mode) addError('plan-mode-mismatch');
    if (options.policyDigest && plan.policyDigest !== options.policyDigest) addError('policy-digest-mismatch');
    if (!validSha(plan.baseSha)) addError('plan-base-sha-invalid');
    if (plan.headSha !== headSha) addError('plan-head-sha-mismatch');
    if (!validSha(plan.mergeSha)) addError('plan-merge-sha-invalid');
    if (!validSha(plan.mergeTreeSha)) addError('plan-merge-tree-sha-invalid');
    if (!isObject(plan.event)) addError('plan-event-missing');
    else {
      if (plan.event.pullRequest !== prNumber) addError('plan-pr-number-mismatch');
      if (options.repository && plan.event.repository !== options.repository) addError('plan-repository-mismatch');
      if (plan.event.name !== 'pull_request_target') addError('plan-event-mismatch');
      if (plan.event.ref !== raw.baseBranch) addError('plan-base-branch-mismatch');
    }
    validateCommit(raw.planMergeCommit, plan.mergeSha, [plan.baseSha, headSha], plan.mergeTreeSha, 'plan-merge-commit');
    if (headSha === finalHeadSha) {
      if (plan.baseSha !== baseSha) addError('final-plan-base-sha-mismatch');
      if (isObject(raw.mergeCommit) && plan.mergeTreeSha !== raw.mergeCommit.treeSha) addError('final-plan-merge-tree-mismatch');
    }
  }

  const requiredRun = raw.requiredRun;
  if (!isObject(requiredRun)) addError('required-run-missing');
  else {
    if (!Number.isInteger(requiredRun.id)) addError('required-run-id-invalid');
    if (requiredRun.path !== REQUIRED_WORKFLOW_PATH) addError('required-workflow-mismatch');
    if (requiredRun.event !== 'pull_request') addError('required-event-mismatch');
    if (requiredRun.status !== 'completed') addError('required-run-incomplete');
    if (!REQUIRED_RUN_CONCLUSIONS.has(requiredRun.conclusion)) addError('required-run-conclusion-unusable');
    if (!Number.isInteger(requiredRun.runAttempt) || requiredRun.runAttempt < 1) addError('required-run-attempt-invalid');
    if (requiredRun.headSha !== headSha) addError('required-head-sha-mismatch');
    if (requiredRun.headBranch !== raw.headBranch) addError('required-head-branch-mismatch');
    if (requiredRun.headRepository !== raw.headRepository) addError('required-head-repository-mismatch');
    validatePullBindings(requiredRun.pullRequests, 'required-run');
    const requiredTriggerMs = Date.parse(requiredRun.triggerCreatedAt ?? '');
    const requiredCreatedMs = Date.parse(requiredRun.createdAt ?? '');
    const requiredUpdatedMs = Date.parse(requiredRun.updatedAt ?? '');
    const mergedMs = Date.parse(mergedAt ?? '');
    if (Number.isNaN(requiredTriggerMs)
      || Number.isNaN(requiredCreatedMs)
      || Number.isNaN(requiredUpdatedMs)
      || requiredCreatedMs < requiredTriggerMs
      || requiredUpdatedMs < requiredCreatedMs
      || (!Number.isNaN(mergedMs) && (requiredCreatedMs > mergedMs || requiredUpdatedMs > mergedMs))) addError('required-run-after-merge');
    if (validTimestamp(options.since) && validTimestamp(options.until)
      && (requiredTriggerMs < Date.parse(options.since) || requiredUpdatedMs > Date.parse(options.until))) addError('required-run-outside-window');
    const shadowCreatedMs = Date.parse(raw.shadowRun && raw.shadowRun.createdAt ? raw.shadowRun.createdAt : '');
    if (Number.isNaN(requiredTriggerMs)
      || Number.isNaN(shadowCreatedMs)
      || Math.abs(requiredTriggerMs - shadowCreatedMs) > RUN_CORRELATION_WINDOW_MS) addError('required-shadow-run-time-mismatch');
  }

  const jobs = Array.isArray(raw.jobs) ? raw.jobs : [];
  if (!Array.isArray(raw.jobs) || jobs.length === 0) addError('required-jobs-missing');
  const failedJobs = [];
  for (const job of jobs) {
    if (!isObject(job) || typeof job.name !== 'string' || job.name.length === 0) {
      addError('required-job-name-invalid');
      continue;
    }
    if (requiredRun && job.runId !== requiredRun.id) addError(`required-job-run-mismatch:${job.name}`);
    if (requiredRun && job.runAttempt !== requiredRun.runAttempt) addError(`required-job-attempt-mismatch:${job.name}`);
    if (job.headSha !== headSha) addError(`required-job-head-mismatch:${job.name}`);
    if (job.status !== 'completed') addError(`required-job-incomplete:${job.name}`);
    if (FAILURE_CONCLUSIONS.has(job.conclusion)) failedJobs.push({ checkName: job.name, conclusion: job.conclusion });
    else if (!NON_FAILURE_CONCLUSIONS.has(job.conclusion)) addError(`required-job-conclusion-unusable:${job.name}:${job.conclusion}`);
  }
  const jobNames = jobs.filter((job) => isObject(job) && typeof job.name === 'string').map((job) => job.name);
  if (new Set(jobNames).size !== jobNames.length) addError('required-job-names-duplicated');
  if (requiredRun && requiredRun.conclusion === 'success' && failedJobs.length > 0) addError('required-run-success-with-failed-job');
  if (requiredRun && requiredRun.conclusion !== 'success' && failedJobs.length === 0) addError('required-run-failed-without-failed-job');

  const byCheck = laneByCheckName(policy);
  const failedLanes = [];
  for (const failed of failedJobs) {
    const lane = byCheck.get(failed.checkName);
    if (!lane) addError(`unknown-failed-job:${failed.checkName}`);
    else failedLanes.push({ ...lane, conclusion: failed.conclusion });
  }
  failedLanes.sort((a, b) => a.checkName.localeCompare(b.checkName));
  const plannedChecks = new Set(Array.isArray(plan && plan.selected) ? plan.selected.map((entry) => entry.checkName) : []);
  const plannedLanes = uniqueSorted(Array.isArray(plan && plan.selected) ? plan.selected.map((entry) => entry.lane) : []);
  const missedLanes = failedLanes
    .filter((entry) => !plannedChecks.has(entry.checkName))
    .map((entry) => ({ ...entry }));
  const uniqueErrors = uniqueSorted(errors);
  const usable = uniqueErrors.length === 0;
  return {
    prNumber: Number.isInteger(prNumber) ? prNumber : null,
    mergedAt: validTimestamp(mergedAt) ? new Date(mergedAt).toISOString() : null,
    headSha: validSha(headSha) ? String(headSha).toLowerCase() : null,
    finalHeadSha: validSha(finalHeadSha) ? String(finalHeadSha).toLowerCase() : null,
    headBranch: typeof raw.headBranch === 'string' ? raw.headBranch : null,
    headRepository: typeof raw.headRepository === 'string' ? raw.headRepository : null,
    baseBranch: typeof raw.baseBranch === 'string' ? raw.baseBranch : null,
    baseRepository: typeof raw.baseRepository === 'string' ? raw.baseRepository : null,
    baseSha: validSha(baseSha) ? String(baseSha).toLowerCase() : null,
    mergeCommitSha: validSha(mergeCommitSha) ? String(mergeCommitSha).toLowerCase() : null,
    mergeTreeSha: isObject(raw.mergeCommit) && validSha(raw.mergeCommit.treeSha) ? String(raw.mergeCommit.treeSha).toLowerCase() : null,
    shadowRunId: isObject(raw.shadowRun) && Number.isInteger(raw.shadowRun.id) ? raw.shadowRun.id : null,
    requiredRunId: isObject(requiredRun) && Number.isInteger(requiredRun.id) ? requiredRun.id : null,
    requiredRunAttempt: isObject(requiredRun) && Number.isInteger(requiredRun.runAttempt) ? requiredRun.runAttempt : null,
    requiredConclusion: isObject(requiredRun) ? requiredRun.conclusion ?? null : null,
    plannedLanes,
    failedLanes,
    missedLanes,
    usable,
    covered: usable && missedLanes.length === 0,
    errors: uniqueErrors,
  };
}

function summarizeStats(entries, sampleComplete) {
  return entries.map((entry) => {
    const recall = entry.failureCount === 0 ? null : entry.coveredFailureCount / entry.failureCount;
    return {
      ...entry,
      recall,
      readyForSelection: sampleComplete && entry.failureCount > 0 && entry.missedFailureCount === 0,
    };
  });
}

function invalidateObservation(observation, code) {
  return {
    ...observation,
    usable: false,
    covered: false,
    errors: uniqueSorted([...observation.errors, code]),
  };
}

export function buildRecallReport(rawObservations, policy, options) {
  const policyErrors = validatePolicy(policy);
  if (policyErrors.length > 0) throw new Error(`Invalid Smart CI policy: ${policyErrors.join('; ')}`);
  if (!options || !validTimestamp(options.since) || !validTimestamp(options.until)) throw new Error('report window needs valid since/until timestamps');
  if (Date.parse(options.since) > Date.parse(options.until)) throw new Error('report window since must not be after until');
  if (!/^[\w.-]+\/[\w.-]+$/.test(String(options.repository ?? ''))) throw new Error('report repository must be owner/name');
  if (!/^sha256:[0-9a-f]{64}$/i.test(String(options.policyDigest ?? ''))) throw new Error('report policyDigest must be sha256:<hex>');
  if (!Number.isInteger(options.minimumObservations) || options.minimumObservations < MINIMUM_PR_OBSERVATIONS) throw new Error(`minimumObservations must be an integer >= ${MINIMUM_PR_OBSERVATIONS}`);
  if (!Array.isArray(rawObservations)) throw new Error('observations must be an array');

  let observations = rawObservations.map((raw) => normaliseObservation(raw, policy, options));
  const identityCounts = new Map();
  for (const observation of observations) {
    if (!Number.isInteger(observation.prNumber)
      || !observation.headSha
      || !Number.isInteger(observation.requiredRunId)
      || !Number.isInteger(observation.requiredRunAttempt)) continue;
    const key = `${observation.prNumber}:${observation.headSha}:${observation.requiredRunId}:${observation.requiredRunAttempt}`;
    identityCounts.set(key, (identityCounts.get(key) ?? 0) + 1);
  }
  observations = observations.map((observation) => {
    if (!Number.isInteger(observation.prNumber)
      || !observation.headSha
      || !Number.isInteger(observation.requiredRunId)
      || !Number.isInteger(observation.requiredRunAttempt)) return observation;
    const key = `${observation.prNumber}:${observation.headSha}:${observation.requiredRunId}:${observation.requiredRunAttempt}`;
    return identityCounts.get(key) > 1 ? invalidateObservation(observation, 'duplicate-observation') : observation;
  });

  const groupIndexes = new Map();
  observations.forEach((observation, index) => {
    const key = Number.isInteger(observation.prNumber) ? `pr:${observation.prNumber}` : `invalid:${index}`;
    if (!groupIndexes.has(key)) groupIndexes.set(key, []);
    groupIndexes.get(key).push(index);
  });
  for (const indexes of groupIndexes.values()) {
    const rows = indexes.map((index) => observations[index]);
    const groupErrors = [];
    for (const field of ['mergedAt', 'finalHeadSha', 'headBranch', 'headRepository', 'baseSha', 'baseBranch', 'baseRepository', 'mergeCommitSha', 'mergeTreeSha']) {
      if (new Set(rows.map((row) => row[field])).size !== 1) groupErrors.push(`pr-${field.replace(/[A-Z]/g, (letter) => `-${letter.toLowerCase()}`)}-mismatch`);
    }
    if (!rows.some((row) => row.usable && row.headSha === row.finalHeadSha && row.requiredConclusion === 'success')) {
      groupErrors.push('final-head-success-missing');
    }
    for (const index of indexes) {
      for (const error of groupErrors) observations[index] = invalidateObservation(observations[index], error);
    }
  }

  observations.sort((a, b) => (a.prNumber ?? Number.MAX_SAFE_INTEGER) - (b.prNumber ?? Number.MAX_SAFE_INTEGER)
    || String(a.headSha).localeCompare(String(b.headSha))
    || (a.requiredRunId ?? Number.MAX_SAFE_INTEGER) - (b.requiredRunId ?? Number.MAX_SAFE_INTEGER)
    || (a.requiredRunAttempt ?? Number.MAX_SAFE_INTEGER) - (b.requiredRunAttempt ?? Number.MAX_SAFE_INTEGER));
  const grouped = new Map();
  observations.forEach((observation, index) => {
    const key = Number.isInteger(observation.prNumber) ? `pr:${observation.prNumber}` : `invalid:${index}`;
    if (!grouped.has(key)) grouped.set(key, []);
    grouped.get(key).push(observation);
  });
  const pullRequests = [...grouped.values()].map((rows) => {
    const usable = rows.every((row) => row.usable);
    return {
      prNumber: rows[0].prNumber,
      mergedAt: rows[0].mergedAt,
      finalHeadSha: rows[0].finalHeadSha,
      revisionCount: rows.length,
      usable,
      failedLaneCount: usable ? rows.reduce((sum, row) => sum + row.failedLanes.length, 0) : 0,
      missedFailureCount: usable ? rows.reduce((sum, row) => sum + row.missedLanes.length, 0) : 0,
      errors: uniqueSorted(rows.flatMap((row) => row.errors)),
    };
  }).sort((a, b) => (a.prNumber ?? Number.MAX_SAFE_INTEGER) - (b.prNumber ?? Number.MAX_SAFE_INTEGER));
  const usablePrNumbers = new Set(pullRequests.filter((pull) => pull.usable).map((pull) => pull.prNumber));
  const usable = observations.filter((observation) => usablePrNumbers.has(observation.prNumber));
  const failedLaneCount = usable.reduce((sum, observation) => sum + observation.failedLanes.length, 0);
  const missedFailureCount = usable.reduce((sum, observation) => sum + observation.missedLanes.length, 0);
  const usableObservationCount = pullRequests.filter((pull) => pull.usable).length;
  const unusableObservationCount = pullRequests.length - usableObservationCount;
  const sampleComplete = usableObservationCount >= options.minimumObservations && unusableObservationCount === 0;
  const recall = failedLaneCount === 0 ? null : (failedLaneCount - missedFailureCount) / failedLaneCount;

  const laneCounts = new Map(Object.entries(policy.lanes).map(([lane, definition]) => [lane, {
    lane,
    checkName: definition.checkName,
    family: definition.family,
    failureCount: 0,
    coveredFailureCount: 0,
    missedFailureCount: 0,
  }]));
  for (const observation of usable) {
    const missedChecks = new Set(observation.missedLanes.map((entry) => entry.checkName));
    for (const failure of observation.failedLanes) {
      const row = laneCounts.get(failure.lane);
      row.failureCount += 1;
      if (missedChecks.has(failure.checkName)) row.missedFailureCount += 1;
      else row.coveredFailureCount += 1;
    }
  }
  const laneStats = summarizeStats([...laneCounts.values()].sort((a, b) => a.lane.localeCompare(b.lane)), sampleComplete);
  const familyRows = new Map();
  for (const lane of laneStats) {
    if (!familyRows.has(lane.family)) familyRows.set(lane.family, { family: lane.family, failureCount: 0, coveredFailureCount: 0, missedFailureCount: 0 });
    const row = familyRows.get(lane.family);
    row.failureCount += lane.failureCount;
    row.coveredFailureCount += lane.coveredFailureCount;
    row.missedFailureCount += lane.missedFailureCount;
  }
  const familyStats = summarizeStats([...familyRows.values()].sort((a, b) => a.family.localeCompare(b.family)), sampleComplete);
  const readinessReasons = [];
  if (usableObservationCount < options.minimumObservations) readinessReasons.push('insufficient-observations');
  if (unusableObservationCount > 0) readinessReasons.push('unusable-observations');
  if (failedLaneCount === 0) readinessReasons.push('no-failure-evidence');
  if (missedFailureCount > 0) readinessReasons.push('missed-failures');

  return {
    schemaVersion: 1,
    kind: 'smart-ci-recall-report',
    repository: options.repository ?? null,
    policyId: policy.policyId,
    policyDigest: options.policyDigest ?? null,
    window: {
      since: new Date(options.since).toISOString(),
      until: new Date(options.until).toISOString(),
    },
    minimumObservations: options.minimumObservations,
    observationCount: pullRequests.length,
    usableObservationCount,
    unusableObservationCount,
    revisionObservationCount: observations.length,
    usableRevisionObservationCount: usable.length,
    failedLaneCount,
    missedFailureCount,
    recall,
    sampleComplete,
    readyForSelection: sampleComplete && missedFailureCount === 0 && familyStats.some((family) => family.readyForSelection),
    readinessReasons: uniqueSorted(readinessReasons),
    pullRequests,
    observations,
    familyStats,
    laneStats,
  };
}

function markdownCell(value) {
  return String(value ?? '').replace(/\|/g, '\\|').replace(/[\r\n]+/g, ' ');
}

function displayRecall(value) {
  return value === null ? 'not observed' : `${(value * 100).toFixed(1)}%`;
}

export function renderRecallMarkdown(report) {
  const lines = [
    '# Smart CI shadow recall report',
    '',
    `- Repository: \`${markdownCell(report.repository ?? 'unknown')}\``,
    `- Window: \`${report.window.since}\` through \`${report.window.until}\``,
    `- Policy: \`${report.policyId}\` (\`${report.policyDigest}\`)`,
    `- Usable merged PRs: **${report.usableObservationCount}/${report.observationCount}** (minimum ${report.minimumObservations})`,
    `- Usable revision/attempt observations: **${report.usableRevisionObservationCount}/${report.revisionObservationCount}**`,
    `- Failed lanes observed: **${report.failedLaneCount}**; missed: **${report.missedFailureCount}**; recall: **${displayRecall(report.recall)}**`,
    `- Ready for selection: **${report.readyForSelection ? 'yes' : 'no'}**${report.readinessReasons.length > 0 ? ` (${report.readinessReasons.join(', ')})` : ''}`,
    '',
    '## Lane families',
    '',
    '| Family | Failures | Covered | Missed | Recall | Ready |',
    '| --- | ---: | ---: | ---: | ---: | --- |',
  ];
  for (const row of report.familyStats) {
    lines.push(`| ${markdownCell(row.family)} | ${row.failureCount} | ${row.coveredFailureCount} | ${row.missedFailureCount} | ${displayRecall(row.recall)} | ${row.readyForSelection ? 'yes' : 'no'} |`);
  }
  lines.push('', '## Revision and attempt observations', '', '| PR | Head | Run / attempt | Usable | Failed | Missed | Evidence |', '| ---: | --- | --- | --- | ---: | ---: | --- |');
  for (const observation of report.observations) {
    const evidence = observation.usable ? 'complete' : observation.errors.join('; ');
    const runAttempt = observation.requiredRunId && observation.requiredRunAttempt
      ? `${observation.requiredRunId} / ${observation.requiredRunAttempt}`
      : 'unknown';
    lines.push(`| #${observation.prNumber ?? '?'} | \`${observation.headSha ? observation.headSha.slice(0, 12) : 'unknown'}\` | ${runAttempt} | ${observation.usable ? 'yes' : 'no'} | ${observation.failedLanes.length} | ${observation.missedLanes.length} | ${markdownCell(evidence)} |`);
  }
  if (report.observations.length === 0) lines.push('| - | - | - | no | 0 | 0 | no merged PRs in window |');
  return `${lines.join('\n')}\n`;
}

export function exitCodeForReport(report) {
  if (report.unusableObservationCount > 0) return 1;
  if (report.missedFailureCount > 0) return 3;
  if (!report.readyForSelection) return 2;
  return 0;
}

function writeOutput(path, contents) {
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, contents);
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  if (args.help) return;
  if (!existsSync(args.policy)) throw new Error(`policy file not found: ${args.policy}`);
  const policyText = readFileSync(args.policy, 'utf8');
  const policy = JSON.parse(policyText);
  const digest = policyDigest(policyText);
  let rawObservations;
  if (args.input) {
    if (!existsSync(args.input)) throw new Error(`input file not found: ${args.input}`);
    const parsed = JSON.parse(readFileSync(args.input, 'utf8'));
    rawObservations = Array.isArray(parsed) ? parsed : parsed.observations;
  } else {
    rawObservations = await collectLiveObservations(args);
  }
  const report = buildRecallReport(rawObservations, policy, {
    repository: args.repo,
    since: args.since,
    until: args.until,
    minimumObservations: args.minimumObservations,
    policyDigest: digest,
  });
  const json = `${JSON.stringify(report, null, 2)}\n`;
  const markdown = renderRecallMarkdown(report);
  if (args.outJson) writeOutput(args.outJson, json);
  if (args.outMarkdown) writeOutput(args.outMarkdown, markdown);
  if (!args.outJson && !args.outMarkdown) process.stdout.write(markdown);
  else console.log(JSON.stringify({
    observations: report.observationCount,
    usable: report.usableObservationCount,
    failedLanes: report.failedLaneCount,
    missedFailures: report.missedFailureCount,
    readyForSelection: report.readyForSelection,
    exitCode: exitCodeForReport(report),
  }));
  process.exitCode = exitCodeForReport(report);
}

if (process.argv[1] && /recall-report\.mjs$/.test(process.argv[1])) {
  main().catch((error) => {
    console.error(error.stack ?? String(error));
    process.exitCode = 1;
  });
}
