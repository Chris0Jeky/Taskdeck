import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { globToRegExp, matchesGlob } from './lib/glob.mjs';
import {
  PolicyError,
  buildPlan,
  classifyTrust,
  errorPlan,
  evaluateGate,
  policyDigest,
  renderGateSummary,
  renderPlanSummary,
  validatePlan,
  validatePolicy,
} from './lib/plan.mjs';
import { inputFromEvent, parseChangedFiles } from './plan.mjs';

const policyText = readFileSync(new URL('../../../ci/policy.v1.json', import.meta.url), 'utf8');
const policy = JSON.parse(policyText);
const digest = policyDigest(policyText);
const BASE = 'a'.repeat(40);
const HEAD = 'b'.repeat(40);

function ownerInput(changedFiles, overrides = {}) {
  return {
    eventName: 'pull_request_target',
    repository: 'Chris0Jeky/Taskdeck',
    pullRequestNumber: 2400,
    ref: 'main',
    isDraft: false,
    baseSha: BASE,
    headSha: HEAD,
    mergeSha: null,
    mergeTreeSha: null,
    actorLogin: 'Chris0Jeky',
    actorType: 'User',
    authorAssociation: 'OWNER',
    isFork: false,
    labels: [],
    changedFiles,
    changedFilesAvailable: true,
    executionMode: 'hosted',
    ...overrides,
  };
}

const laneIds = (plan) => plan.selected.map((entry) => entry.lane);

test('the checked-in policy is valid', () => {
  assert.deepEqual(validatePolicy(policy), []);
  assert.equal(policy.mode, 'shadow');
  assert.equal(policy.defaultExecutionMode, 'hosted');
});

test('glob semantics: ** spans directories, * stays inside one segment, root patterns stay at root', () => {
  assert.equal(matchesGlob('docs/ci/SMART_CI.md', '**/*.md'), true);
  assert.equal(matchesGlob('README.md', '**/*.md'), true);
  assert.equal(matchesGlob('docs/ci/SMART_CI.md', '*.md'), false);
  assert.equal(matchesGlob('backend/src/Taskdeck.Api/Auth/ApiKeyAuthHandler.cs', 'backend/src/**/Auth/**'), true);
  assert.equal(matchesGlob('backend/src/Taskdeck.Api/Controllers/AuthController.cs', 'backend/src/**/*Auth*'), true);
  assert.equal(matchesGlob('backend/src/Taskdeck.Api/Auth/Handler.cs', 'backend/src/**/*Auth*'), false);
  assert.equal(matchesGlob('deploy/Dockerfile.production', '**/Dockerfile*'), true);
  assert.equal(matchesGlob('.github/workflows/ci-required.yml', '.github/workflows/**'), true);
  assert.equal(matchesGlob('a/b.txt', 'a/?.txt'), true);
  assert.equal(globToRegExp('x.y').test('xay'), false);
});

test('docs-only owner PR is R0 / T1 and selects only the always-lanes, all hosted', () => {
  const plan = buildPlan(ownerInput(['docs/ci/SMART_CI.md', 'README.md']), policy, digest);
  assert.equal(plan.risk, 'R0');
  assert.equal(plan.trust, 'T1');
  assert.equal(plan.escalated, false);
  assert.deepEqual(laneIds(plan), [...policy.alwaysLanes].sort());
  assert.ok(plan.selected.every((entry) => entry.hosted === true));
  assert.equal(plan.executionMode.effective, 'hosted');
  assert.ok(plan.skipped.some((entry) => entry.lane === 'api-integration-windows'));
  assert.ok(plan.skipped.every((entry) => entry.reason.length > 0));
  assert.deepEqual(validatePlan(plan), []);
});

test('a Domain change is R2 and fans out to backend lanes without Windows or E2E', () => {
  const plan = buildPlan(ownerInput(['backend/src/Taskdeck.Domain/Entities/Board.cs']), policy, digest);
  assert.equal(plan.risk, 'R2');
  assert.equal(plan.escalated, false);
  const lanes = laneIds(plan);
  for (const lane of ['backend-unit-linux', 'backend-architecture', 'api-integration-linux', 'migration-validation']) assert.ok(lanes.includes(lane), lane);
  assert.ok(!lanes.includes('api-integration-windows'));
  assert.ok(!lanes.includes('e2e-smoke'));
  assert.ok(!lanes.includes('frontend-unit-linux'));
});

test('a capture/proposal boundary file is R3 even inside Domain (cross-cutting edge)', () => {
  const plan = buildPlan(ownerInput(['backend/src/Taskdeck.Domain/Entities/Capture.cs']), policy, digest);
  assert.equal(plan.risk, 'R3');
  assert.ok(plan.groups.some((group) => group.id === 'capture-proposal-executor'));
  assert.ok(laneIds(plan).includes('e2e-smoke'));
});

test('a migration change is R3 and adds the Windows API contract, E2E and migration validation', () => {
  const plan = buildPlan(ownerInput(['backend/src/Taskdeck.Infrastructure/Migrations/20260830_X.cs']), policy, digest);
  assert.equal(plan.risk, 'R3');
  const lanes = laneIds(plan);
  for (const lane of ['migration-validation', 'api-integration-linux', 'api-integration-windows', 'e2e-smoke', 'backend-architecture']) assert.ok(lanes.includes(lane), lane);
  assert.ok(plan.selected.find((entry) => entry.lane === 'e2e-smoke').reasons.includes('risk:R3'));
});

test('a workflow change is a control-path change: R4 / T2, escalated, every lane, hosted', () => {
  const plan = buildPlan(ownerInput(['.github/workflows/ci-required.yml', 'docs/x.md'], { executionMode: 'hybrid' }), policy, digest);
  assert.equal(plan.risk, 'R4');
  assert.equal(plan.trust, 'T2');
  assert.equal(plan.escalated, true);
  assert.ok(plan.escalationReasons.includes('control-path-change'));
  assert.deepEqual(plan.controlPathsChanged, ['.github/workflows/ci-required.yml']);
  assert.equal(plan.selected.length, Object.keys(policy.lanes).length);
  assert.equal(plan.skipped.length, 0);
  assert.equal(plan.executionMode.effective, 'hosted');
  assert.ok(plan.executionMode.hostedForced);
  assert.ok(plan.selected.every((entry) => entry.hosted === true));
});

test('an unmapped path escalates to the full hosted plan', () => {
  const plan = buildPlan(ownerInput(['ee/mystery/thing.txt'], { executionMode: 'hybrid' }), policy, digest);
  assert.equal(plan.escalated, true);
  assert.deepEqual(plan.escalationReasons, ['unmapped-path']);
  assert.deepEqual(plan.unmappedPaths, ['ee/mystery/thing.txt']);
  assert.equal(plan.selected.length, Object.keys(policy.lanes).length);
  assert.equal(plan.executionMode.effective, 'hosted');
});

test('missing changed-file list or SHAs escalates', () => {
  const noFiles = buildPlan(ownerInput([], { changedFilesAvailable: false }), policy, digest);
  assert.ok(noFiles.escalationReasons.includes('changed-files-unavailable'));
  const noHead = buildPlan(ownerInput(['docs/x.md'], { headSha: null }), policy, digest);
  assert.ok(noHead.escalationReasons.includes('base-or-head-sha-missing'));
});

test('forks, bots and untrusted associations are T3 and forced hosted', () => {
  const fork = buildPlan(ownerInput(['docs/x.md'], { isFork: true, executionMode: 'hybrid' }), policy, digest);
  assert.equal(fork.trust, 'T3');
  assert.equal(fork.executionMode.effective, 'hosted');
  const bot = buildPlan(ownerInput(['docs/x.md'], { actorLogin: 'dependabot[bot]', actorType: 'Bot', authorAssociation: 'CONTRIBUTOR' }), policy, digest);
  assert.equal(bot.trust, 'T3');
  const collaborator = buildPlan(ownerInput(['docs/x.md'], { authorAssociation: 'COLLABORATOR' }), policy, digest);
  assert.equal(collaborator.trust, 'T3');
  assert.equal(classifyTrust({ eventName: 'push', ref: 'refs/tags/v0.3.0', actorLogin: 'Chris0Jeky', authorAssociation: 'OWNER' }, [], policy), 'T4');
});

test('labels only widen: ci:full escalates, ci:hosted forces hosted, ci:windows-full adds the Windows family', () => {
  const full = buildPlan(ownerInput(['docs/x.md'], { labels: ['ci:full'] }), policy, digest);
  assert.ok(full.escalated);
  assert.ok(full.escalationReasons.includes('label:ci:full'));
  const hosted = buildPlan(ownerInput(['backend/src/Taskdeck.Domain/X.cs'], { labels: ['ci:hosted'], executionMode: 'hybrid' }), policy, digest);
  assert.equal(hosted.executionMode.effective, 'hosted');
  assert.ok(hosted.executionMode.reasons.includes('label:force-hosted'));
  const windows = buildPlan(ownerInput(['docs/x.md'], { labels: ['ci:windows-full'] }), policy, digest);
  const windowsLanes = Object.entries(policy.lanes).filter(([, lane]) => lane.family === 'windows').map(([id]) => id);
  for (const lane of windowsLanes) assert.ok(laneIds(windows).includes(lane), lane);
  assert.equal(windows.escalated, false);
});

test('hybrid mode routes trusted lanes to the isolated runner classes for a T1 change and keeps control lanes hosted', () => {
  const plan = buildPlan(ownerInput(['backend/src/Taskdeck.Application/Services/X.cs'], { executionMode: 'hybrid' }), policy, digest);
  assert.equal(plan.executionMode.effective, 'hybrid');
  const backend = plan.selected.find((entry) => entry.lane === 'backend-unit-linux');
  assert.equal(backend.runnerClass, 'selfHostedLinuxHeavy');
  assert.equal(backend.hosted, false);
  assert.ok(backend.runner.includes('self-hosted'));
  const control = plan.selected.find((entry) => entry.lane === 'smart-ci-plan');
  assert.equal(control.hosted, true);
  assert.deepEqual(validatePlan(plan), []);
});

test('plans are deterministic and independent of changed-file order', () => {
  const files = ['frontend/taskdeck-web/src/views/InboxView.vue', 'backend/src/Taskdeck.Api/Controllers/CaptureController.cs', 'docs/STATUS.md'];
  const first = JSON.stringify(buildPlan(ownerInput(files), policy, digest));
  const second = JSON.stringify(buildPlan(ownerInput([...files].reverse()), policy, digest));
  const third = JSON.stringify(buildPlan(ownerInput([...files, files[0]]), policy, digest));
  assert.equal(first, second);
  assert.equal(first, third);
});

test('overlapping groups union their lanes and take the highest risk floor', () => {
  const plan = buildPlan(ownerInput(['backend/src/Taskdeck.Api/Mcp/WriteTools.cs']), policy, digest);
  assert.equal(plan.risk, 'R3');
  assert.deepEqual(plan.groups.map((group) => group.id), ['backend-api', 'mcp-process']);
  assert.ok(laneIds(plan).includes('api-integration-windows'));
});

test('validatePolicy rejects broken references and unsafe defaults', () => {
  const broken = JSON.parse(policyText);
  broken.pathGroups[0].lanes = ['no-such-lane'];
  broken.lanes['backend-unit-linux'].hostedFallback = 'selfHostedLinuxHeavy';
  broken.riskClasses.R4.fullPlan = false;
  broken.trustClasses.T3.selfHostedAllowed = true;
  const errors = validatePolicy(broken);
  assert.ok(errors.some((error) => error.includes('no-such-lane')));
  assert.ok(errors.some((error) => error.includes('hostedFallback')));
  assert.ok(errors.some((error) => error.includes('R4.fullPlan')));
  assert.ok(errors.some((error) => error.includes('T3.selfHostedAllowed')));
  assert.throws(() => buildPlan(ownerInput(['docs/x.md']), broken, digest), PolicyError);
});

test('errorPlan selects every lane hosted and records the error', () => {
  const plan = errorPlan(ownerInput(['docs/x.md']), policy, digest, new Error('boom'));
  assert.equal(plan.plannerError.message, 'boom');
  assert.equal(plan.selected.length, Object.keys(policy.lanes).length);
  assert.ok(plan.selected.every((entry) => entry.hosted === true));
  assert.equal(plan.executionMode.effective, 'hosted');
  assert.deepEqual(validatePlan(plan), []);
});

test('the gate fails closed on missing, mismatched or errored plans in both modes', () => {
  const missing = evaluateGate(null, { mode: 'shadow' });
  assert.equal(missing.ok, false);
  assert.equal(missing.failures[0].code, 'plan-missing');
  const plan = buildPlan(ownerInput(['docs/x.md']), policy, digest);
  const shaMismatch = evaluateGate(plan, { mode: 'shadow', expectedHeadSha: 'c'.repeat(40) });
  assert.equal(shaMismatch.ok, false);
  assert.ok(shaMismatch.failures.some((failure) => failure.code === 'head-sha-mismatch'));
  const digestMismatch = evaluateGate(plan, { mode: 'shadow', expectedPolicyDigest: 'sha256:' + '0'.repeat(64) });
  assert.equal(digestMismatch.ok, false);
  const errored = evaluateGate(errorPlan(ownerInput(['docs/x.md']), policy, digest, new Error('x')), { mode: 'shadow' });
  assert.equal(errored.ok, false);
  assert.ok(errored.failures.some((failure) => failure.code === 'planner-error'));
  const planJobFailed = evaluateGate(plan, { mode: 'shadow', planJobResult: 'failure' });
  assert.equal(planJobFailed.ok, false);
});

test('the gate distinguishes shadow (report) from enforce (block) for job evidence', () => {
  const plan = buildPlan(ownerInput(['docs/x.md']), policy, digest);
  const noEvidence = evaluateGate(plan, { mode: 'shadow', expectedHeadSha: HEAD, expectedBaseSha: BASE, expectedPolicyDigest: digest, planJobResult: 'success' });
  assert.equal(noEvidence.ok, true);
  assert.equal(noEvidence.wouldFail, false);
  assert.ok(noEvidence.notes.some((note) => note.includes('no job evidence')));
  const results = Object.fromEntries(plan.selected.map((entry) => [entry.checkName, { conclusion: 'success', headSha: HEAD }]));
  assert.equal(evaluateGate(plan, { mode: 'enforce', results }).ok, true);
  results[plan.selected[0].checkName] = { conclusion: 'failure' };
  const shadow = evaluateGate(plan, { mode: 'shadow', results });
  assert.equal(shadow.ok, true);
  assert.equal(shadow.wouldFail, true);
  const enforce = evaluateGate(plan, { mode: 'enforce', results });
  assert.equal(enforce.ok, false);
  assert.ok(enforce.failures.some((failure) => failure.code === 'selected-not-success'));
  delete results[plan.selected[1].checkName];
  assert.ok(evaluateGate(plan, { mode: 'enforce', results }).failures.some((failure) => failure.code === 'selected-evidence-missing'));
  const cancelled = { ...results, [plan.selected[0].checkName]: { conclusion: 'cancelled' } };
  assert.ok(evaluateGate(plan, { mode: 'enforce', results: cancelled }).failures.some((failure) => failure.code === 'selected-not-success'));
  const wrongSha = { ...Object.fromEntries(plan.selected.map((entry) => [entry.checkName, { conclusion: 'success', headSha: 'd'.repeat(40) }])) };
  assert.ok(evaluateGate(plan, { mode: 'enforce', results: wrongSha }).failures.some((failure) => failure.code === 'evidence-wrong-sha'));
});

test('evidence without a head SHA fails in enforce mode', () => {
  const plan = buildPlan(ownerInput(['docs/x.md']), policy, digest);
  const results = Object.fromEntries(plan.selected.map((entry) => [entry.checkName, { conclusion: 'success' }]));
  const verdict = evaluateGate(plan, { mode: 'enforce', results });
  assert.equal(verdict.ok, false);
  assert.ok(verdict.failures.every((failure) => failure.code === 'evidence-sha-missing'));
});

test('a receipt whose lanes do not partition the policy is invalid when the policy is known', () => {
  const plan = buildPlan(ownerInput(['docs/x.md']), policy, digest);
  assert.deepEqual(validatePlan(plan, policy), []);
  const hollow = { ...plan, selected: [], skipped: [] };
  assert.ok(validatePlan(hollow, policy).some((error) => error.includes('exactly once')));
  assert.ok(validatePlan(hollow).some((error) => error.includes('at least one lane')));
  const renamed = { ...plan, selected: plan.selected.map((entry, index) => (index === 0 ? { ...entry, checkName: 'Something Else' } : entry)) };
  assert.ok(validatePlan(renamed, policy).some((error) => error.includes('names check')));
  const duplicated = { ...plan, skipped: [...plan.skipped, plan.skipped[0]] };
  assert.ok(validatePlan(duplicated, policy).some((error) => error.includes('more than once')));
  assert.equal(evaluateGate(hollow, { mode: 'enforce', policy, results: {} }).ok, false);
  assert.equal(evaluateGate(hollow, { mode: 'shadow', policy }).ok, false, 'a hollow receipt is a planner defect even in shadow');
});

test('a revision pushed by someone other than the trusted author is T3', () => {
  const pushedByBot = buildPlan(ownerInput(['docs/x.md'], { senderLogin: 'dependabot[bot]', senderType: 'Bot', repositoryOwnerLogin: 'Chris0Jeky', executionMode: 'hybrid' }), policy, digest);
  assert.equal(pushedByBot.trust, 'T3');
  assert.equal(pushedByBot.executionMode.effective, 'hosted');
  const pushedByStranger = buildPlan(ownerInput(['docs/x.md'], { senderLogin: 'someone-else', senderType: 'User', repositoryOwnerLogin: 'Chris0Jeky' }), policy, digest);
  assert.equal(pushedByStranger.trust, 'T3');
  const pushedByOwner = buildPlan(ownerInput(['docs/x.md'], { senderLogin: 'Chris0Jeky', senderType: 'User', repositoryOwnerLogin: 'Chris0Jeky' }), policy, digest);
  assert.equal(pushedByOwner.trust, 'T1');
  assert.equal(pushedByOwner.actor.sender, 'Chris0Jeky');
});

test('auth, executor-pipeline and infrastructure MCP files are R3 (Codex P1 gaps)', () => {
  for (const path of ['backend/src/Taskdeck.Application/Services/MfaService.cs', 'backend/src/Taskdeck.Application/Services/JwtSettings.cs', 'backend/src/Taskdeck.Application/Services/IPasswordHasher.cs', 'backend/src/Taskdeck.Api/Middleware/TokenValidationMiddleware.cs', 'backend/src/Taskdeck.Application/DTOs/OidcDtos.cs']) {
    const plan = buildPlan(ownerInput([path]), policy, digest);
    assert.equal(plan.risk, 'R3', path);
    assert.ok(plan.groups.some((group) => group.id === 'auth-security'), path);
  }
  for (const path of ['backend/src/Taskdeck.Application/Services/Pipeline/OperationHandlerRegistry.cs', 'backend/src/Taskdeck.Application/Services/Pipeline/ExecutionAuditRecorder.cs', 'backend/src/Taskdeck.Application/Services/AutomationPolicyEngine.cs']) {
    const plan = buildPlan(ownerInput([path]), policy, digest);
    assert.equal(plan.risk, 'R3', path);
    assert.ok(laneIds(plan).includes('e2e-smoke'), path);
  }
  const mcp = buildPlan(ownerInput(['backend/src/Taskdeck.Infrastructure/Mcp/StdioUserContextProvider.cs']), policy, digest);
  assert.equal(mcp.risk, 'R3');
  assert.ok(laneIds(mcp).includes('api-integration-windows'));
});

test('a plan that claims a self-hosted runner for a non-T1 or R4 change is invalid', () => {
  const plan = buildPlan(ownerInput(['docs/x.md']), policy, digest);
  const tampered = { ...plan, trust: 'T3', executionMode: { ...plan.executionMode, effective: 'hybrid' } };
  assert.ok(validatePlan(tampered).some((error) => error.includes('non-T1')));
});

test('inputFromEvent reads pull_request payloads without content and detects forks', () => {
  const event = {
    repository: { full_name: 'Chris0Jeky/Taskdeck', owner: { login: 'Chris0Jeky' } },
    pull_request: {
      number: 7,
      draft: true,
      title: 'secret title must not be copied',
      body: 'secret body',
      base: { sha: BASE, ref: 'main', repo: { full_name: 'Chris0Jeky/Taskdeck' } },
      head: { sha: HEAD, repo: { full_name: 'someone/Taskdeck' } },
      merge_commit_sha: 'e'.repeat(40),
      user: { login: 'someone', type: 'User' },
      author_association: 'NONE',
      labels: [{ name: 'ci:full' }],
    },
  };
  const input = inputFromEvent({ ...event, sender: { login: 'someone', type: 'User' } }, 'pull_request_target', { changedFiles: ['docs/x.md'], changedFilesAvailable: true });
  assert.equal(input.isFork, true);
  assert.equal(input.senderLogin, 'someone');
  assert.equal(input.repositoryOwnerLogin, 'Chris0Jeky');
  assert.equal(input.isDraft, true);
  assert.equal(input.pullRequestNumber, 7);
  assert.deepEqual(input.labels, ['ci:full']);
  assert.equal(input.mergeSha, 'e'.repeat(40));
  const plan = buildPlan(input, policy, digest);
  assert.equal(plan.trust, 'T3');
  assert.ok(!JSON.stringify(plan).includes('secret title'));
  assert.ok(!JSON.stringify(plan).includes('secret body'));
  const push = inputFromEvent({ repository: { full_name: 'o/r', owner: { login: 'o' } }, before: BASE, after: HEAD, ref: 'refs/heads/main', sender: { login: 'o', type: 'User' } }, 'push', {});
  assert.equal(push.authorAssociation, 'OWNER');
  assert.equal(push.headSha, HEAD);
});

test('parseChangedFiles accepts plain lists and status/path/previous TSV', () => {
  assert.deepEqual(parseChangedFiles('a.txt\n\nb/c.md\n'), ['a.txt', 'b/c.md']);
  assert.deepEqual(parseChangedFiles('modified\tdocs/a.md\t\nrenamed\tdocs/new.md\tdocs/old.md\n'), ['docs/a.md', 'docs/new.md', 'docs/old.md']);
});

test('summaries render without throwing and stay content-free', () => {
  const plan = buildPlan(ownerInput(['backend/src/Taskdeck.Api/Mcp/X.cs']), policy, digest);
  const summary = renderPlanSummary(plan);
  assert.match(summary, /Smart CI plan \(shadow\)/);
  assert.match(summary, /api-integration-windows/);
  const gate = renderGateSummary(evaluateGate(plan, { mode: 'shadow' }), plan);
  assert.match(gate, /Required Gate/);
});
