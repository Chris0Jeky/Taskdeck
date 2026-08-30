import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { buildPlan, policyDigest } from './lib/plan.mjs';
import {
  buildRecallReport,
  exitCodeForReport,
  nextLink,
  normaliseObservation,
  parseArgs,
  renderRecallMarkdown,
} from './recall-report.mjs';

const repository = 'Chris0Jeky/Taskdeck';
const policyText = readFileSync(new URL('../../../ci/policy.v1.json', import.meta.url), 'utf8');
const policy = JSON.parse(policyText);
const digest = policyDigest(policyText);
const window = {
  repository,
  since: '2026-08-30T20:18:21.000Z',
  until: '2026-08-31T20:18:21.000Z',
  minimumObservations: 20,
  policyDigest: digest,
};

const shaFor = (number) => number.toString(16).padStart(40, '0');

function planFor(prNumber, headSha, options = {}) {
  return buildPlan({
    eventName: 'pull_request_target',
    repository,
    repositoryOwnerLogin: 'Chris0Jeky',
    pullRequestNumber: prNumber,
    ref: 'main',
    isDraft: false,
    baseSha: options.baseSha,
    headSha,
    mergeSha: options.mergeSha,
    mergeTreeSha: options.mergeTreeSha,
    actorLogin: 'Chris0Jeky',
    actorType: 'User',
    authorAssociation: 'OWNER',
    senderLogin: 'Chris0Jeky',
    senderType: 'User',
    eventAction: 'synchronize',
    headActors: ['Chris0Jeky'],
    headActorsKnown: true,
    isFork: false,
    labels: [],
    changedFiles: ['docs/example.md'],
    changedFileRows: 1,
    changedFilesAvailable: true,
    changedFilesExpected: 1,
    notes: [],
    executionMode: 'hosted',
  }, policy, digest);
}

function observation(prNumber, options = {}) {
  const headSha = options.headSha ?? shaFor(prNumber);
  const finalHeadSha = options.finalHeadSha ?? headSha;
  const failedCheckName = options.failedCheckName ?? null;
  const mergedAt = options.mergedAt ?? '2026-08-31T12:00:00.000Z';
  const headBranch = `issue-${prNumber}/fixture`;
  const baseSha = options.baseSha ?? shaFor(100000 + prNumber);
  const planBaseSha = options.planBaseSha ?? baseSha;
  const planMergeSha = options.planMergeSha ?? shaFor(200000 + prNumber);
  const planMergeTreeSha = options.planMergeTreeSha ?? shaFor(300000 + prNumber);
  const mergeCommitSha = options.mergeCommitSha ?? shaFor(400000 + prNumber);
  const mergeTreeSha = options.mergeTreeSha ?? planMergeTreeSha;
  const shadowRunId = options.shadowRunId ?? 10000 + prNumber;
  const requiredRunId = options.requiredRunId ?? 20000 + prNumber;
  const runAttempt = options.runAttempt ?? 1;
  const raw = {
    repository,
    prNumber,
    mergedAt,
    headSha,
    finalHeadSha,
    headBranch,
    headRepository: repository,
    baseSha,
    baseBranch: 'main',
    baseRepository: repository,
    mergeCommitSha,
    mergeCommit: {
      sha: mergeCommitSha,
      parents: [baseSha, finalHeadSha],
      treeSha: mergeTreeSha,
    },
    headPullRequests: [prNumber],
    artifact: {
      id: 9000 + prNumber,
      name: `smart-ci-plan-${prNumber}-${headSha}`,
      expired: false,
      workflowRunId: shadowRunId,
      headSha,
      headBranch,
      createdAt: '2026-08-31T09:02:00.000Z',
      updatedAt: '2026-08-31T09:03:00.000Z',
    },
    shadowRun: {
      id: shadowRunId,
      path: '.github/workflows/smart-ci-shadow.yml',
      event: 'pull_request_target',
      status: 'completed',
      conclusion: 'success',
      headSha,
      headBranch,
      headRepository: repository,
      createdAt: '2026-08-31T09:00:00.000Z',
      updatedAt: '2026-08-31T09:05:00.000Z',
      pullRequests: [prNumber],
    },
    plan: planFor(prNumber, headSha, { baseSha: planBaseSha, mergeSha: planMergeSha, mergeTreeSha: planMergeTreeSha }),
    planMergeCommit: {
      sha: planMergeSha,
      parents: [planBaseSha, headSha],
      treeSha: planMergeTreeSha,
    },
    requiredRun: {
      id: requiredRunId,
      path: '.github/workflows/ci-required.yml',
      event: 'pull_request',
      status: 'completed',
      conclusion: failedCheckName ? 'failure' : 'success',
      headSha,
      headBranch,
      headRepository: repository,
      triggerCreatedAt: '2026-08-31T09:01:00.000Z',
      createdAt: '2026-08-31T10:00:00.000Z',
      updatedAt: '2026-08-31T11:00:00.000Z',
      runAttempt,
      pullRequests: [prNumber],
    },
    jobs: failedCheckName
      ? [{ name: failedCheckName, status: 'completed', conclusion: options.jobConclusion ?? 'failure', runId: requiredRunId, runAttempt, headSha }]
      : [
        { name: 'Docs Governance / Docs Governance', status: 'completed', conclusion: 'success', runId: requiredRunId, runAttempt, headSha },
        { name: 'Backend Unit / Backend Unit (ubuntu-latest)', status: 'completed', conclusion: 'skipped', runId: requiredRunId, runAttempt, headSha },
      ],
  };
  if (typeof options.mutate === 'function') options.mutate(raw);
  return raw;
}

test('a failed selected lane is covered and a failed skipped lane is a recall miss', () => {
  const covered = normaliseObservation(
    observation(1, { failedCheckName: 'Docs Governance / Docs Governance' }),
    policy,
    window,
  );
  assert.equal(covered.usable, true);
  assert.equal(covered.covered, true);
  assert.deepEqual(covered.missedLanes, []);
  assert.equal(covered.failedLanes[0].lane, 'docs-governance');

  const missed = normaliseObservation(
    observation(2, { failedCheckName: 'Backend Unit / Backend Unit (ubuntu-latest)' }),
    policy,
    window,
  );
  assert.equal(missed.usable, true);
  assert.equal(missed.covered, false);
  assert.equal(missed.missedLanes[0].lane, 'backend-unit-linux');
});

test('successful and skipped jobs are not failures', () => {
  const result = normaliseObservation(observation(3), policy, window);
  assert.equal(result.usable, true);
  assert.deepEqual(result.failedLanes, []);
  assert.deepEqual(result.missedLanes, []);
});

test('planner errors, invalid plans and policy drift make observations unusable', () => {
  const plannerError = observation(4, { mutate: (raw) => { raw.plan.plannerError = { name: 'Error', message: 'canary' }; } });
  assert.equal(normaliseObservation(plannerError, policy, window).usable, false);
  assert(normaliseObservation(plannerError, policy, window).errors.includes('planner-error'));

  const invalid = observation(5, { mutate: (raw) => { raw.plan.selected = []; } });
  assert(normaliseObservation(invalid, policy, window).errors.some((error) => error.startsWith('plan-invalid:')));

  const drifted = observation(6, { mutate: (raw) => { raw.plan.policyDigest = `sha256:${'0'.repeat(64)}`; } });
  assert(normaliseObservation(drifted, policy, window).errors.includes('policy-digest-mismatch'));
});

test('artifact, PR and SHA bindings fail closed', () => {
  const noArtifact = observation(7, { mutate: (raw) => { raw.artifact = null; } });
  assert(normaliseObservation(noArtifact, policy, window).errors.includes('plan-artifact-missing'));

  const wrongArtifact = observation(8, { mutate: (raw) => { raw.artifact.name = 'smart-ci-plan-wrong'; } });
  assert(normaliseObservation(wrongArtifact, policy, window).errors.includes('plan-artifact-name-mismatch'));

  const ambiguousExpiry = observation(20, { mutate: (raw) => { delete raw.artifact.expired; } });
  assert(normaliseObservation(ambiguousExpiry, policy, window).errors.includes('plan-artifact-expiry-invalid'));

  const wrongPlanPr = observation(9, { mutate: (raw) => { raw.plan.event.pullRequest = 999; } });
  assert(normaliseObservation(wrongPlanPr, policy, window).errors.includes('plan-pr-number-mismatch'));

  const wrongRunSha = observation(10, { mutate: (raw) => { raw.requiredRun.headSha = 'f'.repeat(40); } });
  assert(normaliseObservation(wrongRunSha, policy, window).errors.includes('required-head-sha-mismatch'));
});

test('incomplete, cancelled-only and temporally invalid required evidence is unusable', () => {
  const incomplete = observation(11, { mutate: (raw) => { raw.requiredRun.status = 'in_progress'; raw.jobs[0].status = 'in_progress'; } });
  const incompleteResult = normaliseObservation(incomplete, policy, window);
  assert(incompleteResult.errors.includes('required-run-incomplete'));
  assert(incompleteResult.errors.some((error) => error.startsWith('required-job-incomplete:')));

  const cancelledRun = observation(12, { mutate: (raw) => { raw.requiredRun.conclusion = 'cancelled'; } });
  assert(normaliseObservation(cancelledRun, policy, window).errors.includes('required-run-conclusion-unusable'));

  const timedOutRun = observation(19, {
    failedCheckName: 'Docs Governance / Docs Governance',
    jobConclusion: 'timed_out',
    mutate: (raw) => { raw.requiredRun.conclusion = 'timed_out'; },
  });
  assert.equal(normaliseObservation(timedOutRun, policy, window).usable, true);

  const afterMerge = observation(13, { mutate: (raw) => { raw.requiredRun.createdAt = '2026-09-01T00:00:00.000Z'; } });
  assert(normaliseObservation(afterMerge, policy, window).errors.includes('required-run-after-merge'));

  const shadowAfterMerge = observation(17, { mutate: (raw) => { raw.shadowRun.updatedAt = '2026-09-01T00:00:00.000Z'; } });
  assert(normaliseObservation(shadowAfterMerge, policy, window).errors.includes('shadow-run-after-merge'));

  const beforeWindow = observation(18, { mutate: (raw) => {
    raw.shadowRun.createdAt = '2026-08-30T19:00:00.000Z';
    raw.shadowRun.updatedAt = '2026-08-30T19:05:00.000Z';
    raw.requiredRun.triggerCreatedAt = '2026-08-30T19:01:00.000Z';
    raw.requiredRun.createdAt = '2026-08-30T19:06:00.000Z';
    raw.requiredRun.updatedAt = '2026-08-30T19:10:00.000Z';
  } });
  const beforeWindowResult = normaliseObservation(beforeWindow, policy, window);
  assert(beforeWindowResult.errors.includes('shadow-run-outside-window'));
  assert(beforeWindowResult.errors.includes('required-run-outside-window'));
});

test('unknown failed job names and inconsistent workflow conclusions fail closed', () => {
  const unknown = observation(14, { failedCheckName: 'Unmapped Required Job' });
  assert(normaliseObservation(unknown, policy, window).errors.includes('unknown-failed-job:Unmapped Required Job'));

  const successWithFailure = observation(15, {
    failedCheckName: 'Docs Governance / Docs Governance',
    mutate: (raw) => { raw.requiredRun.conclusion = 'success'; },
  });
  assert(normaliseObservation(successWithFailure, policy, window).errors.includes('required-run-success-with-failed-job'));

  const failedWithoutJob = observation(16, { mutate: (raw) => { raw.requiredRun.conclusion = 'failure'; } });
  assert(normaliseObservation(failedWithoutJob, policy, window).errors.includes('required-run-failed-without-failed-job'));
});

test('nineteen observations stay non-ready and twenty covered observations become ready', () => {
  const nineteen = Array.from({ length: 19 }, (_, index) => observation(100 + index, {
    failedCheckName: index === 0 ? 'Docs Governance / Docs Governance' : null,
  }));
  nineteen.push(observation(100, { runAttempt: 2 }));
  const report19 = buildRecallReport(nineteen, policy, window);
  assert.equal(report19.usableObservationCount, 19, JSON.stringify(report19.pullRequests.filter((pull) => !pull.usable)));
  assert.equal(report19.readyForSelection, false);
  assert(report19.readinessReasons.includes('insufficient-observations'));
  assert.equal(exitCodeForReport(report19), 2);

  const twenty = [...nineteen, observation(119)];
  const report20 = buildRecallReport(twenty, policy, window);
  assert.equal(report20.usableObservationCount, 20);
  assert.equal(report20.failedLaneCount, 1);
  assert.equal(report20.missedFailureCount, 0);
  assert.equal(report20.recall, 1);
  assert.equal(report20.readyForSelection, true);
  assert.equal(exitCodeForReport(report20), 0);
});

test('missed failures and unusable observations have distinct non-zero exits', () => {
  const missed = Array.from({ length: 20 }, (_, index) => observation(200 + index, {
    failedCheckName: index === 0 ? 'Backend Unit / Backend Unit (ubuntu-latest)' : null,
  }));
  missed.push(observation(200, { runAttempt: 2 }));
  const missedReport = buildRecallReport(missed, policy, window);
  assert.equal(missedReport.missedFailureCount, 1);
  assert.equal(missedReport.readyForSelection, false);
  assert.equal(exitCodeForReport(missedReport), 3);

  const unusable = [...missed.filter((raw) => raw.prNumber !== 219), observation(220, { mutate: (raw) => { raw.artifact = null; } })];
  const unusableReport = buildRecallReport(unusable, policy, window);
  assert.equal(unusableReport.unusableObservationCount, 1);
  assert.equal(exitCodeForReport(unusableReport), 1);
});

test('twenty green observations do not turn zero-failure families into evidence', () => {
  const green = Array.from({ length: 20 }, (_, index) => observation(300 + index));
  const report = buildRecallReport(green, policy, window);
  assert.equal(report.sampleComplete, true);
  assert.equal(report.recall, null);
  assert.equal(report.readyForSelection, false);
  assert(report.readinessReasons.includes('no-failure-evidence'));
  assert(report.familyStats.every((family) => family.recall === null && family.readyForSelection === false));
  assert.equal(exitCodeForReport(report), 2);
});

test('the minimum is twenty unique merged PRs, not duplicated rows or attempts', () => {
  const one = observation(350);
  const duplicated = buildRecallReport(Array.from({ length: 20 }, () => structuredClone(one)), policy, window);
  assert.equal(duplicated.observationCount, 1);
  assert.equal(duplicated.revisionObservationCount, 20);
  assert.equal(duplicated.unusableObservationCount, 1);
  assert(duplicated.observations.every((row) => row.errors.includes('duplicate-observation')));
  assert.equal(exitCodeForReport(duplicated), 1);

  const attempts = Array.from({ length: 20 }, (_, index) => observation(351, { runAttempt: index + 1 }));
  const onePr = buildRecallReport(attempts, policy, window);
  assert.equal(onePr.observationCount, 1);
  assert.equal(onePr.usableObservationCount, 1);
  assert.equal(onePr.revisionObservationCount, 20);
  assert.equal(onePr.sampleComplete, false);
  assert(onePr.readinessReasons.includes('insufficient-observations'));
  assert.equal(exitCodeForReport(onePr), 2);
  assert.throws(() => buildRecallReport([], policy, { ...window, minimumObservations: 19 }), />= 20/);
});

test('an earlier-head failed attempt contributes recall while the final head proves mergeability', () => {
  const prNumber = 360;
  const finalHeadSha = shaFor(900360);
  const landedTree = shaFor(900361);
  const earlier = observation(prNumber, {
    headSha: shaFor(900359),
    finalHeadSha,
    requiredRunId: 700360,
    failedCheckName: 'Docs Governance / Docs Governance',
    planMergeSha: shaFor(800360),
    planMergeTreeSha: shaFor(800361),
    mergeTreeSha: landedTree,
  });
  const final = observation(prNumber, {
    headSha: finalHeadSha,
    finalHeadSha,
    requiredRunId: 700361,
    planMergeSha: shaFor(800362),
    planMergeTreeSha: landedTree,
    mergeTreeSha: landedTree,
  });
  const otherPulls = Array.from({ length: 19 }, (_, index) => observation(361 + index));
  const report = buildRecallReport([earlier, final, ...otherPulls], policy, window);
  assert.equal(report.observationCount, 20);
  assert.equal(report.revisionObservationCount, 21);
  assert.equal(report.pullRequests.find((pull) => pull.prNumber === prNumber).revisionCount, 2);
  assert.equal(report.failedLaneCount, 1);
  assert.equal(report.missedFailureCount, 0);
  assert.equal(report.readyForSelection, true);
  assert.equal(exitCodeForReport(report), 0);
});

test('a merged PR without a usable successful final-head attempt fails closed', () => {
  const noFinalSuccess = observation(381, { failedCheckName: 'Docs Governance / Docs Governance' });
  const otherPulls = Array.from({ length: 19 }, (_, index) => observation(382 + index));
  const report = buildRecallReport([noFinalSuccess, ...otherPulls], policy, window);
  const pull = report.pullRequests.find((entry) => entry.prNumber === 381);
  assert.equal(pull.usable, false);
  assert(pull.errors.includes('final-head-success-missing'));
  assert.equal(report.unusableObservationCount, 1);
  assert.equal(exitCodeForReport(report), 1);
});

test('one PR cannot combine attempts from different branch identities', () => {
  const first = observation(380, { runAttempt: 1 });
  const second = observation(380, { runAttempt: 2, mutate: (raw) => {
    raw.headBranch = 'other/branch';
    raw.artifact.headBranch = raw.headBranch;
    raw.shadowRun.headBranch = raw.headBranch;
    raw.requiredRun.headBranch = raw.headBranch;
  } });
  const report = buildRecallReport([first, second], policy, window);
  assert.equal(report.observationCount, 1);
  assert.equal(report.unusableObservationCount, 1);
  assert(report.pullRequests[0].errors.includes('pr-head-branch-mismatch'));
});

test('plan, landed merge, PR and temporal bindings fail closed on mismatch', () => {
  const planParent = observation(390, { mutate: (raw) => { raw.planMergeCommit.parents[1] = 'f'.repeat(40); } });
  assert(normaliseObservation(planParent, policy, window).errors.includes('plan-merge-commit-parents-mismatch'));

  const planTree = observation(391, { mutate: (raw) => { raw.planMergeCommit.treeSha = 'f'.repeat(40); } });
  assert(normaliseObservation(planTree, policy, window).errors.includes('plan-merge-commit-tree-mismatch'));

  const landedParent = observation(392, { mutate: (raw) => { raw.mergeCommit.parents[0] = 'f'.repeat(40); } });
  assert(normaliseObservation(landedParent, policy, window).errors.includes('merge-commit-parents-mismatch'));

  const finalBase = observation(393, { mutate: (raw) => {
    raw.plan.baseSha = 'f'.repeat(40);
    raw.planMergeCommit.parents[0] = 'f'.repeat(40);
  } });
  assert(normaliseObservation(finalBase, policy, window).errors.includes('final-plan-base-sha-mismatch'));

  const correlation = observation(394, { mutate: (raw) => { raw.requiredRun.triggerCreatedAt = '2026-08-31T10:00:00.000Z'; } });
  assert(normaliseObservation(correlation, policy, window).errors.includes('required-shadow-run-time-mismatch'));

  const outsideWindow = observation(395, { mergedAt: '2026-09-01T12:00:00.000Z' });
  assert(normaliseObservation(outsideWindow, policy, window).errors.includes('merged-at-outside-window'));
});

test('present PR association metadata must match, while GitHub empty arrays remain explicit', () => {
  const mismatched = observation(396, { mutate: (raw) => {
    raw.headPullRequests = [999];
    raw.shadowRun.pullRequests = [999];
    raw.requiredRun.pullRequests = [999];
  } });
  const mismatchResult = normaliseObservation(mismatched, policy, window);
  assert(mismatchResult.errors.includes('head-pr-mismatch'));
  assert(mismatchResult.errors.includes('shadow-run-pr-mismatch'));
  assert(mismatchResult.errors.includes('required-run-pr-mismatch'));

  const empty = observation(397, { mutate: (raw) => {
    raw.headPullRequests = [];
    raw.shadowRun.pullRequests = [];
    raw.requiredRun.pullRequests = [];
  } });
  assert.equal(normaliseObservation(empty, policy, window).usable, true);
});

test('reports and Markdown are byte-deterministic and observations sort by PR number', () => {
  const fixtures = Array.from({ length: 20 }, (_, index) => observation(401 + index));
  const first = buildRecallReport(fixtures, policy, window);
  const second = buildRecallReport([...fixtures].reverse(), policy, window);
  assert.deepEqual(first.observations.slice(0, 3).map((entry) => entry.prNumber), [401, 402, 403]);
  assert.equal(JSON.stringify(first), JSON.stringify(second));
  assert.equal(renderRecallMarkdown(first), renderRecallMarkdown(second));
  assert.equal(Object.hasOwn(first, 'generatedAtUtc'), false);
});

test('argument and pagination parsers reject ambiguous inputs', () => {
  const args = parseArgs([
    '--repo', repository,
    '--since', '2026-08-30T20:18:21Z',
    '--until', '2026-08-31T20:18:21Z',
    '--min-observations', '20',
    '--input', 'fixtures.json',
    '--out-json', 'report.json',
    '--out-md', 'report.md',
  ]);
  assert.equal(args.since, '2026-08-30T20:18:21.000Z');
  assert.equal(args.minimumObservations, 20);
  assert.throws(() => buildRecallReport([], policy, { ...window, repository: null }), /repository must be owner\/name/);
  assert.throws(() => buildRecallReport([], policy, { ...window, policyDigest: null }), /policyDigest/);
  assert.throws(() => buildRecallReport([], policy, { ...window, since: window.until, until: window.since }), /since must not be after until/);
  assert.throws(() => parseArgs(['--since', 'invalid', '--until', '2026-08-31']), /ISO-8601/);
  assert.throws(() => parseArgs(['--since', '2026-09-01', '--until', '2026-08-31']), /must not be after/);
  assert.throws(() => parseArgs(['--since', '2026-08-30', '--until', '2026-08-31', '--min-observations', '19']), />= 20/);
  assert.throws(() => parseArgs(['--since', '2026-08-30', '--until', '2026-08-31', '--unknown']), /Unknown argument/);
  assert.equal(nextLink('<https://api.github.com/x?page=2>; rel="next", <https://api.github.com/x?page=9>; rel="last"'), 'https://api.github.com/x?page=2');
  assert.equal(nextLink('<https://api.github.com/x?page=1>; rel="prev"'), null);
});
