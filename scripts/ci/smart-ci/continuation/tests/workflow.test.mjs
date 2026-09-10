import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { stageWorkflow } from '../tools/stage-taskdeck.mjs';

const workflow = () => readFileSync(new URL('../../../../../.github/workflows/ci-required.yml', import.meta.url), 'utf8');
function block(text, id) {
  const start = text.indexOf(`\n  ${id}:\n`); assert.ok(start >= 0, `missing ${id}`);
  return text.slice(start + 1).split(/\n  [a-z][a-z0-9-]*:\n/)[0];
}
test('checked-in required workflow satisfies minimal dependency staging', () => {
  const text = workflow(), staged = stageWorkflow(text); assert.deepEqual(staged.changes, []); assert.equal(staged.text, text);
});
test('E2E waits for frontend and existing backend prerequisites', () => {
  const b = block(workflow(), 'e2e-smoke');
  for (const id of ['frontend-unit', 'backend-unit', 'api-integration', 'migration-validation', 'backend-architecture', 'docs-governance']) assert.ok(b.includes(`      - ${id}\n`));
});
test('API does not wait for the full backend unit platform matrix', () => {
  const b = block(workflow(), 'api-integration'); assert.ok(b.includes('      - backend-architecture\n')); assert.ok(!b.includes('      - backend-unit\n'));
});
test('PR-only secret scan remains independent of push and merge-group barriers', () => {
  const text = workflow(); assert.ok(block(text, 'secret-scan').includes("if: ${{ github.event_name == 'pull_request' }}"));
  assert.ok(!text.includes('      - secret-scan\n')); assert.match(text, /^  push:/m); assert.match(text, /^  pull_request:/m); assert.match(text, /^  merge_group:/m);
});
