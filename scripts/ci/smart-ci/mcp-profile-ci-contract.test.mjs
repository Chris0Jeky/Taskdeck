import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';

import { buildPlan, policyDigest } from './lib/plan.mjs';

const policyText = readFileSync(new URL('../../../ci/policy.v1.json', import.meta.url), 'utf8');
const policy = JSON.parse(policyText);
const digest = policyDigest(policyText);
const apiWorkflow = readFileSync(
  new URL('../../../.github/workflows/reusable-api-integration.yml', import.meta.url),
  'utf8',
);
const BASE = 'a'.repeat(40);
const HEAD = 'b'.repeat(40);

function ownerInput(changedFiles) {
  return {
    eventName: 'pull_request_target',
    repository: 'Chris0Jeky/Taskdeck',
    pullRequestNumber: 2312,
    ref: 'main',
    isDraft: false,
    baseSha: BASE,
    headSha: HEAD,
    mergeRefQualification: 'qualified',
    mergeBaseSha: BASE,
    mergeBaseTipSha: null,
    mergeSha: 'c'.repeat(40),
    mergeTreeSha: 'd'.repeat(40),
    actorLogin: 'Chris0Jeky',
    actorType: 'User',
    authorAssociation: 'OWNER',
    isFork: false,
    labels: [],
    changedFiles,
    changedFilesAvailable: true,
    executionMode: 'hosted',
  };
}

test('every Docker MCP profile safety surface selects the hosted Windows API lane', () => {
  const protectedPaths = [
    'scripts/mcp/Test-DockerMcpProfile.ps1',
    'scripts/mcp/Set-MarketplaceMcpCredentials.ps1',
    'scripts/mcp/Test-DockerMcpProfile.Tests.ps1',
    'scripts/drills/drill-mcp-invalid-credentials.sh',
  ];

  for (const path of protectedPaths) {
    const plan = buildPlan(ownerInput([path]), policy, digest);
    assert.equal(plan.risk, 'R3', `${path} must retain the MCP process risk floor`);
    assert.ok(plan.groups.some((group) => group.id === 'mcp-process'), `${path} must match mcp-process`);
    assert.ok(
      plan.selected.some((entry) => entry.lane === 'api-integration-windows'),
      `${path} must select api-integration-windows`,
    );
  }
});

test('the Windows API lane runs the fake-backed profile suite with mandatory Bash coverage', () => {
  const step = apiWorkflow.match(
    /      - name: Validate Docker MCP profile safety regressions\n[\s\S]*?(?=\n      - name:|\s*$)/,
  )?.[0];

  assert.ok(step, 'reusable-api-integration.yml must contain the Docker MCP profile safety step');
  assert.match(step, /if: matrix\.os == 'windows-latest'/);
  assert.match(step, /shell: powershell/);
  assert.match(step, /scripts\/mcp\/Test-DockerMcpProfile\.Tests\.ps1 -RequireBash/);
});
