import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import test from 'node:test'
import { fileURLToPath } from 'node:url'
import { K6_HARD_GATE_METRICS } from './k6-summary-contract.mjs'

const validatorPath = fileURLToPath(new URL('./require-k6-summary.mjs', import.meta.url))
const analyzerPath = fileURLToPath(new URL('./check-k6-thresholds.mjs', import.meta.url))
const fixturePath = fileURLToPath(new URL('./k6-summary-minimal.fixture.json', import.meta.url))

function runValidator(summaryPath) {
  return spawnSync(process.execPath, [validatorPath, summaryPath], {
    encoding: 'utf8',
  })
}

function runAnalyzer(summaryPath) {
  return spawnSync(process.execPath, [analyzerPath, summaryPath, '--fail-on-breach'], {
    encoding: 'utf8',
  })
}

async function runWithContents(contents, runner = runValidator) {
  const tempDir = await mkdtemp(join(tmpdir(), 'taskdeck-k6-summary-'))
  const summaryPath = join(tempDir, 'k6-summary.json')

  try {
    await writeFile(summaryPath, contents, 'utf8')
    return runner(summaryPath)
  } finally {
    await rm(tempDir, { recursive: true, force: true })
  }
}

async function loadFixture() {
  return JSON.parse(await readFile(fixturePath, 'utf8'))
}

function setMetricValue(metric, valueName, value) {
  const exportedName = valueName === 'rate' ? 'value' : valueName
  metric[exportedName] = value
}

function preserveAggregatePercentileOrder(requirement, metric, valueName, value) {
  if (requirement.name === 'http_req_duration' && valueName === 'p(95)') {
    metric['p(99)'] = Math.max(metric['p(99)'], value)
  }
}

function createBreachingValue(requirement, check) {
  const domain = requirement.valueDomains[check.valueName]
  if (check.operator === '<') {
    return domain.maximum === undefined ? check.limit + 1 : (check.limit + domain.maximum) / 2
  }

  return (domain.minimum + check.limit) / 2
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
  const result = await runWithContents('')

  assert.equal(result.status, 1)
  assert.match(result.stderr, /Required k6 summary is empty/)
})

test('fails when the required k6 summary is malformed JSON', async () => {
  const result = await runWithContents('{not-json}')

  assert.equal(result.status, 1)
  assert.match(result.stderr, /Required k6 summary is not valid JSON/)
})

test('fails when parseable JSON lacks a k6 metrics object', async () => {
  const result = await runWithContents(JSON.stringify({}))

  assert.equal(result.status, 1)
  assert.match(result.stderr, /must contain a metrics object/)
})

test('fails when the k6 metrics object is empty in both validator and analyzer paths', async () => {
  const contents = JSON.stringify({ metrics: {} })
  const validatorResult = await runWithContents(contents)
  const analyzerResult = await runWithContents(contents, runAnalyzer)

  assert.equal(validatorResult.status, 1)
  assert.match(validatorResult.stderr, /metrics object must not be empty/)
  assert.equal(analyzerResult.status, 1)
  assert.match(analyzerResult.stderr, /Invalid k6 hard-gate summary: metrics object must not be empty/)
})

for (const requirement of K6_HARD_GATE_METRICS) {
  test(`fails when required hard-gate metric ${requirement.name} is missing`, async () => {
    const summary = await loadFixture()
    delete summary.metrics[requirement.name]
    const contents = JSON.stringify(summary)

    const validatorResult = await runWithContents(contents)
    const analyzerResult = await runWithContents(contents, runAnalyzer)

    assert.equal(validatorResult.status, 1)
    assert.ok(validatorResult.stderr.includes(`required metric "${requirement.name}"`))
    assert.equal(analyzerResult.status, 1)
    assert.ok(analyzerResult.stderr.includes(`required metric "${requirement.name}"`))
  })
}

const malformedCases = [
  {
    name: 'required metric is not an object',
    mutate(summary) {
      summary.metrics.http_req_failed = null
    },
    expected: /required metric "http_req_failed" as an object/,
  },
  {
    name: 'required value is a NaN-like JSON string',
    mutate(summary) {
      summary.metrics.http_req_duration['p(95)'] = 'NaN'
    },
    expected: /finite numeric value "p\(95\)"/,
  },
  {
    name: 'required threshold is missing',
    mutate(summary) {
      delete summary.metrics.http_req_duration.thresholds['p(99)<5000']
    },
    expected: /must contain threshold "p\(99\)<5000"/,
  },
  {
    name: 'required threshold result is not boolean evidence',
    mutate(summary) {
      summary.metrics.checks.thresholds['rate>0.99'] = { ok: 'true' }
    },
    expected: /must contain boolean result evidence/,
  },
  {
    name: 'a duration value is negative',
    mutate(summary) {
      summary.metrics.http_req_duration['p(95)'] = -1
    },
    expected: /value "p\(95\)" must be at least 0/,
  },
  {
    name: 'a rate value is negative',
    mutate(summary) {
      summary.metrics.http_req_failed.value = -0.01
    },
    expected: /value "rate" must be at least 0 and at most 1/,
  },
  {
    name: 'a rate value is above one',
    mutate(summary) {
      summary.metrics.checks.value = 1.01
    },
    expected: /value "rate" must be at least 0 and at most 1/,
  },
  {
    name: 'aggregate percentile evidence is not monotonic',
    mutate(summary) {
      summary.metrics.http_req_duration['p(95)'] = 1800
      summary.metrics.http_req_duration['p(99)'] = 1700
    },
    expected: /value "p\(95\)"=1800 must not exceed "p\(99\)"=1700/,
  },
]

for (const scenario of malformedCases) {
  test(`fails when ${scenario.name}`, async () => {
    const summary = await loadFixture()
    scenario.mutate(summary)
    const contents = JSON.stringify(summary)

    const validatorResult = await runWithContents(contents)
    const analyzerResult = await runWithContents(contents, runAnalyzer)

    assert.equal(validatorResult.status, 1)
    assert.match(validatorResult.stderr, scenario.expected)
    assert.equal(analyzerResult.status, 1)
    assert.match(analyzerResult.stderr, scenario.expected)
  })
}

for (const requirement of K6_HARD_GATE_METRICS) {
  for (const thresholdName of requirement.thresholds) {
    const check = requirement.thresholdChecks[thresholdName]

    test(`rejects contradictory pass evidence for ${requirement.name} ${thresholdName}`, async () => {
      const summary = await loadFixture()
      const metric = summary.metrics[requirement.name]
      const contradictoryValue = createBreachingValue(requirement, check)
      setMetricValue(metric, check.valueName, contradictoryValue)
      preserveAggregatePercentileOrder(requirement, metric, check.valueName, contradictoryValue)
      // Numeric evidence breaches the hard gate, but flattened false claims a pass.
      metric.thresholds[thresholdName] = false

      const contents = JSON.stringify(summary)
      const validatorResult = await runWithContents(contents)
      const analyzerResult = await runWithContents(contents, runAnalyzer)

      assert.equal(validatorResult.status, 1)
      assert.match(validatorResult.stderr, /contradicts value/)
      assert.equal(analyzerResult.status, 1)
      assert.match(analyzerResult.stderr, /contradicts value/)
    })

    test(`accepts consistent equality-boundary breach evidence for ${requirement.name} ${thresholdName}`, async () => {
      const summary = await loadFixture()
      const metric = summary.metrics[requirement.name]
      setMetricValue(metric, check.valueName, check.limit)
      preserveAggregatePercentileOrder(requirement, metric, check.valueName, check.limit)
      // Equality breaches every strict hard gate, and flattened true records that breach.
      metric.thresholds[thresholdName] = true

      const contents = JSON.stringify(summary)
      const validatorResult = await runWithContents(contents)
      const analyzerResult = await runWithContents(contents, runAnalyzer)

      assert.equal(validatorResult.status, 0, validatorResult.stderr)
      assert.equal(analyzerResult.status, 1)
      assert.match(analyzerResult.stdout, /k6 threshold breached/)
    })
  }
}

test('accepts a realistic minimal k6 0.49 summary export and analyzer path', () => {
  const validatorResult = runValidator(fixturePath)
  const analyzerResult = runAnalyzer(fixturePath)

  assert.equal(validatorResult.status, 0, validatorResult.stderr)
  assert.match(validatorResult.stdout, /k6 summary validated/)
  assert.equal(analyzerResult.status, 0, analyzerResult.stderr)
  assert.match(analyzerResult.stdout, /HTTP request duration/)
  assert.match(analyzerResult.stdout, /All k6 performance thresholds passed/)
})

test('accepts nested metric values and threshold ok objects', async () => {
  const summary = await loadFixture()

  for (const requirement of K6_HARD_GATE_METRICS) {
    const metric = summary.metrics[requirement.name]
    metric.values = {}

    for (const valueName of requirement.values) {
      const exportedName = valueName === 'rate' ? 'value' : valueName
      metric.values[valueName] = metric[exportedName]
      delete metric[exportedName]
    }

    for (const thresholdName of requirement.thresholds) {
      metric.thresholds[thresholdName] = { ok: !metric.thresholds[thresholdName] }
    }
  }

  const contents = JSON.stringify(summary)
  const validatorResult = await runWithContents(contents)
  const analyzerResult = await runWithContents(contents, runAnalyzer)

  assert.equal(validatorResult.status, 0, validatorResult.stderr)
  assert.equal(analyzerResult.status, 0, analyzerResult.stderr)
  assert.match(analyzerResult.stdout, /All k6 performance thresholds passed/)
})

for (const thresholdEvidence of [false, { ok: true }]) {
  const encoding = typeof thresholdEvidence === 'boolean' ? 'flattened' : 'nested'

  test(`rejects conflicting nested and flattened metric values with ${encoding} threshold evidence`, async () => {
    const summary = await loadFixture()
    const boardWrite = summary.metrics['http_req_duration{workload:board-write}']
    boardWrite['p(95)'] = 2200
    boardWrite.values = { 'p(95)': 1900 }
    boardWrite.thresholds['p(95)<4500'] = thresholdEvidence

    const contents = JSON.stringify(summary)
    const validatorResult = await runWithContents(contents)
    const analyzerResult = await runWithContents(contents, runAnalyzer)

    assert.equal(validatorResult.status, 1)
    assert.match(validatorResult.stderr, /conflicting nested \(1900\) and flattened \(2200\) evidence/)
    assert.equal(analyzerResult.status, 1)
    assert.match(analyzerResult.stderr, /conflicting nested \(1900\) and flattened \(2200\) evidence/)
  })
}

test('accepts duplicate nested and flattened metric values only when they agree', async () => {
  const summary = await loadFixture()
  const boardWrite = summary.metrics['http_req_duration{workload:board-write}']
  boardWrite.values = { 'p(95)': boardWrite['p(95)'] }

  const contents = JSON.stringify(summary)
  const validatorResult = await runWithContents(contents)
  const analyzerResult = await runWithContents(contents, runAnalyzer)

  assert.equal(validatorResult.status, 0, validatorResult.stderr)
  assert.equal(analyzerResult.status, 0, analyzerResult.stderr)
  assert.match(analyzerResult.stdout, /All k6 performance thresholds passed/)
})

test('accepts a nested boolean breach result when the numeric evidence reaches equality', async () => {
  const summary = await loadFixture()
  summary.metrics.http_req_failed.value = 0.01
  summary.metrics.http_req_failed.thresholds['rate<0.01'] = { ok: false }

  const validatorResult = await runWithContents(JSON.stringify(summary))
  const analyzerResult = await runWithContents(JSON.stringify(summary), runAnalyzer)

  assert.equal(validatorResult.status, 0, validatorResult.stderr)
  assert.equal(analyzerResult.status, 1)
  assert.match(analyzerResult.stdout, /k6 threshold breached: http_req_failed rate<0.01/)
})

test('validator hard-gate contract matches the board-heavy k6 profile', async () => {
  const profile = await readFile(new URL('../../tests/load/k6/board-heavy-load.js', import.meta.url), 'utf8')
  const expectedThresholds = [
    'http_req_failed: ["rate<0.01"]',
    'checks: ["rate>0.99"]',
    'http_req_duration: ["p(95)<2000", "p(99)<5000"]',
    '"http_req_duration{workload:board-read}": ["p(95)<900"]',
    '"http_req_duration{workload:board-write}": ["p(95)<4500"]',
  ]

  for (const expectedThreshold of expectedThresholds) {
    assert.ok(profile.includes(expectedThreshold), `Missing profile threshold: ${expectedThreshold}`)
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
