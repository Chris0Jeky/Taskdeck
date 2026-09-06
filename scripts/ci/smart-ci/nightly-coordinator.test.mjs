// CI-10 slice 1 (#2334): the nightly coordinator decision module. Covers the issue's head-start
// test plan in docs/analysis/2026-08-30-acceleration-bundle/issues/2334-*.md plus the policy-group
// enumeration that keeps GROUP_DEEP_SUITES honest as `ci/policy.v1.json` grows.

import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { policyDigest } from './lib/plan.mjs';
import {
  GROUP_DEEP_SUITES,
  NIGHTLY_COORDINATOR_KIND,
  NIGHTLY_COORDINATOR_SCHEMA_VERSION,
  NIGHTLY_DEEP_SUITES,
  REASONS,
  SKIP_REASONS,
  SUITE_PREREQUISITES,
  closeUnderPrerequisites,
  decideNightlyPlan,
  detectDuplicateQualification,
  nightlyCoordinatorMappingErrors,
  nightlyCoordinatorSuiteGraphErrors,
  normaliseLastQualified,
  parseArgs,
  parseChangedFileList,
  renderNightlySummary,
} from './nightly-coordinator.mjs';

const policyPath = fileURLToPath(new URL('../../../ci/policy.v1.json', import.meta.url));
const policyText = readFileSync(policyPath, 'utf8');
const policy = JSON.parse(policyText);
const digest = policyDigest(policyText);

const HEAD = 'a'.repeat(40);
const TREE = 'b'.repeat(40);
const LAST_HEAD = 'c'.repeat(40);
const LAST_TREE = 'd'.repeat(40);
// 2026-09-03 is a Thursday (UTC day 4); 2026-09-05 is a Saturday (UTC day 6).
const THURSDAY = '2026-09-03T03:25:00.000Z';
const SATURDAY = '2026-09-05T03:25:00.000Z';

function lastReceipt(overrides = {}) {
  return {
    headSha: LAST_HEAD,
    treeSha: LAST_TREE,
    completedAtUtc: '2026-09-02T03:59:00.000Z',
    complete: true,
    ...overrides,
  };
}

function coordinatorInput(overrides = {}) {
  return {
    policy,
    policyDigest: digest,
    lastQualified: lastReceipt(),
    headSha: HEAD,
    treeSha: TREE,
    changedFiles: [],
    nowUtc: THURSDAY,
    weeklySlotUtcDay: 6,
    forceFull: false,
    ...overrides,
  };
}

test('the checked-in policy sanity: the fixture SHAs and the weekly slot days are what the tests assume', () => {
  assert.equal(new Date(THURSDAY).getUTCDay(), 4);
  assert.equal(new Date(SATURDAY).getUTCDay(), 6);
  assert.equal(NIGHTLY_DEEP_SUITES.length, 12);
  assert.equal(new Set(NIGHTLY_DEEP_SUITES).size, 12);
});

test('no relevant change gives no-change and a receipt naming both SHAs', () => {
  const receipt = decideNightlyPlan(coordinatorInput({
    treeSha: LAST_TREE,
    changedFiles: [],
  }));

  assert.equal(receipt.schemaVersion, NIGHTLY_COORDINATOR_SCHEMA_VERSION);
  assert.equal(receipt.kind, NIGHTLY_COORDINATOR_KIND);
  assert.equal(receipt.verdict, 'no-change');
  assert.deepEqual(receipt.reasons, [REASONS.identicalTree]);
  assert.equal(receipt.current.headSha, HEAD);
  assert.equal(receipt.current.treeSha, LAST_TREE);
  assert.equal(receipt.lastQualified.headSha, LAST_HEAD);
  assert.equal(receipt.lastQualified.treeSha, LAST_TREE);
  assert.equal(receipt.lastQualifiedUnavailableReason, null);
  assert.deepEqual(receipt.selectedSuites, []);
  // Invariant 1: an explicit receipt for every suite, not a skipped workflow.
  assert.equal(receipt.skippedSuites.length, NIGHTLY_DEEP_SUITES.length);
  for (const entry of receipt.skippedSuites) assert.equal(entry.reason, SKIP_REASONS.noChange);
  assert.equal(receipt.generatedAtUtc, THURSDAY);
  assert.equal(receipt.policyId, policy.policyId);
  assert.equal(receipt.policyDigest, digest);
});

test('a docs-only change maps to a group with no deep suite and is also no-change', () => {
  const receipt = decideNightlyPlan(coordinatorInput({
    changedFiles: ['docs/STATUS.md', 'autodoc/AGENT_INDEX.md'],
  }));

  assert.equal(receipt.verdict, 'no-change');
  assert.deepEqual(receipt.reasons, [REASONS.noDeepSuiteGroups]);
  assert.deepEqual(receipt.matchedGroups, ['docs']);
  assert.deepEqual(receipt.selectedSuites, []);
  assert.equal(receipt.changedFileCount, 2);
});

test('a backend-only change selects the backend deep suites and not the browser matrix', () => {
  const receipt = decideNightlyPlan(coordinatorInput({
    changedFiles: [
      'backend/src/Taskdeck.Domain/Boards/Board.cs',
      'backend/src/Taskdeck.Application/Boards/CreateBoardHandler.cs',
    ],
  }));

  assert.equal(receipt.verdict, 'affected');
  assert.deepEqual(receipt.reasons, [REASONS.affectedGroups]);
  assert.deepEqual(receipt.matchedGroups, ['backend-application', 'backend-domain']);
  assert.deepEqual(receipt.selectedSuites, [
    'backend-solution',
    'load-concurrency-harness',
    'performance-regression-gate',
    'container-images',
    'backend-coverage',
  ]);
  assert.ok(!receipt.selectedSuites.includes('e2e-cross-browser'));
  assert.ok(!receipt.selectedSuites.includes('e2e-smoke'));
  assert.ok(!receipt.selectedSuites.includes('frontend-coverage'));
  const skipped = new Map(receipt.skippedSuites.map((entry) => [entry.suite, entry.reason]));
  assert.equal(skipped.get('e2e-cross-browser'), SKIP_REASONS.notAffected);
});

test('a frontend-only change selects the browser suites plus the backend-solution they need', () => {
  const receipt = decideNightlyPlan(coordinatorInput({
    changedFiles: ['frontend/taskdeck-web/src/views/BoardView.vue'],
  }));

  assert.equal(receipt.verdict, 'affected');
  assert.deepEqual(receipt.matchedGroups, ['frontend-src']);
  // `e2e-smoke` and `e2e-cross-browser` declare `needs: backend-solution` in ci-nightly.yml, so a
  // receipt that promised them without it would promise evidence GitHub Actions would skip.
  assert.deepEqual(receipt.selectedSuites, [
    'backend-solution',
    'e2e-smoke',
    'e2e-cross-browser',
    'frontend-coverage',
  ]);
  assert.ok(!receipt.selectedSuites.includes('backend-coverage'));
});

test('the weekly slot forces weekly-full even when the diff is empty', () => {
  const receipt = decideNightlyPlan(coordinatorInput({
    nowUtc: SATURDAY,
    weeklySlotUtcDay: 6,
    changedFiles: [],
  }));

  assert.equal(receipt.verdict, 'weekly-full');
  assert.deepEqual(receipt.reasons, [REASONS.weeklySlot]);
  assert.deepEqual(receipt.weeklySlot, { utcDay: 6, nowUtcDay: 6, matched: true });
  assert.deepEqual(receipt.selectedSuites, [...NIGHTLY_DEEP_SUITES]);
  assert.deepEqual(receipt.skippedSuites, []);
});

test('the weekly slot forces weekly-full even when the tree SHA is unchanged', () => {
  const receipt = decideNightlyPlan(coordinatorInput({
    nowUtc: SATURDAY,
    weeklySlotUtcDay: 6,
    treeSha: LAST_TREE,
    changedFiles: [],
  }));

  assert.equal(receipt.verdict, 'weekly-full');
  assert.equal(receipt.duplicateQualification, true);
  assert.deepEqual(receipt.selectedSuites, [...NIGHTLY_DEEP_SUITES]);
});

test('an out-of-range or unparseable weekly slot fails closed rather than disabling the sweep', () => {
  for (const slot of [7, -1, 1.5, Number.NaN, 'saturday']) {
    const receipt = decideNightlyPlan(coordinatorInput({ weeklySlotUtcDay: slot }));
    assert.equal(receipt.verdict, 'full-sweep', `slot ${String(slot)}`);
    assert.ok(receipt.reasons.includes(REASONS.weeklySlotInvalid), `slot ${String(slot)}`);
  }
  const unconfigured = decideNightlyPlan(coordinatorInput({ weeklySlotUtcDay: null }));
  assert.equal(unconfigured.verdict, 'no-change');
  assert.deepEqual(unconfigured.weeklySlot, { utcDay: null, nowUtcDay: 4, matched: false });
});

test('a missing last receipt gives full-sweep, never no-change', () => {
  const receipt = decideNightlyPlan(coordinatorInput({ lastQualified: null, treeSha: LAST_TREE }));

  assert.equal(receipt.verdict, 'full-sweep');
  assert.deepEqual(receipt.reasons, [REASONS.lastReceiptMissing]);
  assert.equal(receipt.lastQualified, null);
  assert.equal(receipt.lastQualifiedUnavailableReason, REASONS.lastReceiptMissing);
  assert.deepEqual(receipt.selectedSuites, [...NIGHTLY_DEEP_SUITES]);
});

test('an unreadable last receipt gives full-sweep', () => {
  const unreadable = [
    'not-an-object',
    lastReceipt({ headSha: 'nope' }),
    lastReceipt({ treeSha: null }),
    lastReceipt({ completedAtUtc: 'never' }),
  ];
  for (const value of unreadable) {
    const receipt = decideNightlyPlan(coordinatorInput({ lastQualified: value }));
    assert.equal(receipt.verdict, 'full-sweep');
    assert.deepEqual(receipt.reasons, [REASONS.lastReceiptUnreadable]);
    assert.equal(receipt.lastQualified, null);
  }

  const explicit = decideNightlyPlan(coordinatorInput({
    lastQualified: null,
    lastQualifiedReason: REASONS.lastReceiptUnreadable,
  }));
  assert.equal(explicit.verdict, 'full-sweep');
  assert.deepEqual(explicit.reasons, [REASONS.lastReceiptUnreadable]);
});

test('an incomplete last receipt gives full-sweep and never advances the marker', () => {
  const receipt = decideNightlyPlan(coordinatorInput({
    lastQualified: lastReceipt({ complete: false }),
    treeSha: LAST_TREE,
    changedFiles: [],
  }));

  assert.equal(receipt.verdict, 'full-sweep');
  assert.deepEqual(receipt.reasons, [REASONS.lastReceiptIncomplete]);
  assert.equal(receipt.lastQualified.complete, false);
  assert.equal(receipt.duplicateQualification, false);
  assert.equal(receipt.duplicateQualificationReason, REASONS.lastReceiptIncomplete);
  assert.deepEqual(receipt.selectedSuites, [...NIGHTLY_DEEP_SUITES]);

  const normalised = normaliseLastQualified(lastReceipt({ complete: false }));
  assert.equal(normalised.reason, REASONS.lastReceiptIncomplete);
  assert.equal(normalised.receipt.complete, false);
});

test('an unreachable diff gives full-sweep with the reason in the receipt', () => {
  const receipt = decideNightlyPlan(coordinatorInput({ changedFiles: null }));

  assert.equal(receipt.verdict, 'full-sweep');
  assert.deepEqual(receipt.reasons, [REASONS.diffUnavailable]);
  assert.equal(receipt.changedFilesAvailable, false);
  assert.equal(receipt.changedFileCount, null);
  assert.deepEqual(receipt.selectedSuites, [...NIGHTLY_DEEP_SUITES]);
});

test('duplicate qualification is detected from the tree SHA', () => {
  assert.deepEqual(detectDuplicateQualification(LAST_TREE, lastReceipt()), {
    duplicate: true,
    reason: 'tree-sha-already-qualified',
    treeSha: LAST_TREE,
    lastQualifiedTreeSha: LAST_TREE,
  });
  assert.equal(detectDuplicateQualification(TREE, lastReceipt()).duplicate, false);
  assert.equal(detectDuplicateQualification(TREE, lastReceipt()).reason, 'tree-sha-differs');
  assert.equal(detectDuplicateQualification(TREE, null).reason, 'no-last-receipt');
  assert.equal(detectDuplicateQualification('short', lastReceipt()).reason, 'current-tree-sha-invalid');
  assert.equal(detectDuplicateQualification(TREE, lastReceipt({ treeSha: 'x' })).reason, 'last-receipt-tree-sha-invalid');
  // Case-insensitive: the same tree in upper case is still the same tree.
  assert.equal(detectDuplicateQualification(LAST_TREE.toUpperCase(), lastReceipt()).duplicate, true);

  const receipt = decideNightlyPlan(coordinatorInput({ treeSha: LAST_TREE }));
  assert.equal(receipt.duplicateQualification, true);
  assert.equal(receipt.duplicateQualificationReason, 'tree-sha-already-qualified');
});

test('an unmapped path gives full-sweep', () => {
  const receipt = decideNightlyPlan(coordinatorInput({
    changedFiles: ['some-new-top-level-thing.bin', 'backend/src/Taskdeck.Domain/Boards/Board.cs'],
  }));

  assert.equal(receipt.verdict, 'full-sweep');
  assert.deepEqual(receipt.reasons, [REASONS.unmappedPath]);
  assert.deepEqual(receipt.unmappedPaths, ['some-new-top-level-thing.bin']);
  assert.deepEqual(receipt.selectedSuites, [...NIGHTLY_DEEP_SUITES]);
});

test('a control-path change gives full-sweep', () => {
  const receipt = decideNightlyPlan(coordinatorInput({
    changedFiles: ['.github/workflows/ci-nightly.yml', 'docs/ci/SMART_CI.md'],
  }));

  assert.equal(receipt.verdict, 'full-sweep');
  assert.ok(receipt.reasons.includes(REASONS.controlPathChanged));
  assert.deepEqual(receipt.controlPathsChanged, ['.github/workflows/ci-nightly.yml']);
  assert.deepEqual(receipt.selectedSuites, [...NIGHTLY_DEEP_SUITES]);
});

test('an invalid policy, an invalid SHA, an unparseable clock and force-full each fail closed', () => {
  const invalidPolicy = decideNightlyPlan(coordinatorInput({ policy: { policyId: 'broken' } }));
  assert.equal(invalidPolicy.verdict, 'full-sweep');
  assert.ok(invalidPolicy.reasons.includes(REASONS.policyInvalid));

  const noPolicy = decideNightlyPlan(coordinatorInput({ policy: null }));
  assert.equal(noPolicy.verdict, 'full-sweep');
  assert.ok(noPolicy.reasons.includes(REASONS.policyInvalid));

  const badHead = decideNightlyPlan(coordinatorInput({ headSha: 'HEAD' }));
  assert.equal(badHead.verdict, 'full-sweep');
  assert.ok(badHead.reasons.includes(REASONS.headShaInvalid));
  assert.equal(badHead.current.headSha, null);

  const badTree = decideNightlyPlan(coordinatorInput({ treeSha: '' }));
  assert.equal(badTree.verdict, 'full-sweep');
  assert.ok(badTree.reasons.includes(REASONS.treeShaInvalid));

  const badClock = decideNightlyPlan(coordinatorInput({ nowUtc: 'tonight' }));
  assert.equal(badClock.verdict, 'full-sweep');
  assert.ok(badClock.reasons.includes(REASONS.nowUnparseable));
  assert.equal(badClock.generatedAtUtc, '1970-01-01T00:00:00.000Z');

  const forced = decideNightlyPlan(coordinatorInput({ forceFull: true, treeSha: LAST_TREE }));
  assert.equal(forced.verdict, 'full-sweep');
  assert.ok(forced.reasons.includes(REASONS.forceFull));
  assert.equal(forced.forceFull, true);

  const nothing = decideNightlyPlan(undefined);
  assert.equal(nothing.verdict, 'full-sweep');
  assert.deepEqual(nothing.selectedSuites, [...NIGHTLY_DEEP_SUITES]);
});

test('every policy path group has a deep-suite mapping and every mapped suite exists', () => {
  assert.deepEqual(nightlyCoordinatorMappingErrors(policy), []);

  const policyIds = policy.pathGroups.map((group) => group.id).sort();
  assert.deepEqual(Object.keys(GROUP_DEEP_SUITES).sort(), policyIds);

  const known = new Set(NIGHTLY_DEEP_SUITES);
  for (const [groupId, suites] of Object.entries(GROUP_DEEP_SUITES)) {
    for (const suite of suites) assert.ok(known.has(suite), `${groupId} names unknown suite ${suite}`);
  }

  // A new policy group with no mapping is reported, and at decision time it fails closed.
  const grown = { ...policy, pathGroups: [...policy.pathGroups, { id: 'brand-new-surface', riskFloor: 'R2', patterns: ['brand-new/**'], lanes: [] }] };
  const errors = nightlyCoordinatorMappingErrors(grown);
  assert.deepEqual(errors, ['policy path group brand-new-surface has no deep-suite mapping']);

  const receipt = decideNightlyPlan(coordinatorInput({ policy: grown, changedFiles: ['brand-new/thing.txt'] }));
  assert.equal(receipt.verdict, 'full-sweep');
  assert.ok(receipt.reasons.includes(REASONS.groupNotMapped));
  assert.deepEqual(receipt.selectedSuites, [...NIGHTLY_DEEP_SUITES]);

  // A dropped policy group is reported too, so the table cannot rot in the other direction.
  const shrunk = { ...policy, pathGroups: policy.pathGroups.filter((group) => group.id !== 'load-and-evals') };
  assert.deepEqual(nightlyCoordinatorMappingErrors(shrunk), [
    'deep-suite mapping names unknown policy path group load-and-evals',
  ]);
});

/**
 * The `needs:` edges of a nightly workflow, as `{ jobId: [dependency, ...] }`, read straight from
 * the YAML. Deliberately a narrow line scanner rather than a YAML dependency: it understands the
 * two shapes these files use (`needs: job` and a `needs:` block of `- job` items) at their exact
 * indentation, so an unexpected shape shows up as a missing edge and fails the drift assertion.
 */
function workflowNeedsEdges(workflowPath) {
  const edges = new Map();
  let job = null;
  let inNeedsBlock = false;
  for (const rawLine of readFileSync(workflowPath, 'utf8').split(/\r?\n/)) {
    const jobMatch = /^ {2}([A-Za-z0-9_-]+):\s*$/.exec(rawLine);
    if (jobMatch) {
      job = jobMatch[1];
      edges.set(job, []);
      inNeedsBlock = false;
      continue;
    }
    if (job === null) continue;
    const inlineNeeds = /^ {4}needs:\s*(\S.*)$/.exec(rawLine);
    if (inlineNeeds) {
      edges.get(job).push(inlineNeeds[1].replace(/^\[|\]$/g, '').split(',').map((part) => part.trim())
        .filter(Boolean));
      inNeedsBlock = false;
      continue;
    }
    if (/^ {4}needs:\s*$/.test(rawLine)) {
      inNeedsBlock = true;
      continue;
    }
    if (inNeedsBlock) {
      const item = /^ {6}- (\S+)\s*$/.exec(rawLine);
      if (item) {
        edges.get(job).push([item[1]]);
        continue;
      }
      inNeedsBlock = false;
    }
  }
  return new Map([...edges].map(([id, groups]) => [id, groups.flat()]));
}

test('the prerequisite table matches the nightly workflows and the graph is well formed', () => {
  assert.deepEqual(nightlyCoordinatorSuiteGraphErrors(), []);

  const workflows = [
    fileURLToPath(new URL('../../../.github/workflows/ci-nightly.yml', import.meta.url)),
    fileURLToPath(new URL('../../../.github/workflows/nightly-quality.yml', import.meta.url)),
  ];
  const actual = new Map();
  for (const workflow of workflows) {
    for (const [job, needs] of workflowNeedsEdges(workflow)) {
      if (!NIGHTLY_DEEP_SUITES.includes(job)) continue;
      actual.set(job, needs.slice().sort());
    }
  }
  // Every deep suite is a real job in one of the two workflows, and nothing is missed.
  assert.deepEqual([...actual.keys()].sort(), [...NIGHTLY_DEEP_SUITES].sort());

  const declared = new Map(NIGHTLY_DEEP_SUITES.map((suite) => [
    suite,
    (Object.hasOwn(SUITE_PREREQUISITES, suite) ? [...SUITE_PREREQUISITES[suite]] : []).sort(),
  ]));
  for (const suite of NIGHTLY_DEEP_SUITES) {
    assert.deepEqual(declared.get(suite), actual.get(suite), `needs edges for ${suite}`);
  }
  // The scanner really did find the five documented edges, so an empty parse cannot pass above.
  assert.equal([...actual.values()].filter((needs) => needs.length > 0).length, 5);
});

test('every receipt selection is closed under the workflow needs edges', () => {
  const scenarios = [
    ['frontend/taskdeck-web/src/views/BoardView.vue'],
    ['frontend/taskdeck-web/tests/e2e/board.spec.ts'],
    ['deploy/docker/frontend.Dockerfile'],
    ['tests/load/board-read.js'],
    ['backend/src/Taskdeck.Domain/Boards/Board.cs'],
    ['.mcp.json'],
    ['backend/src/Taskdeck.Api/Endpoints/BoardEndpoints.cs'],
    ['docs/STATUS.md'],
    // A fail-closed full sweep is closed too: every suite is selected.
    ['some-new-top-level-thing.bin'],
  ];
  for (const changedFiles of scenarios) {
    const receipt = decideNightlyPlan(coordinatorInput({ changedFiles }));
    const selected = new Set(receipt.selectedSuites);
    const skipped = new Set(receipt.skippedSuites.map((entry) => entry.suite));
    for (const suite of selected) {
      for (const need of (Object.hasOwn(SUITE_PREREQUISITES, suite) ? SUITE_PREREQUISITES[suite] : [])) {
        assert.ok(selected.has(need), `${changedFiles[0]}: ${suite} selected without ${need}`);
        assert.ok(!skipped.has(need), `${changedFiles[0]}: ${need} both needed and skipped`);
      }
    }
  }

  // The three groups the closure exists for: each names a dependent suite, none names the
  // dependency, and the receipt supplies it.
  for (const groupId of ['frontend-src', 'frontend-e2e', 'containers-deploy', 'load-and-evals']) {
    assert.ok(!GROUP_DEEP_SUITES[groupId].includes('backend-solution'), groupId);
    assert.ok(closeUnderPrerequisites(GROUP_DEEP_SUITES[groupId]).includes('backend-solution'), groupId);
  }

  // Closure adds only prerequisites; it never invents an unrelated suite or drops one.
  assert.deepEqual(closeUnderPrerequisites([]), []);
  assert.deepEqual(closeUnderPrerequisites(['backend-coverage']), ['backend-coverage']);
  assert.deepEqual(closeUnderPrerequisites([...NIGHTLY_DEEP_SUITES]).sort(), [...NIGHTLY_DEEP_SUITES].sort());
});

test('two calls with the same input produce byte-identical JSON and markdown', () => {
  const input = () => coordinatorInput({
    changedFiles: [
      'frontend/taskdeck-web/src/views/BoardView.vue',
      'backend/src/Taskdeck.Api/Endpoints/BoardEndpoints.cs',
      'backend/src/Taskdeck.Api/Endpoints/BoardEndpoints.cs',
    ],
  });
  const first = decideNightlyPlan(input());
  const second = decideNightlyPlan(input());

  assert.equal(JSON.stringify(first, null, 2), JSON.stringify(second, null, 2));
  assert.equal(renderNightlySummary(first), renderNightlySummary(second));
  assert.equal(first.verdict, 'affected');
  assert.equal(first.changedFileCount, 2, 'duplicate paths are counted once');

  // Input order must not move a byte either.
  const reordered = decideNightlyPlan(coordinatorInput({
    changedFiles: [
      'backend/src/Taskdeck.Api/Endpoints/BoardEndpoints.cs',
      'frontend/taskdeck-web/src/views/BoardView.vue',
    ],
  }));
  const forward = decideNightlyPlan(coordinatorInput({
    changedFiles: [
      'frontend/taskdeck-web/src/views/BoardView.vue',
      'backend/src/Taskdeck.Api/Endpoints/BoardEndpoints.cs',
    ],
  }));
  assert.equal(JSON.stringify(reordered), JSON.stringify(forward));
});

test('the markdown summary renders every deep suite and the verdict', () => {
  const receipt = decideNightlyPlan(coordinatorInput({
    changedFiles: ['backend/src/Taskdeck.Domain/Boards/Board.cs'],
  }));
  const markdown = renderNightlySummary(receipt);

  assert.match(markdown, /^# Smart CI nightly coordinator$/m);
  assert.match(markdown, /- Verdict: \*\*affected\*\*/);
  for (const suite of NIGHTLY_DEEP_SUITES) assert.ok(markdown.includes(`\`${suite}\``), suite);
  assert.match(markdown, /\| `backend-solution` \| run \| affected \|/);
  assert.match(markdown, /\| `e2e-cross-browser` \| skip \| not-selected-by-changed-groups \|/);
});

test('parseChangedFileList reads plain lines and TSV rename rows', () => {
  assert.deepEqual(parseChangedFileList('docs/a.md\n\nbackend/b.cs\n'), ['docs/a.md', 'backend/b.cs']);
  assert.deepEqual(parseChangedFileList('modified\tdocs/a.md\t\nrenamed\tdocs/new.md\tdocs/old.md\n'), [
    'docs/a.md',
    'docs/new.md',
    'docs/old.md',
  ]);
});

test('parseArgs reads every documented flag and rejects an unknown one', () => {
  const args = parseArgs([
    '--policy', 'ci/policy.v1.json',
    '--last-receipt', 'last.json',
    '--head-sha', HEAD,
    '--tree-sha', TREE,
    '--changed-files', 'changed.txt',
    '--now', THURSDAY,
    '--weekly-slot', '6',
    '--force-full',
    '--out', 'out.json',
    '--out-md', 'out.md',
    '--summary', 'summary.md',
  ]);

  assert.deepEqual(args, {
    policy: 'ci/policy.v1.json',
    lastReceipt: 'last.json',
    headSha: HEAD,
    treeSha: TREE,
    changedFiles: 'changed.txt',
    now: THURSDAY,
    weeklySlotUtcDay: 6,
    forceFull: true,
    out: 'out.json',
    outMarkdown: 'out.md',
    summary: 'summary.md',
    help: false,
  });
  assert.equal(parseArgs([]).policy, 'ci/policy.v1.json');
  assert.equal(parseArgs(['--help']).help, true);
  assert.throws(() => parseArgs(['--nope']), /Unknown argument: --nope/);
});

test('the CLI writes the receipt JSON and the markdown summary to files', () => {
  const root = mkdtempSync(join(tmpdir(), 'taskdeck-nightly-coordinator-'));
  const receiptPath = join(root, 'last.json');
  const changedPath = join(root, 'changed.tsv');
  const outPath = join(root, 'nested', 'nightly-plan.json');
  const outMarkdownPath = join(root, 'nested', 'nightly-plan.md');
  const summaryPath = join(root, 'step-summary.md');

  try {
    writeFileSync(receiptPath, `${JSON.stringify(lastReceipt())}\n`);
    writeFileSync(changedPath, 'modified\tbackend/src/Taskdeck.Domain/Boards/Board.cs\t\n');
    writeFileSync(summaryPath, '');

    const stdout = execFileSync(process.execPath, [
      fileURLToPath(new URL('./nightly-coordinator.mjs', import.meta.url)),
      '--policy', policyPath,
      '--last-receipt', receiptPath,
      '--head-sha', HEAD,
      '--tree-sha', TREE,
      '--changed-files', changedPath,
      '--now', THURSDAY,
      '--weekly-slot', '6',
      '--out', outPath,
      '--out-md', outMarkdownPath,
      '--summary', summaryPath,
    ], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });

    const receipt = JSON.parse(readFileSync(outPath, 'utf8'));
    assert.equal(receipt.kind, NIGHTLY_COORDINATOR_KIND);
    assert.equal(receipt.verdict, 'affected');
    assert.deepEqual(receipt.matchedGroups, ['backend-domain']);
    assert.equal(receipt.current.headSha, HEAD);
    assert.equal(receipt.lastQualified.treeSha, LAST_TREE);
    assert.equal(receipt.policyDigest, digest);
    assert.equal(receipt.generatedAtUtc, THURSDAY);
    assert.ok(!receipt.selectedSuites.includes('e2e-cross-browser'));

    const markdown = readFileSync(outMarkdownPath, 'utf8');
    assert.equal(markdown, renderNightlySummary(receipt));
    assert.equal(readFileSync(summaryPath, 'utf8'), markdown);
    assert.equal(stdout, markdown);

    // A missing changed-file list is an unavailable diff, not an empty one.
    const missingDiffOut = join(root, 'missing-diff.json');
    execFileSync(process.execPath, [
      fileURLToPath(new URL('./nightly-coordinator.mjs', import.meta.url)),
      '--policy', policyPath,
      '--last-receipt', receiptPath,
      '--head-sha', HEAD,
      '--tree-sha', TREE,
      '--changed-files', join(root, 'does-not-exist.tsv'),
      '--now', THURSDAY,
      '--out', missingDiffOut,
    ], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });
    const missingDiff = JSON.parse(readFileSync(missingDiffOut, 'utf8'));
    assert.equal(missingDiff.verdict, 'full-sweep');
    assert.deepEqual(missingDiff.reasons, [REASONS.diffUnavailable]);

    // An unreadable last receipt escalates rather than throwing.
    const badReceiptPath = join(root, 'corrupt.json');
    writeFileSync(badReceiptPath, '{ not json');
    const badReceiptOut = join(root, 'corrupt-plan.json');
    execFileSync(process.execPath, [
      fileURLToPath(new URL('./nightly-coordinator.mjs', import.meta.url)),
      '--policy', policyPath,
      '--last-receipt', badReceiptPath,
      '--head-sha', HEAD,
      '--tree-sha', TREE,
      '--changed-files', changedPath,
      '--now', THURSDAY,
      '--out', badReceiptOut,
    ], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });
    const badReceipt = JSON.parse(readFileSync(badReceiptOut, 'utf8'));
    assert.equal(badReceipt.verdict, 'full-sweep');
    assert.deepEqual(badReceipt.reasons, [REASONS.lastReceiptUnreadable]);
  } finally {
    rmSync(root, { recursive: true, force: true, maxRetries: 3, retryDelay: 100 });
  }
});
