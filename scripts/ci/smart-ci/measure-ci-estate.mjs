#!/usr/bin/env node
// CI-01 (ADR-0066): measure the GitHub Actions estate of one repository over a
// date window and emit a content-free JSON + Markdown ledger.
//
//   node scripts/ci/smart-ci/measure-ci-estate.mjs \
//     --repo Chris0Jeky/Taskdeck --since 2026-07-31 --until 2026-08-30 \
//     --sample 30 --workflow-path .github/workflows/ci-required.yml --out-dir docs/ci/baselines
//
// Token: GH_TOKEN or GITHUB_TOKEN, else `gh auth token`. Read scope only
// (`actions: read` in CI). Nothing here writes to GitHub. The ledger for a given
// --until date is never overwritten unless --overwrite is passed (append rule in
// docs/ci/CI_BASELINE.md).

import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import {
  PRICING,
  completedRuns,
  projectMonthlyAllowance,
  renderMarkdown,
  summarizeArtifacts,
  summarizeRunJobs,
  summarizeRuns,
  summarizeSample,
  validateWindow,
  workflowKey,
} from './lib/estate.mjs';

const API = 'https://api.github.com';

function parseArgs(argv) {
  const args = {
    repo: 'Chris0Jeky/Taskdeck',
    since: null,
    until: null,
    sample: 30,
    workflowPath: '.github/workflows/ci-required.yml',
    outDir: 'docs/ci/baselines',
    overwrite: false,
    maxRunPages: 80,
    maxArtifactPages: 400,
  };
  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index];
    const next = () => argv[++index];
    switch (arg) {
      case '--repo': args.repo = next(); break;
      case '--since': args.since = next(); break;
      case '--until': args.until = next(); break;
      case '--sample': args.sample = Number(next()); break;
      case '--workflow-path': args.workflowPath = next(); break;
      case '--out-dir': args.outDir = next(); break;
      case '--overwrite': args.overwrite = true; break;
      case '--max-run-pages': args.maxRunPages = Number(next()); break;
      case '--max-artifact-pages': args.maxArtifactPages = Number(next()); break;
      case '--help':
        console.log('usage: measure-ci-estate.mjs --since YYYY-MM-DD --until YYYY-MM-DD [--repo owner/name] [--sample N] [--workflow-path .github/workflows/x.yml] [--out-dir DIR] [--overwrite]');
        process.exit(0);
        break;
      default:
        throw new Error(`Unknown argument: ${arg}`);
    }
  }
  const windowErrors = validateWindow(args.since, args.until);
  if (windowErrors.length > 0) throw new Error(windowErrors.join('; '));
  if (!/^[\w.-]+\/[\w.-]+$/.test(args.repo)) throw new Error('--repo must be owner/name');
  if (!Number.isInteger(args.sample) || args.sample < 1) throw new Error('--sample must be a positive integer');
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

async function sleep(ms) {
  await new Promise((resolve) => setTimeout(resolve, ms));
}

async function ghFetch(token, url) {
  for (let attempt = 0; attempt < 6; attempt += 1) {
    const response = await fetch(url, {
      headers: {
        Accept: 'application/vnd.github+json',
        Authorization: `Bearer ${token}`,
        'X-GitHub-Api-Version': '2022-11-28',
        'User-Agent': 'taskdeck-smart-ci-measure',
      },
    });
    if (response.status === 403 || response.status === 429) {
      const remaining = Number(response.headers.get('x-ratelimit-remaining') ?? '1');
      const reset = Number(response.headers.get('x-ratelimit-reset') ?? '0') * 1000;
      const retryAfter = Number(response.headers.get('retry-after') ?? '0') * 1000;
      const waitMs = retryAfter || (remaining === 0 && reset ? Math.max(1000, reset - Date.now() + 1000) : 15000 * (attempt + 1));
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

function eachDay(since, until) {
  const days = [];
  for (let cursor = Date.parse(`${since}T00:00:00Z`); cursor <= Date.parse(`${until}T00:00:00Z`); cursor += 86400000) {
    days.push(new Date(cursor).toISOString().slice(0, 10));
  }
  return days;
}

function nextLink(linkHeader) {
  const match = /<([^>]+)>;\s*rel="next"/.exec(linkHeader ?? '');
  return match ? match[1] : null;
}

async function paginate(token, url, itemsKey, maxPages, onPage) {
  const items = [];
  let pages = 0;
  let current = url;
  let truncated = false;
  while (current) {
    const { json, link } = await ghFetch(token, current);
    const pageItems = json[itemsKey] ?? [];
    items.push(...pageItems);
    pages += 1;
    if (onPage) onPage(pages, items.length, json.total_count);
    current = nextLink(link);
    if (current && pages >= maxPages) {
      truncated = true;
      break;
    }
  }
  return { items, pages, truncated };
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  const stem = join(args.outDir, `ci-estate-${args.until}`);
  if (!args.overwrite && (existsSync(`${stem}.json`) || existsSync(`${stem}.md`))) {
    throw new Error(`${stem}.json/.md already exist; ledgers are appended per --until date, never overwritten (pass --overwrite to regenerate this one deliberately)`);
  }
  const token = resolveToken();
  const base = `${API}/repos/${args.repo}`;

  console.error(`listing workflow runs ${args.since}..${args.until} (one API window per day; the runs API returns at most 1000 results per filter)`);
  const runsListing = { items: [], pages: 0, truncated: false, dayWindowsOver1000: [] };
  for (const day of eachDay(args.since, args.until)) {
    const dayListing = await paginate(token, `${base}/actions/runs?created=${day}..${day}&per_page=100`, 'workflow_runs', args.maxRunPages);
    runsListing.items.push(...dayListing.items);
    runsListing.pages += dayListing.pages;
    runsListing.truncated ||= dayListing.truncated;
    if (dayListing.items.length >= 1000) runsListing.dayWindowsOver1000.push(day);
  }
  console.error(`  ${runsListing.items.length} runs over ${runsListing.pages} pages`);
  const runs = runsListing.items.map((run) => ({
    id: run.id,
    name: run.name,
    path: run.path,
    workflow_id: run.workflow_id,
    event: run.event,
    status: run.status,
    conclusion: run.conclusion,
    run_attempt: run.run_attempt,
    head_sha: run.head_sha,
    head_branch: run.head_branch,
    created_at: run.created_at,
    run_started_at: run.run_started_at,
    updated_at: run.updated_at,
  }));
  runs.sort((a, b) => String(b.created_at).localeCompare(String(a.created_at)));
  const runSummary = summarizeRuns(runs);

  const requiredKey = workflowKey({ path: args.workflowPath, name: args.workflowPath });
  const requiredRuns = runs.filter((run) => workflowKey(run) === requiredKey);
  const sampleRuns = requiredRuns
    .filter((run) => run.event === 'pull_request' && run.conclusion === 'success')
    .slice(0, args.sample);
  const workflowLabel = requiredRuns[0] ? requiredRuns[0].name : args.workflowPath;
  console.error(`sampling jobs of ${sampleRuns.length} green ${workflowLabel} pull_request runs (latest attempt of each)`);
  const sampledSummaries = [];
  for (const run of sampleRuns) {
    const jobs = await paginate(token, `${base}/actions/runs/${run.id}/jobs?per_page=100`, 'jobs', 3);
    const summary = summarizeRunJobs(jobs.items.map((job) => ({
      name: job.name,
      labels: job.labels,
      runner_name: job.runner_name,
      started_at: job.started_at,
      completed_at: job.completed_at,
      conclusion: job.conclusion,
    })));
    sampledSummaries.push({ runId: run.id, headSha: run.head_sha, createdAt: run.created_at, runAttempt: run.run_attempt, ...summary });
  }
  const sample = { workflowName: workflowLabel, workflowPath: args.workflowPath, ...summarizeSample(sampledSummaries), runs: sampledSummaries.map(({ jobs, ...rest }) => rest) };

  console.error('reading cache usage and artifacts');
  const cache = (await ghFetch(token, `${base}/actions/cache/usage`)).json;
  const artifactsListing = await paginate(
    token,
    `${base}/actions/artifacts?per_page=100`,
    'artifacts',
    args.maxArtifactPages,
    (page, count, total) => { if (page % 25 === 0) console.error(`  artifacts page ${page}: ${count}/${total ?? '?'}`); },
  );
  const artifacts = { ...summarizeArtifacts(artifactsListing.items), truncated: artifactsListing.truncated, pagesRead: artifactsListing.pages };

  const windowDays = Math.max(1, Math.round((Date.parse(args.until) - Date.parse(args.since)) / 86400000) + 1);
  const completedAllEvents = completedRuns(requiredRuns);
  const completedPullRequest = completedRuns(requiredRuns, 'pull_request');
  const perMonth = (count) => Math.round((count / windowDays) * 30);
  const projections = [
    { label: 'current topology, completed required runs (success + failure, every event)', fullRunsPerMonth: perMonth(completedAllEvents) },
    { label: 'current topology, all required runs including cancelled (upper bound)', fullRunsPerMonth: perMonth(requiredRuns.length) },
    { label: 'pull_request-only qualification (no push/main re-run), completed pull_request runs', fullRunsPerMonth: perMonth(completedPullRequest) },
  ].map((projection) => ({
    ...projection,
    meanBillableMinutesPerRun: sample.billableMinutesPerRun.mean,
    ...projectMonthlyAllowance(sample.billableMinutesPerRun.mean, projection.fullRunsPerMonth),
  }));

  const report = {
    schemaVersion: 1,
    generatedAtUtc: new Date().toISOString(),
    repository: args.repo,
    window: { since: args.since, until: args.until, days: windowDays },
    method: {
      runPagesRead: runsListing.pages,
      runListingTruncated: runsListing.truncated,
      dayWindowsAtTheApiCap: runsListing.dayWindowsOver1000,
      sampleSize: sampledSummaries.length,
      requiredWorkflowPath: args.workflowPath,
      assumptions: [
        `Runs listed with the Actions API \`created=<day>..<day>\` filter for every UTC day in ${args.since}..${args.until} (the API returns at most 1000 runs per filter, so the window is chunked per day; a day at the cap is reported). Workflows are grouped by their workflow file path (GitHub-managed workflows such as Copilot, Dependabot and CodeQL use per-run display names, so those group by their \`dynamic/...\` path).`,
        'The listing exposes one entry per run carrying its LATEST attempt: "runs re-run" counts runs with run_attempt > 1, "extra attempts" sums run_attempt - 1, and the sampled jobs are those of the latest attempt only (earlier failed attempts consumed minutes this ledger does not see).',
        'Job duration = completed_at - started_at (queue time excluded); critical path = last job completion - first job start within one run.',
        `Allowance minutes follow GitHub billing: every job rounds up to a whole minute, Windows counts x${PRICING.allowanceMultiplier.windows}, macOS x${PRICING.allowanceMultiplier.macos}; the GitHub Pro private-repository allowance is ${PRICING.includedMinutesPro} minutes/month and ${PRICING.includedStorageGbPro} GB storage (prices as of ${PRICING.asOf}: Linux $${PRICING.perMinuteUsd.linux}, Windows $${PRICING.perMinuteUsd.windows}, macOS $${PRICING.perMinuteUsd.macos} per minute beyond the allowance).`,
        'The public repository reports 0 billable minutes; the estimate applies private-repository accounting to measured durations.',
        `Prices, rounding and the Pro allowances were verified on docs.github.com on ${PRICING.asOf}; the Windows x${PRICING.allowanceMultiplier.windows} / macOS x${PRICING.allowanceMultiplier.macos} allowance multipliers are GitHub's long-standing rule and were NOT re-verified on the pages read that day (allowanceMultiplierVerified=false) - re-read before the cutover. Shared artifact/Packages storage beyond the allowance is $${PRICING.sharedStoragePerGbMonthUsd} per GB-month; Actions cache $${PRICING.cachePerGbMonthUsd} per GB-month.`,
        'Projections scale the required workflow\'s run counts in the window to 30 days and multiply by the sample\'s mean allowance minutes per run: "completed" counts success + failure conclusions (per event where stated); cancelled runs consumed minutes until cancellation, so the "including cancelled" line is an upper bound.',
        'Artifacts: unexpired bytes only; expired artifacts no longer occupy storage. Sizes are the API-reported size_in_bytes.',
        'The ledger records names, ids, timestamps and sizes only - no log content, no user content.',
      ],
    },
    runs: runSummary,
    sample,
    projections,
    storage: {
      cache: { activeCachesSizeInBytes: cache.active_caches_size_in_bytes, activeCachesCount: cache.active_caches_count },
      artifacts,
    },
  };

  mkdirSync(args.outDir, { recursive: true });
  writeFileSync(`${stem}.json`, `${JSON.stringify(report, null, 2)}\n`);
  writeFileSync(`${stem}.md`, renderMarkdown(report));
  console.error(`wrote ${stem}.json and ${stem}.md`);
  console.log(JSON.stringify({
    runs: report.runs.total,
    requiredRuns: requiredRuns.length,
    completedPullRequestRuns: completedPullRequest,
    sample: report.sample.sampleSize,
    criticalPathP50Min: Number((report.sample.criticalPathSeconds.p50 / 60).toFixed(1)),
    allowanceMinutesPerRunMean: report.sample.billableMinutesPerRun.mean,
    cacheGb: Number((report.storage.cache.activeCachesSizeInBytes / 1e9).toFixed(2)),
    artifactsUnexpiredGb: report.storage.artifacts.unexpiredGb,
    artifactsTruncated: report.storage.artifacts.truncated,
  }));
}

main().catch((error) => {
  console.error(error.stack ?? String(error));
  process.exit(1);
});
