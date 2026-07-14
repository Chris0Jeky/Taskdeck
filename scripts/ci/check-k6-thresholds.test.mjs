import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import test from 'node:test'
import { fileURLToPath } from 'node:url'

const analyzerPath = fileURLToPath(new URL('./check-k6-thresholds.mjs', import.meta.url))
const fixturePath = fileURLToPath(new URL('./k6-summary-minimal.fixture.json', import.meta.url))
const fixture = JSON.parse(await readFile(fixturePath, 'utf8'))

function createSummary(boardWriteP95, boardWriteThresholdOk) {
  const summary = structuredClone(fixture)
  const boardWrite = summary.metrics['http_req_duration{workload:board-write}']
  boardWrite['p(95)'] = boardWriteP95
  // k6 0.49 exports threshold breach flags, so false means the threshold passed.
  boardWrite.thresholds['p(95)<2200'] = !boardWriteThresholdOk
  return summary
}

async function runAnalyzer(boardWriteP95, boardWriteThresholdOk, cwd = process.cwd()) {
  const tempDir = await mkdtemp(join(tmpdir(), 'taskdeck-k6-thresholds-'))
  const summaryPath = join(tempDir, 'k6-summary.json')

  try {
    await writeFile(summaryPath, JSON.stringify(createSummary(boardWriteP95, boardWriteThresholdOk)), 'utf8')
    return spawnSync(process.execPath, [analyzerPath, summaryPath, '--fail-on-breach'], {
      cwd,
      encoding: 'utf8',
    })
  } finally {
    await rm(tempDir, { recursive: true, force: true })
  }
}

test('warns when tagged board-write p95 reaches measured SQLite capacity', async () => {
  const result = await runAnalyzer(2010, true)

  assert.equal(result.status, 0, result.stderr)
  assert.match(result.stdout, /at or above measured 2000ms SQLite capacity/)
  assert.match(result.stdout, /hard gate: 2200ms, 10% jitter allowance/)
})

test('fails when tagged board-write p95 reaches the jitter-adjusted hard gate', async () => {
  const result = await runAnalyzer(2200, false)

  assert.equal(result.status, 1)
  assert.match(result.stdout, /k6 threshold breached: http_req_duration\{workload:board-write\} p\(95\)<2200/)
  assert.match(result.stdout, /exceeds 2200ms hard gate/)
  assert.match(result.stdout, /measured SQLite capacity: 2000ms plus 10% jitter allowance/)
})

test('rejects a real k6 breach flag that contradicts the metric value', async () => {
  const result = await runAnalyzer(1000, false)

  assert.equal(result.status, 1)
  assert.match(result.stderr, /threshold "p\(95\)<2200" contradicts value "p\(95\)"=1000/)
})

test('resolves the analyzer relative to the test module from another working directory', async () => {
  const alternateCwd = await mkdtemp(join(tmpdir(), 'taskdeck-k6-cwd-'))

  try {
    const result = await runAnalyzer(2010, true, alternateCwd)

    assert.equal(result.status, 0, result.stderr)
    assert.match(result.stdout, /at or above measured 2000ms SQLite capacity/)
  } finally {
    await rm(alternateCwd, { recursive: true, force: true })
  }
})
