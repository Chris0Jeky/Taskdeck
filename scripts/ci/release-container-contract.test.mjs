// =============================================================================
// release-container-contract.test.mjs — executable release-container invariants (#1854)
// =============================================================================
//
// The workflow itself cannot be executed safely on an ordinary PR, so the
// version resolver is exercised as a real process and the remaining workflow
// wiring is pinned structurally.
//
// Run: node --test scripts/ci/release-container-contract.test.mjs
// =============================================================================

import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { readFileSync } from 'node:fs'
import test from 'node:test'
import { fileURLToPath } from 'node:url'

const repoRoot = fileURLToPath(new URL('../../', import.meta.url))
const workflowPath = fileURLToPath(new URL('../../.github/workflows/release-container.yml', import.meta.url))
const workflow = readFileSync(workflowPath, 'utf8').replace(/\r\n/g, '\n')
const bashBin = process.platform === 'win32' ? (process.env.BASH_BIN || 'bash') : 'bash'
const healthVersionParser = String.raw`sed -n 's/.*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p'`

function resolveContainerVersion(ref) {
  return spawnSync(bashBin, ['scripts/ci/resolve-container-version.sh', ref], {
    cwd: repoRoot,
    encoding: 'utf8',
  })
}

function parseHealthVersion(payload) {
  return spawnSync(bashBin, ['-c', healthVersionParser], {
    cwd: repoRoot,
    encoding: 'utf8',
    input: payload,
  })
}

function stepBlock(stepName) {
  const start = workflow.indexOf(`      - name: ${stepName}\n`)
  assert.notEqual(start, -1, `step ${stepName} must exist`)
  const rest = workflow.slice(start)
  const next = rest.indexOf('\n      - name: ', 1)
  return next === -1 ? rest : rest.slice(0, next)
}

for (const [ref, expected] of [
  ['refs/tags/v1.2.3', '1.2.3'],
  ['refs/tags/v1.2.3+build.5', '1.2.3'],
  ['refs/tags/v1.2.3-rc.1+meta', '1.2.3-rc.1'],
  ['refs/heads/release-rehearsal', '0.0.0-dev'],
]) {
  test(`resolves ${ref} to ${expected}`, () => {
    const result = resolveContainerVersion(ref)
    assert.equal(result.status, 0, result.stderr)
    assert.equal(result.stdout.trim(), expected)
  })
}

test('rejects a v-prefixed tag that is not valid release semver', () => {
  const result = resolveContainerVersion('refs/tags/v1.2')
  assert.equal(result.status, 1)
  assert.equal(result.stdout, '')
  assert.match(result.stderr, /::error::/)
})

test('the health version parser extracts the version without an early-closing pipeline', () => {
  const result = parseHealthVersion('{"status":"Healthy","version":"1.2.3","timestamp":"now"}\n')
  assert.equal(result.status, 0, result.stderr)
  assert.equal(result.stdout.trim(), '1.2.3')
})

test('the health version parser returns empty successfully when the field is absent', () => {
  const result = parseHealthVersion('{"status":"Healthy","timestamp":"now"}\n')
  assert.equal(result.status, 0, result.stderr)
  assert.equal(result.stdout, '')
})

test('the workflow delegates product-version derivation to the tested resolver before metadata', () => {
  const resolveStep = stepBlock('Resolve product version')
  assert.match(
    resolveStep,
    /VERSION="\$\(bash scripts\/ci\/resolve-container-version\.sh "\$GITHUB_REF"\)"/,
  )
  assert.ok(
    workflow.indexOf('      - name: Resolve product version\n') <
      workflow.indexOf('      - name: Compute image metadata\n'),
    'invalid tag shapes must fail before docker/metadata-action processes them',
  )
})

test('the tag smoke test fails at the first failing command and always cleans up', () => {
  const smokeStep = stepBlock('Smoke-test the built image (tag refs)')
  assert.match(smokeStep, /run: \|\n          set -euo pipefail\n/)
  assert.ok(
    smokeStep.indexOf('set -euo pipefail') < smokeStep.indexOf('docker run -d'),
    'docker run must execute under fail-fast shell semantics',
  )
  assert.match(
    smokeStep,
    /trap 'docker rm -f taskdeck-smoke >\/dev\/null 2>&1 \|\| true' EXIT/,
    'fail-fast exits must still remove a partially started smoke container',
  )
  assert.ok(
    smokeStep.includes(healthVersionParser),
    'the workflow must use the tested no-match-safe version parser',
  )
  assert.doesNotMatch(
    smokeStep,
    /grep -o[\s\S]*head -1/,
    'the version parser must not reintroduce an early-closing grep/head pipeline under pipefail',
  )
})
