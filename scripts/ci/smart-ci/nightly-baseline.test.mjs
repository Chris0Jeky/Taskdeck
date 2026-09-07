import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import {
  MAX_ARTIFACT_BYTES,
  NIGHTLY_SUITE_IDS,
  NIGHTLY_PLAN_ARTIFACT,
  NIGHTLY_WORKFLOW,
  QUALITY_WORKFLOW,
  QUALITY_SUITE_IDS,
  REQUIRED_REAL_JOBS,
  artifactErrors,
  collectGitEvidence,
  isExplicitIsoTimestamp,
  jobSetErrors,
  paginate,
  planReceiptErrors,
  readPlanArtifact,
  selectQualifiedPair,
  workflowRunErrors,
} from './nightly-baseline.mjs';
import {
  NIGHTLY_COORDINATOR_KIND,
  NIGHTLY_COORDINATOR_SCHEMA_VERSION,
  NIGHTLY_DEEP_SUITES,
} from './nightly-coordinator.mjs';

const REPO = 'Chris0Jeky/Taskdeck';
const BRANCH = 'main';
const HEAD = 'a'.repeat(40);
const TREE = 'b'.repeat(40);
const OTHER_HEAD = 'c'.repeat(40);

function runFixture(id, workflow, overrides = {}) {
  return {
    id,
    name: workflow.name,
    path: workflow.path,
    event: 'schedule',
    head_branch: BRANCH,
    head_sha: HEAD,
    status: 'completed',
    conclusion: 'success',
    run_attempt: 2,
    updated_at: '2026-09-07T04:20:00Z',
    repository: { full_name: REPO },
    head_repository: { full_name: REPO },
    ...overrides,
  };
}

let nextJobId = 1;

function jobFixture(run, name, overrides = {}) {
  return {
    id: nextJobId++,
    run_id: run.id,
    run_attempt: run.run_attempt,
    head_sha: run.head_sha,
    name,
    status: 'completed',
    conclusion: 'success',
    completed_at: '2026-09-07T04:19:00Z',
    ...overrides,
  };
}

function jobsFor(run, suites, extras = []) {
  return [
    ...suites.flatMap((suite) => REQUIRED_REAL_JOBS[suite]).map((name) => jobFixture(run, name)),
    ...extras.map((name) => jobFixture(run, name)),
  ];
}

function artifactFixture(run, overrides = {}) {
  return {
    id: 77,
    name: NIGHTLY_PLAN_ARTIFACT,
    expired: false,
    size_in_bytes: 4096,
    workflow_run: { id: run.id, head_sha: run.head_sha },
    ...overrides,
  };
}

function planFixture(overrides = {}) {
  return {
    schemaVersion: NIGHTLY_COORDINATOR_SCHEMA_VERSION,
    kind: NIGHTLY_COORDINATOR_KIND,
    generatedAtUtc: '2026-09-07T03:25:00Z',
    verdict: 'full-sweep',
    reasons: ['last-receipt-missing'],
    current: { headSha: HEAD, treeSha: TREE },
    selectedSuites: [...NIGHTLY_DEEP_SUITES],
    skippedSuites: [],
    ...overrides,
  };
}

const nightlySuites = NIGHTLY_SUITE_IDS;
const qualitySuites = QUALITY_SUITE_IDS;

test('timestamps require an explicit Z or numeric offset', () => {
  assert.equal(isExplicitIsoTimestamp('2026-09-07T03:25:00Z'), true);
  assert.equal(isExplicitIsoTimestamp('2026-09-07T04:25:00+01:00'), true);
  assert.equal(isExplicitIsoTimestamp('2026-09-07T03:25:00'), false);
  assert.equal(isExplicitIsoTimestamp('Sunday'), false);
});

test('workflow run validation binds workflow identity, event, main and repository', () => {
  const valid = runFixture(11, NIGHTLY_WORKFLOW);
  assert.deepEqual(workflowRunErrors(valid, { repo: REPO, branch: BRANCH, workflow: NIGHTLY_WORKFLOW }), []);

  const wrong = {
    ...valid,
    name: QUALITY_WORKFLOW.name,
    path: QUALITY_WORKFLOW.path,
    event: 'pull_request',
    head_branch: 'feature',
    repository: { full_name: 'someone/fork' },
    head_repository: { full_name: 'someone/fork' },
  };
  assert.deepEqual(workflowRunErrors(wrong, { repo: REPO, branch: BRANCH, workflow: NIGHTLY_WORKFLOW }), [
    'head-branch-mismatch',
    'head-repository-mismatch',
    'repository-mismatch',
    'workflow-event-invalid',
    'workflow-name-mismatch',
    'workflow-path-mismatch',
  ]);
});

test('required real-job coverage rejects missing, skipped, wrong-attempt and unexpected jobs', () => {
  const run = runFixture(12, QUALITY_WORKFLOW);
  const complete = jobsFor(run, qualitySuites);
  assert.deepEqual(jobSetErrors(complete, run, qualitySuites), []);

  assert.ok(jobSetErrors(complete.slice(1), run, qualitySuites).some((error) => error.startsWith('required-job-missing:')));
  assert.ok(jobSetErrors(complete.map((job, index) => index === 0 ? { ...job, conclusion: 'skipped' } : job), run, qualitySuites)
    .some((error) => error.startsWith('job-not-success:')));
  assert.ok(jobSetErrors(complete.map((job, index) => index === 0 ? { ...job, run_attempt: 1 } : job), run, qualitySuites)
    .some((error) => error.startsWith('job-attempt-mismatch:')));
  assert.ok(jobSetErrors([...complete, jobFixture(run, 'Surprise job')], run, qualitySuites)
    .some((error) => error === 'unexpected-job:Surprise job'));
});

test('artifact metadata is head/run bound and size limited', () => {
  const run = runFixture(13, NIGHTLY_WORKFLOW);
  assert.deepEqual(artifactErrors(artifactFixture(run), run), []);
  assert.deepEqual(artifactErrors(artifactFixture(run, {
    size_in_bytes: MAX_ARTIFACT_BYTES + 1,
    workflow_run: { id: 999, head_sha: OTHER_HEAD },
  }), run), [
    'artifact-head-mismatch',
    'artifact-run-mismatch',
    'artifact-size-invalid',
  ]);
});

function pairHarness({ nightlyOverrides = {}, qualityOverrides = {}, mutateQualityJobs = (jobs) => jobs } = {}) {
  const nightly = runFixture(21, NIGHTLY_WORKFLOW, nightlyOverrides);
  const quality = runFixture(22, QUALITY_WORKFLOW, {
    updated_at: '2026-09-07T04:30:00Z',
    ...qualityOverrides,
  });
  const nightlyJobs = jobsFor(nightly, nightlySuites, ['Nightly coordinator observation']);
  const qualityJobs = mutateQualityJobs(jobsFor(quality, qualitySuites));
  return {
    nightly,
    quality,
    input: {
      repo: REPO,
      branch: BRANCH,
      currentRunId: 999,
      nightlyRuns: [nightly],
      qualityRuns: [quality],
      loadJobs: async (run) => run.id === nightly.id ? nightlyJobs : qualityJobs,
      loadArtifacts: async () => [artifactFixture(nightly)],
      loadCommit: async () => ({ sha: HEAD, tree: { sha: TREE } }),
    },
  };
}

test('one complete same-head nightly and quality pair advances a complete baseline', async () => {
  const { input } = pairHarness();
  const selected = await selectQualifiedPair(input);
  assert.deepEqual(selected, {
    available: true,
    repo: REPO,
    branch: BRANCH,
    headSha: HEAD,
    treeSha: TREE,
    completedAtUtc: '2026-09-07T04:30:00.000Z',
    nightlyRunId: 21,
    nightlyRunAttempt: 2,
    qualityRunId: 22,
    qualityRunAttempt: 2,
    artifactId: 77,
    artifactName: NIGHTLY_PLAN_ARTIFACT,
  });
});

test('missing, failed, skipped or mismatched-head quality evidence never advances the baseline', async () => {
  const cases = [
    { name: 'missing', transform: (input) => ({ ...input, qualityRuns: [] }) },
    { name: 'failed', transform: (input) => ({
      ...input,
      qualityRuns: input.qualityRuns.map((run) => ({ ...run, conclusion: 'failure' })),
    }) },
    { name: 'skipped job', harness: pairHarness({
      mutateQualityJobs: (jobs) => jobs.map((job, index) => index === 0 ? { ...job, conclusion: 'skipped' } : job),
    }) },
    { name: 'different head', transform: (input) => ({
      ...input,
      qualityRuns: input.qualityRuns.map((run) => ({ ...run, head_sha: OTHER_HEAD })),
    }) },
  ];
  for (const item of cases) {
    const source = item.harness ?? pairHarness();
    const input = item.transform ? item.transform(source.input) : source.input;
    await assert.rejects(() => selectQualifiedPair(input), (error) => {
      assert.equal(error.code, 'no-complete-nightly-pair', item.name);
      return true;
    });
  }
});

test('bounded pagination refuses partial metadata', async () => {
  let page = 0;
  const requestJson = async () => {
    const offset = page++ * 100;
    return { total_count: 250, workflow_runs: Array.from({ length: 100 }, (_, id) => ({ id: offset + id + 1 })) };
  };
  await assert.rejects(() => paginate(requestJson, '/runs', 'workflow_runs', 2), (error) => {
    assert.equal(error.code, 'metadata-pagination-limit');
    return true;
  });
});

test('plan receipt validation binds head/tree and covers all twelve suites', () => {
  const selection = { headSha: HEAD, treeSha: TREE };
  assert.deepEqual(planReceiptErrors(planFixture(), selection), []);
  assert.deepEqual(planReceiptErrors(planFixture({ current: { headSha: OTHER_HEAD, treeSha: TREE } }), selection), [
    'receipt-head-mismatch',
  ]);
  assert.deepEqual(planReceiptErrors(planFixture({ selectedSuites: NIGHTLY_DEEP_SUITES.slice(1) }), selection), [
    'receipt-suite-coverage-invalid',
  ]);
});

test('artifact reader accepts only one exact small JSON receipt', () => {
  const root = mkdtempSync(join(tmpdir(), 'taskdeck-nightly-artifact-'));
  const selection = { headSha: HEAD, treeSha: TREE };
  try {
    writeFileSync(join(root, 'nightly-plan.json'), `${JSON.stringify(planFixture())}\n`);
    assert.equal(readPlanArtifact(root, selection).current.headSha, HEAD);
    writeFileSync(join(root, 'extra.txt'), 'not read');
    assert.throws(() => readPlanArtifact(root, selection), (error) => error.code === 'artifact-files-invalid');
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('empty diff with a different current tree fails closed', () => {
  const baseline = { headSha: HEAD, treeSha: TREE };
  const currentHead = OTHER_HEAD;
  const currentTree = 'd'.repeat(40);
  const git = (_workspace, args) => {
    const key = args.join(' ');
    const values = new Map([
      ['rev-parse HEAD', { status: 0, stdout: `${currentHead}\n` }],
      ['rev-parse HEAD^{tree}', { status: 0, stdout: `${currentTree}\n` }],
      [`rev-parse ${HEAD}^{tree}`, { status: 0, stdout: `${TREE}\n` }],
      [`merge-base --is-ancestor ${HEAD} ${currentHead}`, { status: 0, stdout: '' }],
      [`diff --name-status --find-renames ${HEAD} ${currentHead}`, { status: 0, stdout: '' }],
    ]);
    return values.get(key) ?? { status: 1, stdout: '' };
  };
  assert.throws(
    () => collectGitEvidence({ workspace: '.', expectedCurrentHead: currentHead, baseline, git }),
    (error) => error.code === 'empty-diff-tree-mismatch',
  );
});

function jobBlock(workflowText, jobId) {
  const startMatch = new RegExp(`^  ${jobId}:\\r?$`, 'm').exec(workflowText);
  assert.ok(startMatch, `job ${jobId} exists`);
  const remainder = workflowText.slice(startMatch.index + startMatch[0].length);
  const nextMatch = /^  [A-Za-z0-9_-]+:\r?$/m.exec(remainder);
  return workflowText.slice(startMatch.index, nextMatch ? startMatch.index + startMatch[0].length + nextMatch.index : undefined);
}

test('workflow contract keeps both schedules and all deep jobs unconditional beside the observer', () => {
  const nightlyPath = fileURLToPath(new URL('../../../.github/workflows/ci-nightly.yml', import.meta.url));
  const qualityPath = fileURLToPath(new URL('../../../.github/workflows/nightly-quality.yml', import.meta.url));
  const crossBrowserPath = fileURLToPath(new URL('../../../.github/workflows/reusable-e2e-cross-browser.yml', import.meta.url));
  const nightly = readFileSync(nightlyPath, 'utf8');
  const quality = readFileSync(qualityPath, 'utf8');
  const crossBrowser = readFileSync(crossBrowserPath, 'utf8');

  assert.match(nightly, /- cron: '25 3 \* \* \*'/);
  assert.match(quality, /- cron: '55 3 \* \* \*'/);
  for (const suite of nightlySuites) assert.doesNotMatch(jobBlock(nightly, suite), /^    if:/m, suite);
  for (const suite of qualitySuites) assert.doesNotMatch(jobBlock(quality, suite), /^    if:/m, suite);
  assert.match(jobBlock(nightly, 'nightly-coordinator'), /name: Nightly coordinator observation/);
  assert.match(jobBlock(nightly, 'nightly-coordinator'), /node scripts\/ci\/smart-ci\/nightly-baseline\.mjs/);
  assert.match(jobBlock(nightly, 'nightly-coordinator'), /name: nightly-coordinator-plan/);
  assert.match(nightly, /^  actions: read$/m);
  assert.match(nightly, /OBSERVER_REPO: \$\{\{ github\.repository \}\}/);
  assert.match(nightly, /OBSERVER_EVENT: \$\{\{ github\.event_name \}\}/);
  assert.match(nightly, /OBSERVER_REF: \$\{\{ github\.ref \}\}/);
  assert.match(nightly, /OBSERVER_WORKFLOW: \$\{\{ github\.workflow \}\}/);
  assert.doesNotMatch(quality, /nightly-baseline\.mjs|nightly-coordinator-plan/);

  const matrixProjects = [...crossBrowser.matchAll(/^          - project: (\S+)$/gm)].map((match) => match[1]);
  assert.deepEqual(matrixProjects, ['chromium', 'firefox', 'webkit', 'mobile-chrome', 'mobile-safari']);
  assert.deepEqual(REQUIRED_REAL_JOBS['e2e-cross-browser'], matrixProjects.map(
    (project) => `E2E Cross-Browser Matrix / E2E (${project})`,
  ));
  assert.deepEqual(Object.keys(REQUIRED_REAL_JOBS), NIGHTLY_DEEP_SUITES);
  assert.deepEqual([...NIGHTLY_SUITE_IDS, ...QUALITY_SUITE_IDS], NIGHTLY_DEEP_SUITES);

  const repoRoot = resolve(dirname(nightlyPath), '..', '..');
  const actualRealJobs = (workflowText, suite) => {
    const block = jobBlock(workflowText, suite);
    const outer = /^    name: (.+)$/m.exec(block)?.[1];
    assert.ok(outer, `${suite} has an outer job name`);
    const calledPath = /^    uses: (\.\/\.github\/workflows\/\S+)$/m.exec(block)?.[1];
    if (!calledPath) return [outer];
    const called = readFileSync(resolve(repoRoot, calledPath), 'utf8');
    const innerNames = [...called.matchAll(/^    name: (.+)$/gm)].map((match) => match[1]);
    assert.ok(innerNames.length > 0, `${suite} reusable workflow has named jobs`);
    return innerNames.flatMap((inner) => {
      if (!inner.includes('${{ matrix.project }}')) return [`${outer} / ${inner}`];
      return matrixProjects.map((project) => `${outer} / ${inner.replace('${{ matrix.project }}', project)}`);
    });
  };
  for (const suite of nightlySuites) {
    assert.deepEqual(REQUIRED_REAL_JOBS[suite], actualRealJobs(nightly, suite), `${suite} real jobs drifted`);
  }
  for (const suite of qualitySuites) {
    assert.deepEqual(REQUIRED_REAL_JOBS[suite], actualRealJobs(quality, suite), `${suite} real jobs drifted`);
  }
});
