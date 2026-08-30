import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  PRICING,
  billableMinutesForJob,
  classifyRunnerOs,
  completedRuns,
  costUsdForJob,
  percentile,
  projectMonthlyAllowance,
  renderMarkdown,
  secondsBetween,
  summarizeArtifacts,
  summarizeRunJobs,
  summarizeRuns,
  summarizeSample,
  validateWindow,
  workflowKey,
} from './lib/estate.mjs';

test('secondsBetween tolerates missing, unparsable and inverted timestamps', () => {
  assert.equal(secondsBetween('2026-08-30T10:00:00Z', '2026-08-30T10:01:30Z'), 90);
  assert.equal(secondsBetween(null, '2026-08-30T10:01:30Z'), 0);
  assert.equal(secondsBetween('nope', '2026-08-30T10:01:30Z'), 0);
  assert.equal(secondsBetween('2026-08-30T10:01:30Z', '2026-08-30T10:00:00Z'), 0);
});

test('an unknown runner OS is priced as Linux with a x1 allowance multiplier', () => {
  assert.equal(billableMinutesForJob(61, 'unknown'), 2);
  assert.equal(costUsdForJob(61, 'unknown'), Number((2 * PRICING.perMinuteUsd.linux).toFixed(4)));
});

test('run and job summaries carry only ids, names, timestamps, sizes and counts', () => {
  const jobSummary = summarizeRunJobs([{ name: 'J', labels: ['ubuntu-latest'], started_at: '2026-08-30T10:00:00Z', completed_at: '2026-08-30T10:02:00Z', conclusion: 'success', steps: [{ name: 'secret step log' }], html_url: 'x' }]);
  assert.deepEqual(Object.keys(jobSummary.jobs[0]).sort(), ['billableMinutes', 'completedAt', 'conclusion', 'costUsdIfBeyondAllowance', 'durationSeconds', 'name', 'os', 'startedAt']);
  const runSummary = summarizeRuns([{ name: 'CI', path: '.github/workflows/ci-required.yml', event: 'pull_request', conclusion: 'success', run_attempt: 1, head_sha: 'a', created_at: '2026-08-01T00:00:00Z', display_title: 'PR title must not be copied', head_branch: 'feature/x' }]);
  assert.ok(!JSON.stringify(runSummary).includes('PR title'));
  assert.ok(!JSON.stringify(runSummary).includes('feature/x'));
});

test('classifyRunnerOs reads hosted labels and self-hosted first', () => {
  assert.equal(classifyRunnerOs(['ubuntu-latest']), 'linux');
  assert.equal(classifyRunnerOs(['windows-2025']), 'windows');
  assert.equal(classifyRunnerOs(['macos-15']), 'macos');
  assert.equal(classifyRunnerOs(['self-hosted', 'linux', 'x64']), 'self-hosted');
  assert.equal(classifyRunnerOs([], 'GitHub Actions 12'), 'unknown');
});

test('billable minutes round every job up and apply the OS multiplier', () => {
  assert.equal(billableMinutesForJob(1, 'linux'), 1);
  assert.equal(billableMinutesForJob(60, 'linux'), 1);
  assert.equal(billableMinutesForJob(61, 'linux'), 2);
  assert.equal(billableMinutesForJob(61, 'windows'), 4);
  assert.equal(billableMinutesForJob(30, 'macos'), 10);
  assert.equal(billableMinutesForJob(0, 'linux'), 0);
  assert.equal(billableMinutesForJob(900, 'self-hosted'), 0);
  assert.equal(costUsdForJob(120, 'windows'), Number((2 * PRICING.perMinuteUsd.windows).toFixed(4)));
});

test('percentile uses nearest rank and tolerates empty input', () => {
  assert.equal(percentile([], 50), 0);
  assert.equal(percentile([5], 95), 5);
  assert.equal(percentile([1, 2, 3, 4], 50), 2);
  assert.equal(percentile([1, 2, 3, 4], 95), 4);
});

test('summarizeRunJobs computes critical path across parallel jobs and per-OS rollups', () => {
  const summary = summarizeRunJobs([
    { name: 'A', labels: ['ubuntu-latest'], started_at: '2026-08-30T10:00:00Z', completed_at: '2026-08-30T10:05:00Z', conclusion: 'success' },
    { name: 'B (windows)', labels: ['windows-latest'], started_at: '2026-08-30T10:00:10Z', completed_at: '2026-08-30T10:15:40Z', conclusion: 'success' },
    { name: 'C', labels: ['ubuntu-latest'], started_at: '2026-08-30T10:16:00Z', completed_at: '2026-08-30T10:20:00Z', conclusion: 'success' },
  ]);
  assert.equal(summary.jobCount, 3);
  assert.equal(summary.criticalPathSeconds, 20 * 60);
  assert.equal(summary.aggregateRunnerSeconds, 300 + 930 + 240);
  assert.equal(summary.billableMinutes, 5 + 16 * 2 + 4);
  assert.equal(summary.byOs.windows.jobs, 1);
  assert.equal(summary.byOs.linux.billableMinutes, 9);
});

test('summarizeRuns groups by workflow path, counts re-run attempts, cancellations and exact-SHA multi-event qualification', () => {
  const ci = '.github/workflows/ci-required.yml';
  const runs = [
    { name: 'CI', path: ci, event: 'pull_request', conclusion: 'success', run_attempt: 1, head_sha: 'aaa', created_at: '2026-08-01T00:00:00Z' },
    { name: 'CI', path: ci, event: 'push', conclusion: 'success', run_attempt: 1, head_sha: 'aaa', created_at: '2026-08-01T01:00:00Z' },
    { name: 'CI', path: ci, event: 'pull_request', conclusion: 'cancelled', run_attempt: 1, head_sha: 'bbb', created_at: '2026-08-02T00:00:00Z' },
    { name: 'CI', path: ci, event: 'pull_request', conclusion: 'failure', run_attempt: 3, head_sha: 'bbb', created_at: '2026-08-02T00:10:00Z' },
    { name: 'CI', path: ci, event: 'push', conclusion: 'cancelled', run_attempt: 1, head_sha: 'ddd', created_at: '2026-08-02T00:20:00Z' },
    { name: 'CI Nightly', path: '.github/workflows/ci-nightly.yml', event: 'schedule', conclusion: 'success', run_attempt: 1, head_sha: 'ccc', created_at: '2026-08-02T03:00:00Z' },
    { name: 'PR #1', path: 'dynamic/copilot-pull-request-reviewer/copilot-pull-request-reviewer', event: 'dynamic', conclusion: 'success', run_attempt: 1, head_sha: 'eee', created_at: '2026-08-02T04:00:00Z' },
    { name: 'PR #2', path: 'dynamic/copilot-pull-request-reviewer/copilot-pull-request-reviewer', event: 'dynamic', conclusion: 'success', run_attempt: 1, head_sha: 'fff', created_at: '2026-08-02T05:00:00Z' },
  ];
  const summary = summarizeRuns(runs);
  assert.equal(summary.total, 8);
  assert.equal(summary.reruns, 1);
  assert.equal(summary.rerunAttempts, 2);
  assert.equal(summary.cancelled, 2);
  assert.equal(summary.exactShaQualifiedOnMoreThanOneEvent, 1);
  assert.equal(summary.byWorkflow[ci].label, 'CI');
  assert.equal(summary.byWorkflow[ci].byEvent.pull_request, 3);
  assert.equal(summary.byWorkflow[ci].byEvent.push, 2);
  assert.equal(summary.byWorkflow[ci].rerunAttempts, 2);
  assert.equal(summary.byWorkflow['.github/workflows/ci-nightly.yml'].byEvent.schedule, 1);
  assert.equal(summary.byWorkflow['dynamic/copilot-pull-request-reviewer/copilot-pull-request-reviewer'].total, 2, 'per-run display names collapse into one workflow');
  assert.deepEqual(summary.perDay, { '2026-08-01': 2, '2026-08-02': 6 });
  const required = runs.filter((run) => workflowKey(run) === ci);
  assert.equal(completedRuns(required), 3, 'success + failure across events');
  assert.equal(completedRuns(required, 'pull_request'), 2, 'cancelled pushes never reduce the completed PR count');
});

test('validateWindow rejects impossible or reversed dates', () => {
  assert.deepEqual(validateWindow('2026-07-31', '2026-08-30'), []);
  assert.ok(validateWindow('2026-13-01', '2026-08-30').some((error) => error.includes('real calendar date')));
  assert.ok(validateWindow('2026-02-30', '2026-08-30').some((error) => error.includes('real calendar date')));
  assert.ok(validateWindow('2026-08-30', '2026-07-31').some((error) => error.includes('after')));
  assert.ok(validateWindow(undefined, '2026-08-30').some((error) => error.includes('YYYY-MM-DD')));
});

test('summarizeSample rolls up job means and OS totals', () => {
  const run = (seconds) => summarizeRunJobs([
    { name: 'Backend Unit / Backend Unit (windows-latest)', labels: ['windows-latest'], started_at: '2026-08-30T10:00:00Z', completed_at: new Date(Date.parse('2026-08-30T10:00:00Z') + seconds * 1000).toISOString(), conclusion: 'success' },
  ]);
  const sample = summarizeSample([run(120), run(240)]);
  assert.equal(sample.sampleSize, 2);
  assert.equal(sample.jobs[0].meanSeconds, 180);
  assert.equal(sample.jobs[0].maxSeconds, 240);
  assert.equal(sample.jobs[0].os, 'windows');
  assert.equal(sample.byOs.windows.meanBillableMinutesPerRun, 6);
  assert.equal(sample.billableMinutesPerRun.mean, 6);
});

test('summarizeArtifacts ignores expired artifacts and groups families', () => {
  const summary = summarizeArtifacts([
    { name: 'api-integration-trx-windows-latest', size_in_bytes: 1000, expired: false, created_at: '2026-08-10T00:00:00Z' },
    { name: 'api-integration-trx-ubuntu-latest', size_in_bytes: 500, expired: false, created_at: '2026-08-12T00:00:00Z' },
    { name: 'container-image-tar', size_in_bytes: 999999, expired: true, created_at: '2026-07-01T00:00:00Z' },
  ]);
  assert.equal(summary.listed, 3);
  assert.equal(summary.expiredCount, 1);
  assert.equal(summary.unexpiredCount, 2);
  assert.equal(summary.unexpiredBytes, 1500);
  assert.equal(summary.oldestUnexpiredCreatedAt, '2026-08-10T00:00:00Z');
  assert.equal(summary.topByBytes[0].name, 'api-integration-trx');
  assert.equal(summary.topByBytes[0].count, 2);
});

test('projectMonthlyAllowance reports overage against the Pro allowance', () => {
  assert.deepEqual(projectMonthlyAllowance(100, 20), { projectedBillableMinutes: 2000, includedMinutes: 3000, overageMinutes: 0 });
  assert.deepEqual(projectMonthlyAllowance(100, 50), { projectedBillableMinutes: 5000, includedMinutes: 3000, overageMinutes: 2000 });
});

test('renderMarkdown produces the ledger sections', () => {
  const report = {
    schemaVersion: 1,
    generatedAtUtc: '2026-08-30T18:00:00Z',
    repository: 'o/r',
    window: { since: '2026-07-31', until: '2026-08-30', days: 31 },
    method: { assumptions: ['a1'] },
    runs: summarizeRuns([{ name: 'CI', path: '.github/workflows/ci-required.yml', event: 'pull_request', conclusion: 'success', run_attempt: 1, head_sha: 'a', created_at: '2026-08-01T00:00:00Z' }]),
    sample: { workflowName: 'CI', ...summarizeSample([summarizeRunJobs([{ name: 'J', labels: ['ubuntu-latest'], started_at: '2026-08-30T10:00:00Z', completed_at: '2026-08-30T10:02:00Z', conclusion: 'success' }])]) },
    projections: [{ label: 'x', fullRunsPerMonth: 10, meanBillableMinutesPerRun: 2, ...projectMonthlyAllowance(2, 10) }],
    storage: { cache: { activeCachesSizeInBytes: 1e9, activeCachesCount: 3 }, artifacts: { ...summarizeArtifacts([]), truncated: false } },
  };
  const markdown = renderMarkdown(report);
  assert.match(markdown, /## Method and assumptions/);
  assert.match(markdown, /## Workflow runs in the window/);
  assert.match(markdown, /\| CI \| 1 \| 1 \|/);
  assert.match(markdown, /## Projection against the GitHub Pro allowance/);
  assert.match(markdown, /## Storage/);
  assert.match(markdown, /0\.93 GiB\*\* \(1\.00 GB decimal\)/);
});
