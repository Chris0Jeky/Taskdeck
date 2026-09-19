import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'

import {
  CLEAN_VERDICT,
  NOT_VERIFIED_VERDICT,
  SECRET_SCAN_CHECK_NAME,
  evaluateSecretScanCheckRuns,
  formatSecretScanReceipt,
} from './github/check-pre-merge-secrets-evidence.mjs'

const head = '1'.repeat(40)
const staleHead = '2'.repeat(40)

function run(overrides = {}) {
  return {
    id: 42,
    name: SECRET_SCAN_CHECK_NAME,
    head_sha: head,
    status: 'completed',
    conclusion: 'success',
    details_url: 'https://github.example/check/42',
    ...overrides,
  }
}

test('returns CLEAN only for one terminal-success exact-head Gitleaks check', () => {
  const result = evaluateSecretScanCheckRuns({ check_runs: [run()] }, head)

  assert.equal(result.verdict, CLEAN_VERDICT)
  assert.equal(result.expectedHead, head)
  assert.equal(result.observedHead, head)
  assert.equal(result.status, 'completed')
  assert.equal(result.conclusion, 'success')
  assert.match(formatSecretScanReceipt(result), /^verdict=CLEAN$/m)
})

test('missing, malformed, or stale-head evidence is NOT VERIFIED', () => {
  for (const result of [
    evaluateSecretScanCheckRuns({}, head),
    evaluateSecretScanCheckRuns({ check_runs: [] }, head),
    evaluateSecretScanCheckRuns({ check_runs: [run({ head_sha: staleHead })] }, head),
    evaluateSecretScanCheckRuns({ check_runs: [run()] }, 'short'),
  ]) {
    assert.equal(result.verdict, NOT_VERIFIED_VERDICT)
  }

  assert.match(
    evaluateSecretScanCheckRuns({ check_runs: [run({ head_sha: staleHead })] }, head).reason,
    /stale-head/u,
  )
})

test('non-terminal, unsuccessful, or URL-less evidence is NOT VERIFIED', () => {
  for (const candidate of [
    run({ status: 'queued', conclusion: null }),
    run({ status: 'in_progress', conclusion: null }),
    run({ conclusion: 'failure' }),
    run({ conclusion: 'cancelled' }),
    run({ conclusion: 'neutral' }),
    run({ details_url: '' }),
  ]) {
    assert.equal(
      evaluateSecretScanCheckRuns({ check_runs: [candidate] }, head).verdict,
      NOT_VERIFIED_VERDICT,
    )
  }
})

test('multiple exact-head check runs fail closed as ambiguous', () => {
  const result = evaluateSecretScanCheckRuns(
    { check_runs: [run({ id: 1 }), run({ id: 2 })] },
    head,
  )

  assert.equal(result.verdict, NOT_VERIFIED_VERDICT)
  assert.match(result.reason, /ambiguous/u)
})

test('the pre-merge skill names the exact evidence source and has no unconditional CLEAN template', () => {
  const skill = readFileSync(
    new URL('../.claude/skills/pre-merge-gate/SKILL.md', import.meta.url),
    'utf8',
  )

  assert.doesNotMatch(skill, /^\s*- \[ \] Secrets scan:\s*CLEAN\s*$/mu)
  assert.match(skill, /Secret Scan \/ Gitleaks Scan/u)
  assert.match(skill, /check-pre-merge-secrets-evidence\.mjs/u)
  assert.match(skill, /NOT VERIFIED/u)
  assert.match(skill, /exact PR head/u)
})
