import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { policyDigest } from './lib/plan.mjs';
import { normaliseObservation } from './recall-report.mjs';

const repository = 'Chris0Jeky/Taskdeck';
const policyPath = fileURLToPath(new URL('../../../ci/policy.v1.json', import.meta.url));
const policyText = readFileSync(policyPath, 'utf8');
const policy = JSON.parse(policyText);
const resolverPath = fileURLToPath(new URL('./resolve-merge-ref.mjs', import.meta.url));
const plannerPath = fileURLToPath(new URL('./plan.mjs', import.meta.url));
const gatePath = fileURLToPath(new URL('./evaluate-gate.mjs', import.meta.url));

test('resolver, planner, gate receipt, and recall preserve one exact merge-base observation', async () => {
  const fixtureRoot = mkdtempSync(join(tmpdir(), 'taskdeck-merge-base-roundtrip-'));
  const origin = join(fixtureRoot, 'origin.git');
  const source = join(fixtureRoot, 'source');
  const checkout = join(fixtureRoot, 'checkout');
  const artifacts = join(fixtureRoot, 'artifacts');
  const gitEnvironment = {
    ...process.env,
    GIT_AUTHOR_NAME: 'Taskdeck Smart CI Test',
    GIT_AUTHOR_EMAIL: 'smart-ci-test@example.invalid',
    GIT_COMMITTER_NAME: 'Taskdeck Smart CI Test',
    GIT_COMMITTER_EMAIL: 'smart-ci-test@example.invalid',
  };
  const git = (cwd, ...args) => execFileSync('git', args, {
    cwd,
    encoding: 'utf8',
    env: gitEnvironment,
    stdio: ['ignore', 'pipe', 'pipe'],
  }).trim();

  try {
    git(fixtureRoot, 'init', '--bare', origin);
    git(fixtureRoot, 'init', source);
    git(source, 'config', 'user.name', gitEnvironment.GIT_AUTHOR_NAME);
    git(source, 'config', 'user.email', gitEnvironment.GIT_AUTHOR_EMAIL);
    writeFileSync(join(source, 'fixture.txt'), 'base\n');
    git(source, 'add', 'fixture.txt');
    git(source, 'commit', '-m', 'Create base');
    git(source, 'branch', '-M', 'main');
    const baseSha = git(source, 'rev-parse', 'HEAD');

    git(source, 'switch', '-c', 'feature');
    writeFileSync(join(source, 'fixture.txt'), 'head\n');
    git(source, 'commit', '-am', 'Create head');
    const headSha = git(source, 'rev-parse', 'HEAD');

    git(source, 'switch', 'main');
    git(source, 'merge', '--no-ff', '--no-edit', 'feature');
    const mergeSha = git(source, 'rev-parse', 'HEAD');
    const mergeTreeSha = git(source, 'rev-parse', 'HEAD^{tree}');
    const originUrl = pathToFileURL(origin).href;
    git(source, 'remote', 'add', 'origin', originUrl);
    git(source, 'push', 'origin',
      `${baseSha}:refs/heads/main`,
      `${headSha}:refs/heads/feature`,
      `${mergeSha}:refs/pull/1/merge`);
    git(fixtureRoot, 'clone', '--no-checkout', '--depth=1', '--branch', 'main', originUrl, checkout);

    const mergePath = join(artifacts, 'merge-sha.txt');
    const treePath = join(artifacts, 'merge-tree-sha.txt');
    const mergeBasePath = join(artifacts, 'merge-base-sha.txt');
    const mergeBaseTipPath = join(artifacts, 'merge-base-tip-sha.txt');
    execFileSync(process.execPath, [
      resolverPath,
      '--pr', '1',
      '--base', baseSha,
      '--head', headSha,
      '--base-ref', 'main',
      '--merge-out', mergePath,
      '--tree-out', treePath,
      '--merge-base-out', mergeBasePath,
      '--merge-base-tip-out', mergeBaseTipPath,
    ], {
      cwd: checkout,
      env: { ...process.env, GH_TOKEN: 'synthetic-fixture-token' },
      stdio: ['ignore', 'pipe', 'pipe'],
    });
    assert.equal(readFileSync(mergeBasePath, 'utf8').trim(), baseSha);
    assert.equal(readFileSync(mergeBaseTipPath, 'utf8').trim(), 'null');

    const eventPath = join(fixtureRoot, 'event.json');
    const changedFilesPath = join(fixtureRoot, 'changed-files.tsv');
    const planPath = join(artifacts, 'ci-plan.json');
    const receiptPath = join(artifacts, 'ci-run.json');
    const event = {
      action: 'synchronize',
      repository: { full_name: repository, owner: { login: 'Chris0Jeky' } },
      sender: { login: 'Chris0Jeky', type: 'User' },
      pull_request: {
        number: 1,
        draft: false,
        changed_files: 1,
        base: { sha: baseSha, ref: 'main', repo: { full_name: repository } },
        head: { sha: headSha, ref: 'feature', repo: { full_name: repository } },
        user: { login: 'Chris0Jeky', type: 'User' },
        author_association: 'OWNER',
        labels: [],
      },
    };
    writeFileSync(eventPath, `${JSON.stringify(event)}\n`);
    writeFileSync(changedFilesPath, 'modified\tdocs/example.md\t\n');
    execFileSync(process.execPath, [
      plannerPath,
      '--policy', policyPath,
      '--event', eventPath,
      '--event-name', 'pull_request_target',
      '--base-sha', baseSha,
      '--head-actors', 'Chris0Jeky',
      '--changed-files', changedFilesPath,
      '--changed-files-expected', '1',
      '--merge-sha', readFileSync(mergePath, 'utf8').trim(),
      '--merge-tree-sha', readFileSync(treePath, 'utf8').trim(),
      '--merge-base-sha', readFileSync(mergeBasePath, 'utf8').trim(),
      '--merge-base-tip-sha', readFileSync(mergeBaseTipPath, 'utf8').trim(),
      '--out', planPath,
    ], { stdio: ['ignore', 'pipe', 'pipe'] });
    execFileSync(process.execPath, [
      gatePath,
      '--plan', planPath,
      '--policy', policyPath,
      '--event', eventPath,
      '--event-name', 'pull_request_target',
      '--head-actors', 'Chris0Jeky',
      '--expected-head', headSha,
      '--expected-base', baseSha,
      '--plan-job-result', 'success',
      '--receipt', receiptPath,
    ], { stdio: ['ignore', 'pipe', 'pipe'] });

    const plan = JSON.parse(readFileSync(planPath, 'utf8'));
    const receipt = JSON.parse(readFileSync(receiptPath, 'utf8'));
    assert.deepEqual(
      [plan.mergeBaseSha, plan.mergeBaseTipSha, receipt.mergeBaseSha, receipt.mergeBaseTipSha],
      [baseSha, null, baseSha, null],
    );

    const mergedAt = '2026-09-01T12:00:00.000Z';
    const raw = {
      repository,
      prNumber: 1,
      mergedAt,
      headSha,
      finalHeadSha: headSha,
      headBranch: 'feature',
      headRepository: repository,
      baseSha,
      baseBranch: 'main',
      baseRepository: repository,
      mergeCommitSha: mergeSha,
      mergeCommit: { sha: mergeSha, parents: [baseSha, headSha], treeSha: mergeTreeSha },
      headPullRequests: [1],
      artifact: { id: 10, name: `smart-ci-plan-1-${headSha}`, expired: false, workflowRunId: 20, headSha, headBranch: 'feature', createdAt: '2026-09-01T09:02:00.000Z', updatedAt: '2026-09-01T09:03:00.000Z' },
      shadowRun: { id: 20, path: '.github/workflows/smart-ci-shadow.yml', event: 'pull_request_target', status: 'completed', conclusion: 'success', headSha, headBranch: 'feature', headRepository: repository, createdAt: '2026-09-01T09:00:00.000Z', updatedAt: '2026-09-01T09:05:00.000Z', pullRequests: [1] },
      plan,
      planMergeCommit: { sha: mergeSha, parents: [baseSha, headSha], treeSha: mergeTreeSha },
      requiredRun: { id: 30, path: '.github/workflows/ci-required.yml', event: 'pull_request', status: 'completed', conclusion: 'success', headSha, headBranch: 'feature', headRepository: repository, triggerCreatedAt: '2026-09-01T09:01:00.000Z', createdAt: '2026-09-01T10:00:00.000Z', updatedAt: '2026-09-01T11:00:00.000Z', runAttempt: 1, pullRequests: [1] },
      jobs: [{ name: 'Docs Governance / Docs Governance', status: 'completed', conclusion: 'success', runId: 30, runAttempt: 1, headSha }],
    };
    const recalled = normaliseObservation(raw, policy, {
      repository,
      since: '2026-09-01T00:00:00.000Z',
      until: '2026-09-01T23:59:59.000Z',
      policyDigest: policyDigest(policyText),
    });
    assert.equal(recalled.usable, true, recalled.errors.join(', '));
    assert.equal(recalled.mergeBaseSha, baseSha);
  } finally {
    rmSync(fixtureRoot, { recursive: true, force: true, maxRetries: 3, retryDelay: 100 });
  }
});
