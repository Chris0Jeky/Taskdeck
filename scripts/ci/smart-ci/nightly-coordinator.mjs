#!/usr/bin/env node
// Smart CI nightly coordinator — decision module (ADR-0066 §Decision 10, CI-10 #2334, tracker CI-00 #2324).
//
// One owner answers "what changed on `main` since the last complete deep qualification, and which
// deep suites would produce new evidence tonight". This slice (CI10-1) is the pure decision only:
// it changes no workflow behaviour, and nothing here schedules, skips or gates a job. Wiring the
// verdict into `ci-nightly.yml` / `nightly-quality.yml` is CI10-2, the weekly sweep is CI10-3, and
// the release collapse is CI10-4.
//
// FAIL-CLOSED, matching `docs/ci/SMART_CI.md` invariant 2 (unknown change = full escalation):
// a missing, unreadable or incomplete last receipt, an unreachable diff, an unmapped path, a
// changed control path, a policy path group with no suite mapping, an invalid policy, an
// unparseable clock, an invalid weekly slot or any internal error selects the complete sweep.
// A quiet night is an explicit `no-change` RECEIPT, never a skipped workflow (invariant 1 forbids
// a skip reporting as success to branch protection).
//
// Verdicts
//   no-change    the current tree SHA equals the last qualified tree SHA, or the diff maps only to
//                path groups with no deep suite. Selected: none. This is the honest green receipt.
//   affected     the union of the deep suites of the matched path groups.
//   weekly-full  the configured weekly UTC slot forces the complete sweep regardless of the diff.
//   full-sweep   fail-closed escalation, or an explicit `--force-full` dispatch.
// Precedence: full-sweep > weekly-full > no-change (identical tree) > affected.
//
// Receipt shape (JSON written by the CLI). No `ci/schemas` entry in this slice: CI10-2 adds one
// alongside the policy move described under GROUP_DEEP_SUITES, so the shape is specified here.
//   {
//     schemaVersion: 1,
//     kind: "nightly-plan",
//     generatedAtUtc: string,          // echoed from the `nowUtc` input, never Date.now()
//     policyId: string|null,
//     policyDigest: string|null,
//     verdict: "no-change"|"affected"|"weekly-full"|"full-sweep",
//     reasons: string[],               // sorted, stable ids; never empty
//     current: { headSha: string|null, treeSha: string|null },
//     lastQualified: { headSha: string, treeSha: string, completedAtUtc: string, complete: boolean }|null,
//     lastQualifiedUnavailableReason: string|null,
//     duplicateQualification: boolean,
//     duplicateQualificationReason: string,
//     weeklySlot: { utcDay: number|null, nowUtcDay: number|null, matched: boolean },
//     forceFull: boolean,
//     changedFilesAvailable: boolean,
//     changedFileCount: number|null,
//     matchedGroups: string[],
//     unmappedPaths: string[],
//     controlPathsChanged: string[],
//     selectedSuites: string[],        // canonical NIGHTLY_DEEP_SUITES order
//     skippedSuites: [{ suite: string, reason: string }]
//   }
//
// CLI usage (every input explicit; no network and no git calls anywhere in this module):
//   node scripts/ci/smart-ci/nightly-coordinator.mjs --policy ci/policy.v1.json \
//     --head-sha <sha> --tree-sha <sha> [--last-receipt <file>] [--changed-files <file>] \
//     [--now <iso8601>] [--weekly-slot <0-6>] [--force-full] \
//     [--out artifacts/nightly-plan.json] [--out-md artifacts/nightly-plan.md] \
//     [--summary "$GITHUB_STEP_SUMMARY"]
// Omitting `--changed-files`, or pointing it at a missing file, means the diff is UNAVAILABLE and
// escalates; an existing empty file means an empty diff. Omitting `--last-receipt` means no last
// receipt and escalates. The CLI always exits 0: the verdict is the output, not the exit status.

import { existsSync, mkdirSync, readFileSync, writeFileSync, appendFileSync } from 'node:fs';
import { dirname } from 'node:path';
import { matchGroups, policyDigest, validatePolicy } from './lib/plan.mjs';

export const NIGHTLY_COORDINATOR_SCHEMA_VERSION = 1;
export const NIGHTLY_COORDINATOR_KIND = 'nightly-plan';

/**
 * The deep suites that exist today, as the job ids of `.github/workflows/ci-nightly.yml`
 * (the first nine) and `.github/workflows/nightly-quality.yml` (the last three). Declaration
 * order is the canonical render order, so a receipt is byte-stable for identical input.
 */
export const NIGHTLY_DEEP_SUITES = Object.freeze([
  'openapi-guardrail',
  'developer-portal',
  'backend-solution',
  'e2e-smoke',
  'load-concurrency-harness',
  'performance-regression-gate',
  'e2e-cross-browser',
  'container-images',
  'sast-scanning',
  'backend-coverage',
  'frontend-coverage',
  'dependency-security-signals',
]);

const BACKEND_SUITES = Object.freeze([
  'backend-solution',
  'backend-coverage',
  'load-concurrency-harness',
  'performance-regression-gate',
  'container-images',
]);
const FRONTEND_SUITES = Object.freeze([
  'frontend-coverage',
  'e2e-smoke',
  'e2e-cross-browser',
]);
const DEPENDENCY_SUITES = Object.freeze([
  'dependency-security-signals',
  'sast-scanning',
  'container-images',
]);
const API_CONTRACT_SUITES = Object.freeze(['openapi-guardrail', 'developer-portal']);

/**
 * Path group id (from `ci/policy.v1.json` `pathGroups`) to the deep suites a change in that group
 * can produce new evidence for. Deliberately conservative: a group selects a superset rather than
 * the minimum, and only a genuinely evidence-free group (docs, repo metadata, agent tooling)
 * selects nothing. Dependency manifests and lockfiles are `controlPaths` in the policy, so they
 * escalate to the full sweep before this table is consulted; `backend-project-files` carries the
 * dependency suites for the manifests that are not control paths.
 *
 * CI10-2 moves this table into `ci/policy.v1.json` next to `pathGroups` so the policy digest covers
 * it; it lives here in slice 1 so the decision function can be proven before the policy schema
 * changes. Until then `nightlyCoordinatorMappingErrors()` and its test enumerate every policy group
 * id against this table, so a new group cannot silently select nothing: a policy group with no
 * entry here is a fail-closed full sweep, not an empty selection.
 */
export const GROUP_DEEP_SUITES = Object.freeze({
  'docs': Object.freeze([]),
  'repo-metadata': Object.freeze([]),
  'agent-tooling': Object.freeze([]),
  'mcp-config': Object.freeze(['backend-solution', 'e2e-smoke']),
  'worktree-helpers': Object.freeze([]),
  'governance-scripts': Object.freeze([]),
  'scripts-other': Object.freeze([]),
  'backend-domain': BACKEND_SUITES,
  'backend-application': BACKEND_SUITES,
  'backend-infrastructure': BACKEND_SUITES,
  'persistence-migrations': BACKEND_SUITES,
  'backend-api': Object.freeze([...BACKEND_SUITES, ...API_CONTRACT_SUITES, 'e2e-smoke']),
  'mcp-process': Object.freeze([...BACKEND_SUITES, 'e2e-smoke']),
  'auth-security': Object.freeze([...BACKEND_SUITES, 'e2e-smoke', 'sast-scanning']),
  'capture-proposal-executor': Object.freeze([...BACKEND_SUITES, 'e2e-smoke']),
  'backend-cli': BACKEND_SUITES,
  'backend-tests': BACKEND_SUITES,
  'backend-project-files': Object.freeze([...BACKEND_SUITES, ...DEPENDENCY_SUITES]),
  'frontend-src': FRONTEND_SUITES,
  'frontend-e2e': FRONTEND_SUITES,
  'launchers-windows': Object.freeze([...BACKEND_SUITES, ...FRONTEND_SUITES]),
  'containers-deploy': DEPENDENCY_SUITES,
  'load-and-evals': Object.freeze(['load-concurrency-harness', 'performance-regression-gate']),
});

/**
 * Stable reason ids. Every verdict carries at least one. `unmapped-path` and `control-path-change`
 * deliberately repeat the planner's escalation vocabulary in `lib/plan.mjs`.
 */
export const REASONS = Object.freeze({
  affectedGroups: 'affected-groups',
  controlPathChanged: 'control-path-change',
  coordinatorError: 'coordinator-error',
  diffUnavailable: 'diff-unavailable',
  forceFull: 'force-full-requested',
  groupNotMapped: 'group-not-in-suite-map',
  headShaInvalid: 'current-head-sha-invalid',
  identicalTree: 'identical-tree-sha',
  lastReceiptIncomplete: 'last-receipt-incomplete',
  lastReceiptMissing: 'last-receipt-missing',
  lastReceiptUnreadable: 'last-receipt-unreadable',
  noDeepSuiteGroups: 'no-deep-suite-groups',
  nowUnparseable: 'now-unparseable',
  policyInvalid: 'policy-invalid',
  treeShaInvalid: 'current-tree-sha-invalid',
  unmappedPath: 'unmapped-path',
  weeklySlot: 'weekly-slot',
  weeklySlotInvalid: 'weekly-slot-invalid',
});

/** Why a deep suite is not selected. */
export const SKIP_REASONS = Object.freeze({
  noChange: 'no-change',
  notAffected: 'not-selected-by-changed-groups',
});

function isObject(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function isSha(value) {
  return /^[0-9a-f]{40}$/i.test(String(value ?? ''));
}

function normaliseSha(value) {
  return isSha(value) ? String(value).toLowerCase() : null;
}

function isIsoTimestamp(value) {
  return typeof value === 'string' && value.length > 0 && !Number.isNaN(Date.parse(value));
}

/** Codepoint sort, matching `lib/plan.mjs`; locale-sensitive ordering would not be byte-stable. */
function uniqueSorted(values) {
  return [...new Set(values.map(String))].sort();
}

/** Order a suite set by the canonical NIGHTLY_DEEP_SUITES declaration order. */
function orderSuites(suites) {
  const wanted = new Set(suites.map(String));
  return NIGHTLY_DEEP_SUITES.filter((suite) => wanted.has(suite));
}

/**
 * Consistency errors in GROUP_DEEP_SUITES itself, given a policy document. Empty means the table
 * covers every policy path group, names no unknown deep suite, and names no group the policy has
 * dropped. Sorted so the message set is stable.
 * @param {object} policy parsed `ci/policy.v1.json`
 * @returns {string[]}
 */
export function nightlyCoordinatorMappingErrors(policy) {
  const errors = [];
  const known = new Set(NIGHTLY_DEEP_SUITES);
  for (const [groupId, suites] of Object.entries(GROUP_DEEP_SUITES)) {
    for (const suite of suites) {
      if (!known.has(suite)) errors.push(`group ${groupId} names unknown deep suite ${suite}`);
    }
  }
  const groups = isObject(policy) && Array.isArray(policy.pathGroups) ? policy.pathGroups : [];
  for (const group of groups) {
    const id = isObject(group) && typeof group.id === 'string' ? group.id : null;
    if (id === null) {
      errors.push('policy pathGroups contains an entry without a string id');
      continue;
    }
    if (!Object.hasOwn(GROUP_DEEP_SUITES, id)) errors.push(`policy path group ${id} has no deep-suite mapping`);
  }
  const policyIds = new Set(groups.map((group) => (isObject(group) && typeof group.id === 'string' ? group.id : '')));
  for (const groupId of Object.keys(GROUP_DEEP_SUITES)) {
    if (!policyIds.has(groupId)) errors.push(`deep-suite mapping names unknown policy path group ${groupId}`);
  }
  return errors.sort();
}

/**
 * Duplicate qualification: the current tree SHA was already deep-qualified by the last receipt.
 * CI-12 (#2336) consumes this as the nightly half of its duplicate-qualification flag.
 *
 * A partially failed nightly never qualified anything (issue edge case: some deep suites green,
 * some red), so an incomplete receipt reports `last-receipt-incomplete` rather than a duplicate.
 *
 * @param {string|null} treeSha current `main` tree SHA
 * @param {object|null} lastQualified last deep-qualified receipt, or null
 * @returns {{ duplicate: boolean, reason: string, treeSha: string|null, lastQualifiedTreeSha: string|null }}
 */
export function detectDuplicateQualification(treeSha, lastQualified) {
  const current = normaliseSha(treeSha);
  if (current === null) {
    return { duplicate: false, reason: 'current-tree-sha-invalid', treeSha: null, lastQualifiedTreeSha: null };
  }
  if (!isObject(lastQualified)) {
    return { duplicate: false, reason: 'no-last-receipt', treeSha: current, lastQualifiedTreeSha: null };
  }
  const previous = normaliseSha(lastQualified.treeSha);
  if (previous === null) {
    return { duplicate: false, reason: 'last-receipt-tree-sha-invalid', treeSha: current, lastQualifiedTreeSha: null };
  }
  if (previous !== current) {
    return { duplicate: false, reason: 'tree-sha-differs', treeSha: current, lastQualifiedTreeSha: previous };
  }
  if (lastQualified.complete !== true) {
    return { duplicate: false, reason: REASONS.lastReceiptIncomplete, treeSha: current, lastQualifiedTreeSha: previous };
  }
  return { duplicate: true, reason: 'tree-sha-already-qualified', treeSha: current, lastQualifiedTreeSha: previous };
}

/**
 * Validate the last deep-qualified receipt.
 * @param {object|null} lastQualified
 * @param {string|null} [explicitReason] reason supplied by the caller when the receipt is null
 *   because it could not be read rather than because it does not exist
 * @returns {{ receipt: object|null, reason: string|null }} `reason === null` means usable
 */
export function normaliseLastQualified(lastQualified, explicitReason = null) {
  if (lastQualified === null || lastQualified === undefined) {
    return { receipt: null, reason: explicitReason ?? REASONS.lastReceiptMissing };
  }
  if (!isObject(lastQualified)) return { receipt: null, reason: REASONS.lastReceiptUnreadable };
  const headSha = normaliseSha(lastQualified.headSha);
  const treeSha = normaliseSha(lastQualified.treeSha);
  if (headSha === null || treeSha === null || !isIsoTimestamp(lastQualified.completedAtUtc)) {
    return { receipt: null, reason: REASONS.lastReceiptUnreadable };
  }
  const completedAtUtc = new Date(lastQualified.completedAtUtc).toISOString();
  // The "last deep-qualified SHA" only advances on a complete success.
  if (lastQualified.complete !== true) {
    return {
      receipt: { headSha, treeSha, completedAtUtc, complete: false },
      reason: REASONS.lastReceiptIncomplete,
    };
  }
  return { receipt: { headSha, treeSha, completedAtUtc, complete: true }, reason: null };
}

function buildReceipt(fields) {
  const selected = orderSuites(fields.selectedSuites ?? []);
  const selectedSet = new Set(selected);
  const skipReason = fields.skipReason ?? SKIP_REASONS.notAffected;
  return {
    schemaVersion: NIGHTLY_COORDINATOR_SCHEMA_VERSION,
    kind: NIGHTLY_COORDINATOR_KIND,
    generatedAtUtc: fields.generatedAtUtc,
    policyId: fields.policyId,
    policyDigest: fields.policyDigest,
    verdict: fields.verdict,
    reasons: uniqueSorted(fields.reasons),
    current: { headSha: fields.headSha, treeSha: fields.treeSha },
    lastQualified: fields.lastQualified,
    lastQualifiedUnavailableReason: fields.lastQualifiedUnavailableReason,
    duplicateQualification: fields.duplicate.duplicate,
    duplicateQualificationReason: fields.duplicate.reason,
    weeklySlot: fields.weeklySlot,
    forceFull: fields.forceFull,
    changedFilesAvailable: fields.changedFilesAvailable,
    changedFileCount: fields.changedFileCount,
    matchedGroups: fields.matchedGroups ?? [],
    unmappedPaths: fields.unmappedPaths ?? [],
    controlPathsChanged: fields.controlPathsChanged ?? [],
    selectedSuites: selected,
    skippedSuites: NIGHTLY_DEEP_SUITES
      .filter((suite) => !selectedSet.has(suite))
      .map((suite) => ({ suite, reason: skipReason })),
  };
}

/**
 * Decide tonight's nightly plan. Pure: identical input always yields an identical receipt, and
 * nothing here reads the network, the clock or git.
 *
 * @param {object} input
 * @param {object} input.policy parsed `ci/policy.v1.json`
 * @param {string|null} [input.policyDigest] policyDigest() of the policy file bytes
 * @param {object|null} input.lastQualified last deep-qualified receipt
 *   `{ headSha, treeSha, completedAtUtc, complete }`, or null when missing or unreadable
 * @param {string|null} [input.lastQualifiedReason] escalation reason when `lastQualified` is null
 *   because it could not be read rather than because it does not exist
 * @param {string} input.headSha current `main` head SHA
 * @param {string} input.treeSha current `main` tree SHA
 * @param {string[]|null} input.changedFiles paths changed between the last qualified SHA and the
 *   current head, or null when that diff is unavailable (force-moved history, expired receipt)
 * @param {string} input.nowUtc ISO-8601 timestamp; also the receipt's generatedAtUtc
 * @param {number|null} [input.weeklySlotUtcDay] UTC weekday 0..6 that forces the full sweep
 * @param {boolean} [input.forceFull] workflow_dispatch escalation
 * @returns {object} the receipt described in this file's header
 */
export function decideNightlyPlan(input) {
  const source = isObject(input) ? input : {};
  const policyId = isObject(source.policy) && typeof source.policy.policyId === 'string'
    ? source.policy.policyId
    : null;
  const policyDigestValue = typeof source.policyDigest === 'string' ? source.policyDigest : null;
  const headSha = normaliseSha(source.headSha);
  const treeSha = normaliseSha(source.treeSha);
  const forceFull = source.forceFull === true;
  const nowUtc = isIsoTimestamp(source.nowUtc) ? new Date(source.nowUtc).toISOString() : null;
  const weeklySlotRequested = source.weeklySlotUtcDay ?? null;
  const weeklySlotValid = weeklySlotRequested === null
    || (Number.isInteger(weeklySlotRequested) && weeklySlotRequested >= 0 && weeklySlotRequested <= 6);
  const weeklySlotUtcDay = weeklySlotValid ? weeklySlotRequested : null;
  const nowUtcDay = nowUtc === null ? null : new Date(nowUtc).getUTCDay();
  const weeklySlot = {
    utcDay: weeklySlotUtcDay,
    nowUtcDay,
    matched: weeklySlotUtcDay !== null && nowUtcDay !== null && weeklySlotUtcDay === nowUtcDay,
  };
  const lastQualifiedRaw = source.lastQualified ?? null;
  const duplicate = detectDuplicateQualification(treeSha, lastQualifiedRaw);
  // generatedAtUtc must never fall back to Date.now(): the receipt has to be reproducible.
  const generatedAtUtc = nowUtc ?? '1970-01-01T00:00:00.000Z';

  const base = {
    generatedAtUtc,
    policyId,
    policyDigest: policyDigestValue,
    headSha,
    treeSha,
    duplicate,
    weeklySlot,
    forceFull,
  };

  try {
    const { receipt: lastQualified, reason: lastQualifiedReason } = normaliseLastQualified(
      lastQualifiedRaw,
      typeof source.lastQualifiedReason === 'string' ? source.lastQualifiedReason : null,
    );
    const changedFiles = Array.isArray(source.changedFiles)
      ? source.changedFiles.map(String).filter((path) => path.length > 0)
      : null;
    const changedFilesAvailable = changedFiles !== null;
    const common = {
      ...base,
      lastQualified,
      lastQualifiedUnavailableReason: lastQualifiedReason,
      changedFilesAvailable,
      changedFileCount: changedFilesAvailable ? new Set(changedFiles).size : null,
    };

    // 1. Fail-closed escalations, and the explicit dispatch override.
    const escalations = [];
    if (forceFull) escalations.push(REASONS.forceFull);
    if (lastQualifiedReason !== null) escalations.push(lastQualifiedReason);
    if (headSha === null) escalations.push(REASONS.headShaInvalid);
    if (treeSha === null) escalations.push(REASONS.treeShaInvalid);
    if (nowUtc === null) escalations.push(REASONS.nowUnparseable);
    if (!weeklySlotValid) escalations.push(REASONS.weeklySlotInvalid);
    const policyErrors = isObject(source.policy) ? validatePolicy(source.policy) : ['policy is not an object'];
    if (policyErrors.length > 0) escalations.push(REASONS.policyInvalid);
    if (escalations.length > 0) {
      return buildReceipt({
        ...common, verdict: 'full-sweep', reasons: escalations, selectedSuites: NIGHTLY_DEEP_SUITES,
      });
    }

    // 2. The weekly entropy sweep runs regardless of the diff.
    if (weeklySlot.matched) {
      return buildReceipt({
        ...common, verdict: 'weekly-full', reasons: [REASONS.weeklySlot], selectedSuites: NIGHTLY_DEEP_SUITES,
      });
    }

    // 3. Identical content is identical evidence, whether or not the diff is reachable.
    if (duplicate.duplicate) {
      return buildReceipt({
        ...common,
        verdict: 'no-change',
        reasons: [REASONS.identicalTree],
        selectedSuites: [],
        skipReason: SKIP_REASONS.noChange,
      });
    }

    // 4. Without a diff there is no basis for selection.
    if (!changedFilesAvailable) {
      return buildReceipt({
        ...common, verdict: 'full-sweep', reasons: [REASONS.diffUnavailable], selectedSuites: NIGHTLY_DEEP_SUITES,
      });
    }

    // 5. Map paths to policy groups with the planner's matcher, never a second glob implementation.
    const { groups, unmapped, controlPathsChanged } = matchGroups(uniqueSorted(changedFiles), source.policy);
    const matchedGroups = [...groups.keys()].sort();
    const withPaths = {
      ...common,
      matchedGroups,
      unmappedPaths: uniqueSorted(unmapped),
      controlPathsChanged: uniqueSorted(controlPathsChanged),
    };
    const pathEscalations = [];
    if (unmapped.length > 0) pathEscalations.push(REASONS.unmappedPath);
    if (controlPathsChanged.length > 0) pathEscalations.push(REASONS.controlPathChanged);
    if (matchedGroups.some((groupId) => !Object.hasOwn(GROUP_DEEP_SUITES, groupId))) {
      pathEscalations.push(REASONS.groupNotMapped);
    }
    if (pathEscalations.length > 0) {
      return buildReceipt({
        ...withPaths, verdict: 'full-sweep', reasons: pathEscalations, selectedSuites: NIGHTLY_DEEP_SUITES,
      });
    }

    // 6. The union of the matched groups' deep suites.
    const selected = new Set();
    for (const groupId of matchedGroups) for (const suite of GROUP_DEEP_SUITES[groupId]) selected.add(suite);
    if (selected.size === 0) {
      return buildReceipt({
        ...withPaths,
        verdict: 'no-change',
        reasons: [REASONS.noDeepSuiteGroups],
        selectedSuites: [],
        skipReason: SKIP_REASONS.noChange,
      });
    }
    return buildReceipt({
      ...withPaths, verdict: 'affected', reasons: [REASONS.affectedGroups], selectedSuites: [...selected],
    });
  } catch {
    // Invariant 2: a coordinator defect escalates rather than producing a selective plan.
    return buildReceipt({
      ...base,
      lastQualified: null,
      lastQualifiedUnavailableReason: REASONS.lastReceiptUnreadable,
      changedFilesAvailable: false,
      changedFileCount: null,
      verdict: 'full-sweep',
      reasons: [REASONS.coordinatorError],
      selectedSuites: NIGHTLY_DEEP_SUITES,
    });
  }
}

function markdownCell(value) {
  return String(value ?? '').replace(/\|/g, '\\|').replace(/[\r\n]+/g, ' ');
}

function shortSha(value) {
  return value === null || value === undefined ? 'unknown' : String(value).slice(0, 12);
}

/**
 * Markdown for `$GITHUB_STEP_SUMMARY`. Derived only from the receipt, so it is deterministic too.
 * @param {object} receipt a receipt from decideNightlyPlan()
 * @returns {string}
 */
export function renderNightlySummary(receipt) {
  const selectedSet = new Set(receipt.selectedSuites);
  const lastQualified = receipt.lastQualified
    ? `head \`${shortSha(receipt.lastQualified.headSha)}\`, tree \`${shortSha(receipt.lastQualified.treeSha)}\``
      + ` at \`${markdownCell(receipt.lastQualified.completedAtUtc)}\``
      + ` (complete: ${receipt.lastQualified.complete ? 'yes' : 'no'})`
    : `none (\`${markdownCell(receipt.lastQualifiedUnavailableReason ?? 'unknown')}\`)`;
  const weekly = receipt.weeklySlot.utcDay === null ? 'not configured' : `UTC day ${receipt.weeklySlot.utcDay}`;
  const today = receipt.weeklySlot.nowUtcDay === null ? 'unknown' : String(receipt.weeklySlot.nowUtcDay);
  const lines = [
    '# Smart CI nightly coordinator',
    '',
    `- Verdict: **${markdownCell(receipt.verdict)}**`,
    `- Reasons: ${receipt.reasons.map((reason) => `\`${markdownCell(reason)}\``).join(', ')}`,
    `- Current: head \`${shortSha(receipt.current.headSha)}\`, tree \`${shortSha(receipt.current.treeSha)}\``,
    `- Last qualified: ${lastQualified}`,
    `- Duplicate qualification: **${receipt.duplicateQualification ? 'yes' : 'no'}**`
      + ` (\`${markdownCell(receipt.duplicateQualificationReason)}\`)`,
    `- Changed files: ${receipt.changedFilesAvailable ? String(receipt.changedFileCount) : 'unavailable'}`,
    `- Weekly slot: ${weekly} (today ${today}, matched ${receipt.weeklySlot.matched ? 'yes' : 'no'})`,
    `- Matched path groups: ${receipt.matchedGroups.length > 0
      ? receipt.matchedGroups.map((group) => `\`${markdownCell(group)}\``).join(', ')
      : 'none'}`,
    '',
    '## Deep suites',
    '',
    '| Suite | Decision | Reason |',
    '| --- | --- | --- |',
  ];
  const skipReasons = new Map(receipt.skippedSuites.map((entry) => [entry.suite, entry.reason]));
  for (const suite of NIGHTLY_DEEP_SUITES) {
    const selected = selectedSet.has(suite);
    const reason = selected ? receipt.verdict : skipReasons.get(suite) ?? 'not-selected';
    lines.push(`| \`${markdownCell(suite)}\` | ${selected ? 'run' : 'skip'} | ${markdownCell(reason)} |`);
  }
  if (receipt.unmappedPaths.length > 0) {
    lines.push('', `Unmapped paths: ${receipt.unmappedPaths.map((path) => `\`${markdownCell(path)}\``).join(', ')}`);
  }
  if (receipt.controlPathsChanged.length > 0) {
    lines.push('', `Changed control paths: ${receipt.controlPathsChanged.map((path) => `\`${markdownCell(path)}\``).join(', ')}`);
  }
  return `${lines.join('\n')}\n`;
}

/**
 * One path per line, or TSV `status<TAB>path<TAB>previous_path`, matching plan.mjs's list format.
 * @param {string} text
 * @returns {string[]}
 */
export function parseChangedFileList(text) {
  const paths = [];
  for (const rawLine of String(text).split(/\r?\n/)) {
    const line = rawLine.trim();
    if (!line) continue;
    const parts = line.split('\t');
    if (parts.length >= 2) {
      if (parts[1]) paths.push(parts[1]);
      if (parts[2]) paths.push(parts[2]);
    } else {
      paths.push(parts[0]);
    }
  }
  return paths;
}

export const USAGE = 'usage: nightly-coordinator.mjs --policy <file> --head-sha <sha> --tree-sha <sha>'
  + ' [--last-receipt <file>] [--changed-files <file>] [--now <iso8601>] [--weekly-slot <0-6>]'
  + ' [--force-full] [--out <file>] [--out-md <file>] [--summary <file>]';

/**
 * Parse the CLI arguments. Throws on an unknown flag so a workflow typo is loud.
 * @param {string[]} argv `process.argv.slice(2)`
 */
export function parseArgs(argv) {
  const args = {
    policy: 'ci/policy.v1.json',
    lastReceipt: null,
    headSha: null,
    treeSha: null,
    changedFiles: null,
    now: null,
    weeklySlotUtcDay: null,
    forceFull: false,
    out: null,
    outMarkdown: null,
    summary: null,
    help: false,
  };
  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index];
    const next = () => argv[++index];
    switch (arg) {
      case '--policy': args.policy = next(); break;
      case '--last-receipt': args.lastReceipt = next(); break;
      case '--head-sha': args.headSha = next(); break;
      case '--tree-sha': args.treeSha = next(); break;
      case '--changed-files': args.changedFiles = next(); break;
      case '--now': args.now = next(); break;
      case '--weekly-slot': args.weeklySlotUtcDay = Number(next()); break;
      case '--force-full': args.forceFull = true; break;
      case '--out': args.out = next(); break;
      case '--out-md': args.outMarkdown = next(); break;
      case '--summary': args.summary = next(); break;
      case '--help': args.help = true; break;
      default: throw new Error(`Unknown argument: ${arg}`);
    }
  }
  return args;
}

function writeOutput(path, contents) {
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, contents);
}

/**
 * Assemble the pure input from files and run the decision. Exported for the CLI test; it reads
 * the named files and performs no network, git or clock access.
 * @param {ReturnType<typeof parseArgs>} args
 * @returns {object} the receipt
 */
export function runCoordinator(args) {
  let policy = null;
  let digest = null;
  try {
    const policyText = readFileSync(args.policy, 'utf8');
    digest = policyDigest(policyText);
    policy = JSON.parse(policyText);
  } catch {
    policy = null;
    digest = null;
  }
  let lastQualified = null;
  let lastQualifiedReason = REASONS.lastReceiptMissing;
  if (args.lastReceipt) {
    if (!existsSync(args.lastReceipt)) {
      lastQualifiedReason = REASONS.lastReceiptMissing;
    } else {
      try {
        lastQualified = JSON.parse(readFileSync(args.lastReceipt, 'utf8'));
        lastQualifiedReason = null;
      } catch {
        lastQualified = null;
        lastQualifiedReason = REASONS.lastReceiptUnreadable;
      }
    }
  }
  let changedFiles = null;
  if (args.changedFiles && existsSync(args.changedFiles)) {
    changedFiles = parseChangedFileList(readFileSync(args.changedFiles, 'utf8'));
  }
  return decideNightlyPlan({
    policy,
    policyDigest: digest,
    lastQualified,
    lastQualifiedReason,
    headSha: args.headSha,
    treeSha: args.treeSha,
    changedFiles,
    nowUtc: args.now,
    // Passed through unchanged: `--weekly-slot friday` arrives as NaN and must escalate rather
    // than silently disabling the weekly sweep.
    weeklySlotUtcDay: args.weeklySlotUtcDay,
    forceFull: args.forceFull === true,
  });
}

function main() {
  const args = parseArgs(process.argv.slice(2));
  if (args.help) {
    console.log(USAGE);
    return;
  }
  const receipt = runCoordinator(args);
  const json = `${JSON.stringify(receipt, null, 2)}\n`;
  const markdown = renderNightlySummary(receipt);
  if (args.out) writeOutput(args.out, json);
  if (args.outMarkdown) writeOutput(args.outMarkdown, markdown);
  if (args.summary) appendFileSync(args.summary, markdown);
  process.stdout.write(markdown);
}

if (process.argv[1] && /nightly-coordinator\.mjs$/.test(process.argv[1])) main();
