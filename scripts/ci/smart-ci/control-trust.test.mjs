import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readdirSync, readFileSync } from 'node:fs';
import { test } from 'node:test';

const WORKFLOW_DIRECTORY = new URL('../../../.github/workflows/', import.meta.url);
const SUPPORTED_WORKFLOW = 'smart-ci-shadow.yml';
const EXPECTED_JOB_IDS = ['plan', 'required-gate'];
const EXPECTED_WORKFLOW_FINGERPRINT = '3d1c24768d218402f250f7c6fef7c02f113984811113ef449cdf8bb6dff1d182';

// These fingerprints cover executable step configuration. Step names and comment-only lines are
// deliberately omitted. A changed action, input, environment binding, condition, or run body must
// be reviewed and pinned here.
const EXPECTED_STEP_FINGERPRINTS = {
  plan: [
    '5e81be0b4349af0757f4a5139ce97788464442125fe14495ad861bb82921a8f4',
    'a6342a9bd46d724db04e7559ae7b51b73153a22b474d52167f86da0934f1a2c3',
    '6ee8ac0fb69ff35cc55766271d8547c83c1133f77cb21e546b51074bd026d12a',
    '3e6e7741bbee62150621607b3a3a686c291f896e4268c9adef72a914a57bdcdc',
    'f4e7fb7d1587380193bb4b87dc9a48802923412c47485d7c76e7f7a4de5008d9',
    '0b1a3547a5895f0f13c4ebf219755c818193cc8fa88d7cbebb6857ba72757508',
    '5be7249fea7aef1261b2b749b3bbc3e2ad69333891b95bf7135b53f02c36eff2',
  ],
  'required-gate': [
    '5e81be0b4349af0757f4a5139ce97788464442125fe14495ad861bb82921a8f4',
    'a6342a9bd46d724db04e7559ae7b51b73153a22b474d52167f86da0934f1a2c3',
    'dedcf48f7d4f188d5576c1b48cd7bf3b73ae0052454b379c674aa0be7a9c1dde',
    '7fe35769100ca67255c8168da31c830a2dd972c4d2d12a7fc5b613198b2ca6f3',
    'b2f7f977c410e73c5dc29e0acf3f4216b025f4b638e2476155757bda6381e54e',
  ],
};

function indentation(line) {
  const match = line.match(/^ */);
  return match ? match[0].length : 0;
}

function uncommentedLines(text) {
  return text.replaceAll('\r\n', '\n').split('\n')
    .filter((line) => !/^\s*#/.test(line));
}

function declaresPullRequestTarget(text) {
  return uncommentedLines(text).some((line) => /(^|\s)pull_request_target(?:\s|:|$)/.test(line));
}

function hasSupportedPullRequestTargetTrigger(text) {
  const lines = uncommentedLines(text);
  const onIndex = lines.findIndex((line) => line === 'on:');
  if (onIndex === -1) return false;
  for (let index = onIndex + 1; index < lines.length; index += 1) {
    if (lines[index].trim() && indentation(lines[index]) === 0) break;
    if (lines[index] === '  pull_request_target:') return true;
  }
  return false;
}

function parseSteps(workflowText) {
  const lines = workflowText.replaceAll('\r\n', '\n').split('\n');
  const jobsIndex = lines.findIndex((line) => line === 'jobs:');
  assert.notEqual(jobsIndex, -1, 'pull_request_target workflow must declare a jobs mapping');

  const jobs = new Map();
  let job = null;
  let step = null;
  let inSteps = false;
  for (let index = jobsIndex + 1; index < lines.length; index += 1) {
    const line = lines[index];
    const jobMatch = line.match(/^  ([A-Za-z0-9_-]+):\s*$/);
    if (jobMatch) {
      job = { id: jobMatch[1], steps: [] };
      jobs.set(job.id, job);
      step = null;
      inSteps = false;
      continue;
    }
    if (!job) continue;
    if (line === '    steps:') {
      inSteps = true;
      continue;
    }
    if (!inSteps) continue;

    const stepMatch = line.match(/^      - name:\s*(.+)$/);
    if (stepMatch) {
      step = { name: stepMatch[1], raw: [], uses: null, ref: null, run: '' };
      job.steps.push(step);
      continue;
    }
    assert.doesNotMatch(line, /^      - /,
      `${job.id}: unsupported unnamed step; extend the execution parser deliberately`);
    if (!step) continue;
    if (line.trim() && indentation(line) <= 6) {
      step = null;
      continue;
    }

    step.raw.push(line);
    const usesMatch = line.match(/^        uses:\s*(\S+)\s*(?:#.*)?$/);
    if (usesMatch) step.uses = usesMatch[1];
    const refMatch = line.match(/^          ref:\s*(.+?)\s*(?:#.*)?$/);
    if (refMatch) step.ref = refMatch[1];
    if (/^        run:\s*\|[-+]?\s*$/.test(line)) {
      const body = [];
      while (index + 1 < lines.length && (!lines[index + 1].trim() || indentation(lines[index + 1]) >= 10)) {
        index += 1;
        body.push(lines[index].startsWith('          ') ? lines[index].slice(10) : lines[index]);
        step.raw.push(lines[index]);
      }
      step.run = body.join('\n').trimEnd();
    }
  }
  return jobs;
}

function fingerprintStep(step) {
  const executableShape = step.raw
    .filter((line) => !/^\s*#/.test(line))
    .map((line) => line.trimEnd())
    .join('\n')
    .trimEnd();
  return createHash('sha256').update(executableShape).digest('hex');
}

function fingerprintWorkflow(workflowText) {
  const executionShape = workflowText.replaceAll('\r\n', '\n').split('\n')
    .filter((line) => !/^\s*#/.test(line) && !/^      - name:/.test(line))
    .map((line) => line.trimEnd())
    .join('\n')
    .trimEnd();
  return createHash('sha256').update(executionShape).digest('hex');
}

function assertNoFetchedHeadExecution(step, context) {
  if (step.uses?.startsWith('actions/checkout@')) {
    assert.equal(step.ref, null,
      `${context}: actions/checkout must omit ref so pull_request_target checks out the protected base`);
  }

  // GitHub evaluates expressions before handing a run body to the shell. A
  // comment is therefore not inert when its expression can expand to a
  // newline: a PR-controlled multiline value would put subsequent text on its
  // own executable line. Keep comment-only prose out of the fingerprint, but
  // fail closed for an expression hidden in a run-block comment.
  assert.doesNotMatch(step.run,
    /^\s*#.*\$\{\{/m,
    `${context}: a run-block comment must not interpolate a GitHub expression`);

  assert.doesNotMatch(step.run,
    /\bgit\s+(?:-[^\s]+\s+)*(?:checkout|switch|reset|restore|read-tree|worktree)\b/i,
    `${context}: a pull_request_target run step must not replace or create a worktree from fetched objects`);
  assert.doesNotMatch(step.run,
    /\bgit\s+(?:-[^\s]+\s+)*(?:show|cat-file|archive)\b[^\r\n|]*(?:CONTROL_HEAD|EXPECTED_HEAD|pull_request\.head)[^\r\n|]*\|\s*(?:ba)?sh\b/i,
    `${context}: a pull_request_target run step must not pipe fetched head content into a shell`);
}

function assertControlTrust(workflows) {
  const targetWorkflows = [];
  for (const [name, text] of workflows) {
    if (!declaresPullRequestTarget(text)) continue;
    assert.equal(hasSupportedPullRequestTargetTrigger(text), true,
      `${name}: unsupported pull_request_target trigger shape; extend this contract deliberately`);
    targetWorkflows.push(name);
  }
  assert.deepEqual(targetWorkflows.sort(), [SUPPORTED_WORKFLOW],
    'the pull_request_target trust boundary is pinned to the one reviewed control workflow');

  const jobs = parseSteps(workflows.get(SUPPORTED_WORKFLOW));
  assert.deepEqual([...jobs.keys()], EXPECTED_JOB_IDS,
    'the pull_request_target execution graph is pinned to the reviewed plan and gate jobs');

  for (const [jobId, job] of jobs) {
    assert.ok(job.steps.length > 0, `${jobId}: job must contain steps`);
    assert.ok(job.steps[0].uses?.startsWith('actions/checkout@'),
      `${jobId}: the first executable step must check out the protected base`);
    assert.equal(job.steps.filter((step) => step.uses?.startsWith('actions/checkout@')).length, 1,
      `${jobId}: exactly one base checkout is supported`);
    for (const step of job.steps) assertNoFetchedHeadExecution(step, `${jobId}/${step.name}`);

    assert.deepEqual(job.steps.map(fingerprintStep), EXPECTED_STEP_FINGERPRINTS[jobId],
      `${jobId}: executable step shape changed; review the trust boundary and update its fingerprints`);
  }

  assert.equal(fingerprintWorkflow(workflows.get(SUPPORTED_WORKFLOW)), EXPECTED_WORKFLOW_FINGERPRINT,
    'workflow execution envelope changed; review the pull_request_target trust boundary explicitly');
}

function checkedInWorkflows() {
  return new Map(readdirSync(WORKFLOW_DIRECTORY, { withFileTypes: true })
    .filter((entry) => entry.isFile() && /\.ya?ml$/i.test(entry.name))
    .map((entry) => [entry.name, readFileSync(new URL(entry.name, WORKFLOW_DIRECTORY), 'utf8')]));
}

test('every pull_request_target job keeps execution on the protected base', () => {
  assertControlTrust(checkedInWorkflows());
});

test('the contract rejects a PR-head checkout', () => {
  const workflows = checkedInWorkflows();
  workflows.set(SUPPORTED_WORKFLOW, workflows.get(SUPPORTED_WORKFLOW).replace(
    '          fetch-depth: 1',
    '          ref: ${{ github.event.pull_request.head.sha }}\n          fetch-depth: 1',
  ));
  assert.throws(() => assertControlTrust(workflows), /actions\/checkout must omit ref/);
});

test('the contract rejects execution of a fetched head object', () => {
  const workflows = checkedInWorkflows();
  workflows.set(SUPPORTED_WORKFLOW, workflows.get(SUPPORTED_WORKFLOW).replace(
    '            --note-out artifacts/merge-ref-note.txt',
    '            --note-out artifacts/merge-ref-note.txt\n          git show "${CONTROL_HEAD}:payload.sh" | bash',
  ));
  assert.throws(() => assertControlTrust(workflows), /must not pipe fetched head content into a shell/);
});

test('the contract rejects a GitHub expression hidden in a run-block comment', () => {
  const workflows = checkedInWorkflows();
  workflows.set(SUPPORTED_WORKFLOW, workflows.get(SUPPORTED_WORKFLOW).replace(
    '            --note-out artifacts/merge-ref-note.txt',
    '            --note-out artifacts/merge-ref-note.txt\n          # ${{ github.event.pull_request.body }}',
  ));
  assert.throws(() => assertControlTrust(workflows), /run-block comment must not interpolate a GitHub expression/);
});
