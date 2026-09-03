import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';

const WORKFLOW_ROOT = fileURLToPath(new URL('../../../.github/workflows/', import.meta.url));

function reusableWorkflowPaths() {
  return readdirSync(WORKFLOW_ROOT)
    .filter((name) => /^reusable-.*\.ya?ml$/.test(name))
    .sort()
    .map((name) => join(WORKFLOW_ROOT, name));
}

function indentation(line) {
  return line.length - line.trimStart().length;
}

function uploadSteps(workflowPath) {
  const lines = readFileSync(workflowPath, 'utf8').split(/\r?\n/);
  const uploads = [];

  for (let actionIndex = 0; actionIndex < lines.length; actionIndex += 1) {
    if (!/^\s*uses:\s*actions\/upload-artifact@/.test(lines[actionIndex])) continue;

    const stepIndent = indentation(lines[actionIndex]) - 2;
    let start = actionIndex;
    while (start > 0 && !(indentation(lines[start]) === stepIndent && /^\s*- name:/.test(lines[start]))) start -= 1;

    let end = actionIndex + 1;
    while (end < lines.length && !(indentation(lines[end]) === stepIndent && /^\s*- name:/.test(lines[end]))) end += 1;

    const block = lines.slice(start, end);
    const condition = block.find((line) => /^\s*if:\s*/.test(line))?.replace(/^\s*if:\s*/, '').trim() ?? '';
    const retention = block.find((line) => /^\s*retention-days:\s*/.test(line))?.replace(/^\s*retention-days:\s*/, '').trim() ?? null;
    uploads.push({
      line: actionIndex + 1,
      name: block[0]?.replace(/^\s*- name:\s*/, '').trim() ?? '(unnamed step)',
      condition,
      retention,
      failureConditioned: /\bfailure\s*\(\s*\)/i.test(condition)
        || /\.outcome\s*==\s*['"]failure['"]/i.test(condition),
    });
  }

  return uploads;
}

function describe(step, workflowPath) {
  return `${workflowPath}:${step.line} (${step.name})`;
}

test('reusable upload-artifact steps declare retention, with failure evidence bounded to 1-14 days', () => {
  const violations = [];

  for (const workflowPath of reusableWorkflowPaths()) {
    for (const step of uploadSteps(workflowPath)) {
      if (step.retention === null) {
        violations.push(`${describe(step, workflowPath)} has no retention-days`);
        continue;
      }

      if (step.failureConditioned && !/^(?:[1-9]|1[0-4])$/.test(step.retention)) {
        violations.push(`${describe(step, workflowPath)} has failure retention ${JSON.stringify(step.retention)}, expected an integer from 1 to 14`);
      }
    }
  }

  assert.deepEqual(violations, [], violations.join('\n'));
});

test('the scan covers all reusable workflow files deterministically', () => {
  const paths = reusableWorkflowPaths();
  assert.ok(paths.length > 0, 'expected reusable workflows');
  assert.deepEqual(paths, [...paths].sort());
  assert.ok(paths.some((path) => path.endsWith('reusable-api-integration.yml')));
});
