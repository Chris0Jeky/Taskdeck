import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { validatePlan } from './lib/plan.mjs';

const repository = 'Chris0Jeky/Taskdeck';
const policyPath = fileURLToPath(new URL('../../../ci/policy.v1.json', import.meta.url));
const plannerPath = fileURLToPath(new URL('./plan.mjs', import.meta.url));
const gatePath = fileURLToPath(new URL('./evaluate-gate.mjs', import.meta.url));
const policy = JSON.parse(readFileSync(policyPath, 'utf8'));

function sha(character) {
  return character.repeat(40);
}

function writeChangedFiles(root) {
  const path = join(root, 'changed-files.tsv');
  writeFileSync(path, 'modified\tdocs/example.md\t\n');
  return path;
}

test('diagnostic gate receipts clear a malformed merge-base binding without losing the failed verdict', () => {
  const root = mkdtempSync(join(tmpdir(), 'taskdeck-merge-base-diagnostic-'));
  try {
    const baseSha = sha('1');
    const headSha = sha('2');
    const mergeSha = sha('3');
    const mergeTreeSha = sha('4');
    const eventPath = join(root, 'event.json');
    const planPath = join(root, 'ci-plan.json');
    const receiptPath = join(root, 'ci-run.json');
    const changedFilesPath = writeChangedFiles(root);
    const event = {
      action: 'synchronize',
      repository: { full_name: repository, owner: { login: 'Chris0Jeky' } },
      sender: { login: 'Chris0Jeky', type: 'User' },
      pull_request: {
        number: 2508,
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

    execFileSync(process.execPath, [
      plannerPath,
      '--policy', policyPath,
      '--event', eventPath,
      '--event-name', 'pull_request_target',
      '--head-actors', 'Chris0Jeky',
      '--changed-files', changedFilesPath,
      '--changed-files-expected', '1',
      '--merge-ref-qualification', 'qualified',
      '--merge-sha', mergeSha,
      '--merge-tree-sha', mergeTreeSha,
      '--merge-base-sha', baseSha,
      '--merge-base-tip-sha', 'null',
      '--out', planPath,
    ], { stdio: ['ignore', 'pipe', 'pipe'] });

    const malformedPlan = JSON.parse(readFileSync(planPath, 'utf8'));
    malformedPlan.mergeBaseSha = 'not-a-git-sha';
    writeFileSync(planPath, `${JSON.stringify(malformedPlan, null, 2)}\n`);

    const gate = spawnSync(process.execPath, [
      gatePath,
      '--plan', planPath,
      '--policy', policyPath,
      '--mode', 'shadow',
      '--event', eventPath,
      '--event-name', 'pull_request_target',
      '--head-actors', 'Chris0Jeky',
      '--expected-head', headSha,
      '--expected-base', baseSha,
      '--plan-job-result', 'success',
      '--receipt', receiptPath,
    ], { encoding: 'utf8' });

    assert.equal(gate.status, 1, `${gate.stdout}\n${gate.stderr}`);
    const receipt = JSON.parse(readFileSync(receiptPath, 'utf8'));
    assert.equal(receipt.ok, false);
    assert.equal(receipt.wouldFail, true);
    assert.ok(receipt.failures.some((failure) => failure.code === 'plan-invalid'));
    assert.equal(receipt.mergeBaseSha, null);
    assert.equal(receipt.mergeBaseTipSha, null);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('local what-if planning may carry a PR number without claiming a production merge binding', () => {
  const root = mkdtempSync(join(tmpdir(), 'taskdeck-local-pr-what-if-'));
  try {
    const planPath = join(root, 'ci-plan.json');
    const changedFilesPath = writeChangedFiles(root);
    execFileSync(process.execPath, [
      plannerPath,
      '--policy', policyPath,
      '--event-name', 'local',
      '--base-sha', sha('a'),
      '--head-sha', sha('b'),
      '--repository', repository,
      '--pr', '2508',
      '--changed-files', changedFilesPath,
      '--out', planPath,
    ], { stdio: ['ignore', 'pipe', 'pipe'] });

    const plan = JSON.parse(readFileSync(planPath, 'utf8'));
    assert.equal(plan.plannerError, null);
    assert.equal(plan.event.name, 'local');
    assert.equal(plan.event.pullRequest, 2508);
    assert.equal(plan.mergeRefQualification, null);
    assert.equal(plan.mergeBaseSha, null);
    assert.equal(plan.mergeBaseTipSha, null);
    assert.deepEqual(validatePlan(plan, policy), []);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
