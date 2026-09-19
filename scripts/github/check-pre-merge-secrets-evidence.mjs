#!/usr/bin/env node

import { execFileSync } from 'node:child_process'
import { pathToFileURL } from 'node:url'
import path from 'node:path'

export const SECRET_SCAN_CHECK_NAME = 'Secret Scan / Gitleaks Scan'
export const CLEAN_VERDICT = 'CLEAN'
export const NOT_VERIFIED_VERDICT = 'NOT VERIFIED'

function receipt(overrides = {}) {
  return {
    verdict: NOT_VERIFIED_VERDICT,
    checkName: SECRET_SCAN_CHECK_NAME,
    expectedHead: null,
    observedHead: null,
    status: null,
    conclusion: null,
    detailsUrl: null,
    reason: 'secret-scan evidence was not verified',
    ...overrides,
  }
}

export function evaluateSecretScanCheckRuns(payload, expectedHead) {
  if (typeof expectedHead !== 'string' || !/^[0-9a-f]{40}$/u.test(expectedHead)) {
    return receipt({ expectedHead, reason: 'expected head must be a full 40-character commit SHA' })
  }

  if (!payload || !Array.isArray(payload.check_runs)) {
    return receipt({ expectedHead, reason: 'GitHub check-runs response has no check_runs array' })
  }

  const namedRuns = payload.check_runs.filter((run) => run?.name === SECRET_SCAN_CHECK_NAME)
  const exactRuns = namedRuns.filter((run) => run?.head_sha === expectedHead)

  if (exactRuns.length === 0) {
    const staleHeads = [...new Set(namedRuns.map((run) => run?.head_sha).filter(Boolean))]
    return receipt({
      expectedHead,
      observedHead: staleHeads.length === 1 ? staleHeads[0] : null,
      reason:
        namedRuns.length === 0
          ? `no ${SECRET_SCAN_CHECK_NAME} check run exists for the exact PR head`
          : `only stale-head ${SECRET_SCAN_CHECK_NAME} evidence was returned: ${staleHeads.join(', ')}`,
    })
  }

  if (exactRuns.length !== 1) {
    return receipt({
      expectedHead,
      observedHead: expectedHead,
      reason: `ambiguous ${SECRET_SCAN_CHECK_NAME} evidence: ${exactRuns.length} exact-head runs were returned`,
    })
  }

  const run = exactRuns[0]
  const base = {
    expectedHead,
    observedHead: run.head_sha ?? null,
    status: run.status ?? null,
    conclusion: run.conclusion ?? null,
    detailsUrl: run.details_url ?? null,
  }

  if (run.status !== 'completed') {
    return receipt({
      ...base,
      reason: `${SECRET_SCAN_CHECK_NAME} is ${run.status ?? 'in an unknown state'}, not completed`,
    })
  }

  if (run.conclusion !== 'success') {
    return receipt({
      ...base,
      reason: `${SECRET_SCAN_CHECK_NAME} completed with conclusion ${run.conclusion ?? 'unknown'}, not success`,
    })
  }

  if (typeof run.details_url !== 'string' || run.details_url.trim().length === 0) {
    return receipt({
      ...base,
      reason: `${SECRET_SCAN_CHECK_NAME} has no check-run URL for the evidence receipt`,
    })
  }

  return receipt({
    ...base,
    verdict: CLEAN_VERDICT,
    reason: `${SECRET_SCAN_CHECK_NAME} completed successfully on the exact PR head`,
  })
}

function parseArguments(argv) {
  const values = new Map()
  for (let index = 0; index < argv.length; index += 1) {
    const token = argv[index]
    if (!['--repo', '--head'].includes(token)) {
      throw new Error(`unsupported argument: ${token}`)
    }
    const value = argv[index + 1]
    if (!value || value.startsWith('--')) {
      throw new Error(`${token} requires a value`)
    }
    values.set(token, value)
    index += 1
  }

  const repo = values.get('--repo')
  const head = values.get('--head')
  if (!repo || !/^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/u.test(repo)) {
    throw new Error('--repo must be an owner/name repository')
  }
  if (!head) {
    throw new Error('--head is required')
  }
  return { repo, head }
}

export function formatSecretScanReceipt(result) {
  const value = (candidate) =>
    String(candidate ?? '-').replace(/[\t\r\n]/gu, ' ').trim() || '-'
  return [
    `verdict=${value(result.verdict)}`,
    `check=${value(result.checkName)}`,
    `expected_head=${value(result.expectedHead)}`,
    `observed_head=${value(result.observedHead)}`,
    `status=${value(result.status)}`,
    `conclusion=${value(result.conclusion)}`,
    `url=${value(result.detailsUrl)}`,
    `reason=${value(result.reason)}`,
  ].join('\n')
}

function loadCheckRuns(repo, head) {
  const endpoint = `repos/${repo}/commits/${head}/check-runs?filter=latest&per_page=100`
  const output = execFileSync(
    'gh',
    ['api', '--method', 'GET', '-H', 'Accept: application/vnd.github+json', endpoint],
    { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] },
  )
  return JSON.parse(output)
}

function isDirectExecution() {
  if (!process.argv[1]) return false
  return import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href
}

if (isDirectExecution()) {
  let requestedHead = null
  try {
    const { repo, head } = parseArguments(process.argv.slice(2))
    requestedHead = head
    const result = evaluateSecretScanCheckRuns(loadCheckRuns(repo, head), head)
    console.log(formatSecretScanReceipt(result))
    process.exitCode = result.verdict === CLEAN_VERDICT ? 0 : 2
  } catch (error) {
    const result = receipt({
      expectedHead: requestedHead,
      reason: `helper execution failed: ${error.message}`,
    })
    console.log(formatSecretScanReceipt(result))
    console.error(`secrets evidence check failed: ${error.message}`)
    process.exitCode = 1
  }
}
