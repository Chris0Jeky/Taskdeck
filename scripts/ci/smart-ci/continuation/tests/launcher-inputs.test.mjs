import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { taskdeckContracts } from '../adapters/taskdeck.mjs';
import { inputPatterns } from '../core/contracts.mjs';
import { matches } from '../core/primitives.mjs';

test('current canonical policy separates launcher inputs while preserving the frontend matrix', () => {
  const policy = JSON.parse(readFileSync(new URL('../../../../../ci/policy.v1.json', import.meta.url)));
  const graph = taskdeckContracts(policy);
  assert.equal(matches('backend/src/Taskdeck.Api/Program.cs', inputPatterns(graph, 'frontend-unit-linux')), false);
  assert.equal(matches('backend/src/Taskdeck.Api/Program.cs', inputPatterns(graph, 'source-launcher-linux')), true);
  assert.equal(matches('frontend/taskdeck-web/src/main.ts', inputPatterns(graph, 'source-launcher-linux')), true);
  assert.equal(matches('scripts/dev-up.sh', inputPatterns(graph, 'source-launcher-linux')), true);
  assert.ok(graph.tasks['frontend-unit-windows']);
  assert.ok(Object.values(graph.tasks).every(task => task.reviewed === false));
});
