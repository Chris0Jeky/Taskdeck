import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import test from 'node:test'
import { fileURLToPath } from 'node:url'

const validatorPath = fileURLToPath(new URL('./require-k6-summary.mjs', import.meta.url))

function runValidator(summaryPath) {
  return spawnSync(process.execPath, [validatorPath, summaryPath], {
    encoding: 'utf8',
  })
}

test('fails when the required k6 summary is missing', async () => {
  const tempDir = await mkdtemp(join(tmpdir(), 'taskdeck-k6-summary-'))
  const missingPath = join(tempDir, 'missing-k6-summary.json')

  try {
    const result = runValidator(missingPath)

    assert.equal(result.status, 1)
    assert.match(result.stderr, /Required k6 summary is missing or unreadable/)
  } finally {
    await rm(tempDir, { recursive: true, force: true })
  }
})

test('fails when the required k6 summary is empty', async () => {
  const tempDir = await mkdtemp(join(tmpdir(), 'taskdeck-k6-summary-'))
  const summaryPath = join(tempDir, 'k6-summary.json')

  try {
    await writeFile(summaryPath, '', 'utf8')
    const result = runValidator(summaryPath)

    assert.equal(result.status, 1)
    assert.match(result.stderr, /Required k6 summary is empty/)
  } finally {
    await rm(tempDir, { recursive: true, force: true })
  }
})

test('fails when the required k6 summary is malformed JSON', async () => {
  const tempDir = await mkdtemp(join(tmpdir(), 'taskdeck-k6-summary-'))
  const summaryPath = join(tempDir, 'k6-summary.json')

  try {
    await writeFile(summaryPath, '{not-json}', 'utf8')
    const result = runValidator(summaryPath)

    assert.equal(result.status, 1)
    assert.match(result.stderr, /Required k6 summary is not valid JSON/)
  } finally {
    await rm(tempDir, { recursive: true, force: true })
  }
})

test('fails when parseable JSON lacks a k6 metrics object', async () => {
  const tempDir = await mkdtemp(join(tmpdir(), 'taskdeck-k6-summary-'))
  const summaryPath = join(tempDir, 'k6-summary.json')

  try {
    await writeFile(summaryPath, JSON.stringify({}), 'utf8')
    const result = runValidator(summaryPath)

    assert.equal(result.status, 1)
    assert.match(result.stderr, /must contain a metrics object/)
  } finally {
    await rm(tempDir, { recursive: true, force: true })
  }
})

test('accepts a parseable k6 summary with a metrics object', async () => {
  const tempDir = await mkdtemp(join(tmpdir(), 'taskdeck-k6-summary-'))
  const summaryPath = join(tempDir, 'k6-summary.json')

  try {
    await writeFile(summaryPath, JSON.stringify({ metrics: {} }), 'utf8')
    const result = runValidator(summaryPath)

    assert.equal(result.status, 0, result.stderr)
    assert.match(result.stdout, /k6 summary validated/)
  } finally {
    await rm(tempDir, { recursive: true, force: true })
  }
})

test('both reusable workflows require summaries and preserve always-on artifact uploads', async () => {
  const loadWorkflow = await readFile(new URL('../../.github/workflows/reusable-load-concurrency-harness.yml', import.meta.url), 'utf8')
  const performanceWorkflow = await readFile(new URL('../../.github/workflows/reusable-performance-regression-gate.yml', import.meta.url), 'utf8')

  assert.match(
    loadWorkflow,
    /- name: Require k6 summary artifact\r?\n\s+if: always\(\)\r?\n\s+run: node scripts\/ci\/require-k6-summary\.mjs frontend\/taskdeck-web\/test-results\/load\/k6-summary\.json/,
  )
  assert.match(loadWorkflow, /- name: Upload k6 harness artifacts\r?\n\s+if: always\(\)/)

  assert.match(
    performanceWorkflow,
    /- name: Require k6 summary artifact\r?\n\s+if: always\(\)\r?\n\s+run: node scripts\/ci\/require-k6-summary\.mjs frontend\/taskdeck-web\/test-results\/perf\/k6-summary\.json/,
  )
  assert.match(performanceWorkflow, /- name: Upload performance gate artifacts\r?\n\s+if: always\(\)/)
})
