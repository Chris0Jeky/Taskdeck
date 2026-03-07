import { mkdtemp, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import test from 'node:test'
import assert from 'node:assert/strict'

import { buildSummary } from './summarize-dependency-security-signals.mjs'

test('buildSummary aggregates backend and frontend vulnerability counts', async () => {
  const tempDir = await mkdtemp(join(tmpdir(), 'taskdeck-dependency-summary-'))

  try {
    const backendReportPath = join(tempDir, 'backend.json')
    const frontendReportPath = join(tempDir, 'frontend.json')
    const backendExitCodePath = join(tempDir, 'backend.exitcode')
    const frontendExitCodePath = join(tempDir, 'frontend.exitcode')
    const policyDocPath = join(process.cwd(), 'docs', 'security', 'SECURITY_DEPENDENCY_VULNERABILITY_POLICY.md')

    await writeFile(backendReportPath, JSON.stringify({
      projects: [
        {
          path: 'backend/src/Taskdeck.Api/Taskdeck.Api.csproj',
          frameworks: [
            {
              framework: 'net8.0',
              transitivePackages: [
                {
                  id: 'System.Text.Json',
                  resolvedVersion: '8.0.0',
                  vulnerabilities: [
                    { severity: 'High', advisoryurl: 'https://advisory/high' },
                    { severity: 'Moderate', advisoryurl: 'https://advisory/moderate' },
                  ],
                },
              ],
            },
          ],
        },
      ],
    }), 'utf8')

    await writeFile(frontendReportPath, JSON.stringify({
      vulnerabilities: {
        'happy-dom': {
          name: 'happy-dom',
          severity: 'critical',
          isDirect: true,
          fixAvailable: true,
        },
      },
      metadata: {
        vulnerabilities: {
          info: 0,
          low: 0,
          moderate: 0,
          high: 0,
          critical: 1,
          total: 1,
        },
      },
    }), 'utf8')

    await writeFile(backendExitCodePath, '1', 'utf8')
    await writeFile(frontendExitCodePath, '1', 'utf8')

    const { summary, markdown } = await buildSummary({
      backendReport: backendReportPath,
      backendExitCodeFile: backendExitCodePath,
      frontendReport: frontendReportPath,
      frontendExitCodeFile: frontendExitCodePath,
      policyDoc: policyDocPath,
      summaryTitle: 'Dependency Security Signal Summary',
      workflowContext: 'nightly-quality',
    })

    assert.equal(summary.backend.packageCount, 1)
    assert.equal(summary.backend.severityCounts.high, 1)
    assert.equal(summary.backend.severityCounts.moderate, 1)
    assert.equal(summary.frontend.severityCounts.critical, 1)
    assert.equal(summary.totals.highOrCriticalFindings, 2)
    assert.equal(summary.totals.scanFailures, 2)
    assert.equal(summary.totals.hasActionableFindings, true)
    assert.equal(summary.totals.hasEnforcementFailures, true)
    assert.match(markdown, /System\.Text\.Json@8\.0\.0/)
    assert.match(markdown, /happy-dom/)
    assert.match(markdown, /SECURITY_DEPENDENCY_VULNERABILITY_POLICY\.md/)
  } finally {
    await rm(tempDir, { recursive: true, force: true })
  }
})

test('buildSummary flags parse failures without crashing', async () => {
  const tempDir = await mkdtemp(join(tmpdir(), 'taskdeck-dependency-summary-'))

  try {
    const backendReportPath = join(tempDir, 'backend.json')
    const frontendReportPath = join(tempDir, 'frontend.json')
    const backendExitCodePath = join(tempDir, 'backend.exitcode')
    const frontendExitCodePath = join(tempDir, 'frontend.exitcode')

    await writeFile(backendReportPath, '{not-json}', 'utf8')
    await writeFile(frontendReportPath, '{also-not-json}', 'utf8')
    await writeFile(backendExitCodePath, '2', 'utf8')
    await writeFile(frontendExitCodePath, '3', 'utf8')

    const { summary } = await buildSummary({
      backendReport: backendReportPath,
      backendExitCodeFile: backendExitCodePath,
      frontendReport: frontendReportPath,
      frontendExitCodeFile: frontendExitCodePath,
      policyDoc: null,
      summaryTitle: 'Dependency Security Signal Summary',
      workflowContext: 'ci-extended',
    })

    assert.equal(summary.backend.parseFailed, true)
    assert.equal(summary.frontend.parseFailed, true)
    assert.equal(summary.totals.parseFailures, 2)
    assert.equal(summary.totals.scanFailures, 2)
    assert.equal(summary.totals.hasActionableFindings, false)
    assert.equal(summary.totals.hasEnforcementFailures, true)
  } finally {
    await rm(tempDir, { recursive: true, force: true })
  }
})

test('buildSummary treats non-zero parseable scans as enforcement failures', async () => {
  const tempDir = await mkdtemp(join(tmpdir(), 'taskdeck-dependency-summary-'))

  try {
    const backendReportPath = join(tempDir, 'backend.json')
    const frontendReportPath = join(tempDir, 'frontend.json')
    const backendExitCodePath = join(tempDir, 'backend.exitcode')
    const frontendExitCodePath = join(tempDir, 'frontend.exitcode')

    await writeFile(backendReportPath, JSON.stringify({ projects: [] }), 'utf8')
    await writeFile(frontendReportPath, JSON.stringify({
      error: {
        code: 'EAUDITNETWORK',
        summary: 'mock registry/network failure',
      },
      vulnerabilities: {},
      metadata: {
        vulnerabilities: {
          info: 0,
          low: 0,
          moderate: 0,
          high: 0,
          critical: 0,
          total: 0,
        },
      },
    }), 'utf8')
    await writeFile(backendExitCodePath, '0', 'utf8')
    await writeFile(frontendExitCodePath, '1', 'utf8')

    const { summary } = await buildSummary({
      backendReport: backendReportPath,
      backendExitCodeFile: backendExitCodePath,
      frontendReport: frontendReportPath,
      frontendExitCodeFile: frontendExitCodePath,
      policyDoc: null,
      summaryTitle: 'Dependency Security Signal Summary',
      workflowContext: 'release-security',
    })

    assert.equal(summary.frontend.parseFailed, false)
    assert.equal(summary.totals.parseFailures, 0)
    assert.equal(summary.totals.scanFailures, 1)
    assert.equal(summary.totals.hasActionableFindings, false)
    assert.equal(summary.totals.hasEnforcementFailures, true)
  } finally {
    await rm(tempDir, { recursive: true, force: true })
  }
})
