import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import test from 'node:test'
import assert from 'node:assert/strict'

import { buildSummary } from './summarize-dependency-security-signals.mjs'

const policyDocPath = join(process.cwd(), 'docs', 'security', 'SECURITY_DEPENDENCY_VULNERABILITY_POLICY.md')

function emptyBackendReport() {
  return { projects: [] }
}

function emptyFrontendReport() {
  return {
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
  }
}

function backendReportWithFinding({
  packageId = 'System.Text.Json',
  version = '8.0.0',
  severity = 'High',
  advisoryId = 'GHSA-test-backend-0001',
} = {}) {
  return {
    projects: [
      {
        path: 'backend/src/Taskdeck.Api/Taskdeck.Api.csproj',
        frameworks: [
          {
            framework: 'net8.0',
            transitivePackages: [
              {
                id: packageId,
                resolvedVersion: version,
                vulnerabilities: [
                  {
                    severity,
                    advisoryurl: `https://github.com/advisories/${advisoryId}`,
                  },
                ],
              },
            ],
          },
        ],
      },
    ],
  }
}

function frontendReportWithFinding({
  packageId = 'vite',
  severity = 'high',
  advisoryId = 'GHSA-test-frontend-0001',
} = {}) {
  return {
    vulnerabilities: {
      [packageId]: {
        name: packageId,
        severity,
        isDirect: true,
        via: [
          {
            source: 1_000_001,
            name: packageId,
            dependency: packageId,
            title: 'Fixture advisory',
            url: `https://github.com/advisories/${advisoryId}`,
            severity,
            range: '<=1.0.0',
          },
        ],
        effects: [],
        range: '<=1.0.0',
        nodes: [`node_modules/${packageId}`],
        fixAvailable: true,
      },
    },
    metadata: {
      vulnerabilities: {
        info: 0,
        low: 0,
        moderate: 0,
        high: severity === 'high' ? 1 : 0,
        critical: severity === 'critical' ? 1 : 0,
        total: 1,
      },
    },
  }
}

async function writeScanFixture(tempDir, {
  backendReport = emptyBackendReport(),
  frontendReport = emptyFrontendReport(),
  backendExitCode = 0,
  frontendExitCode = 0,
  allowlist = null,
} = {}) {
  const paths = {
    backendReport: join(tempDir, 'backend.json'),
    frontendReport: join(tempDir, 'frontend.json'),
    backendExitCodeFile: join(tempDir, 'backend.exitcode'),
    frontendExitCodeFile: join(tempDir, 'frontend.exitcode'),
    allowlistPath: allowlist === null ? null : join(tempDir, 'dependency-allowlist.json'),
  }

  await writeFile(paths.backendReport, JSON.stringify(backendReport), 'utf8')
  await writeFile(paths.frontendReport, JSON.stringify(frontendReport), 'utf8')
  await writeFile(paths.backendExitCodeFile, String(backendExitCode), 'utf8')
  await writeFile(paths.frontendExitCodeFile, String(frontendExitCode), 'utf8')
  if (paths.allowlistPath) {
    await writeFile(paths.allowlistPath, JSON.stringify(allowlist), 'utf8')
  }

  return paths
}

function exceptionEntry({
  id,
  ecosystem,
  packageId,
  expiresOn = '2026-10-01',
} = {}) {
  return {
    id,
    ecosystem,
    package: packageId,
    exposure: 'runtime',
    reason: 'No compatible remediation is available in the supported dependency line.',
    compensatingControls: 'The affected feature is not exposed to untrusted input.',
    owner: '@taskdeck-maintainers',
    expiresOn,
  }
}

function summaryOptions(paths, overrides = {}) {
  return {
    ...paths,
    policyDoc: policyDocPath,
    summaryTitle: 'Dependency Security Signal Summary',
    workflowContext: 'ci-required',
    asOfDate: '2026-09-15',
    ...overrides,
  }
}

test('buildSummary aggregates backend and frontend vulnerability counts', async () => {
  const tempDir = await mkdtemp(join(tmpdir(), 'taskdeck-dependency-summary-'))

  try {
    const backendReport = {
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
    }
    const frontendReport = {
      vulnerabilities: {
        'happy-dom': {
          name: 'happy-dom',
          severity: 'critical',
          isDirect: true,
          via: [{
            source: 1_000_002,
            name: 'happy-dom',
            dependency: 'happy-dom',
            title: 'Fixture advisory',
            url: 'https://github.com/advisories/GHSA-test-critical-0002',
            severity: 'critical',
            range: '<=1.0.0',
          }],
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
    }
    const paths = await writeScanFixture(tempDir, {
      backendReport,
      frontendReport,
      backendExitCode: 1,
      frontendExitCode: 1,
    })

    const { summary, markdown } = await buildSummary(summaryOptions(paths, {
      workflowContext: 'nightly-quality',
    }))

    assert.equal(summary.backend.packageCount, 1)
    assert.equal(summary.backend.severityCounts.high, 1)
    assert.equal(summary.backend.severityCounts.moderate, 1)
    assert.equal(summary.frontend.severityCounts.critical, 1)
    assert.equal(summary.totals.highOrCriticalFindings, 2)
    assert.equal(summary.totals.scanFailures, 1)
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
      asOfDate: '2026-09-15',
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

test('buildSummary treats non-zero parseable scans with an audit error as enforcement failures', async () => {
  const tempDir = await mkdtemp(join(tmpdir(), 'taskdeck-dependency-summary-'))

  try {
    const paths = await writeScanFixture(tempDir, {
      frontendReport: {
        error: {
          code: 'EAUDITNETWORK',
          summary: 'mock registry/network failure',
        },
        ...emptyFrontendReport(),
      },
      frontendExitCode: 1,
    })

    const { summary } = await buildSummary(summaryOptions(paths, {
      workflowContext: 'release-security',
    }))

    assert.equal(summary.frontend.parseFailed, false)
    assert.equal(summary.totals.parseFailures, 0)
    assert.equal(summary.totals.scanFailures, 1)
    assert.equal(summary.totals.hasActionableFindings, false)
    assert.equal(summary.totals.hasEnforcementFailures, true)
  } finally {
    await rm(tempDir, { recursive: true, force: true })
  }
})

test('active per-advisory exceptions suppress only their exact NuGet and npm findings', async () => {
  const tempDir = await mkdtemp(join(tmpdir(), 'taskdeck-dependency-summary-'))

  try {
    const backendAdvisory = 'GHSA-test-backend-0001'
    const frontendAdvisory = 'GHSA-test-frontend-0001'
    const paths = await writeScanFixture(tempDir, {
      backendReport: backendReportWithFinding({ advisoryId: backendAdvisory }),
      frontendReport: frontendReportWithFinding({ advisoryId: frontendAdvisory }),
      frontendExitCode: 1,
      allowlist: {
        schemaVersion: 1,
        advisories: [
          exceptionEntry({
            id: backendAdvisory,
            ecosystem: 'nuget',
            packageId: 'System.Text.Json',
          }),
          exceptionEntry({
            id: frontendAdvisory,
            ecosystem: 'npm',
            packageId: 'vite',
          }),
        ],
      },
    })

    const { summary, markdown } = await buildSummary(summaryOptions(paths))

    assert.equal(summary.allowlist.parseFailed, false)
    assert.equal(summary.allowlist.activeEntryCount, 2)
    assert.equal(summary.allowlist.expiredEntryCount, 0)
    assert.equal(summary.backend.acceptedHighOrCriticalCount, 1)
    assert.equal(summary.frontend.acceptedHighOrCriticalCount, 1)
    assert.equal(summary.backend.unresolvedHighOrCriticalCount, 0)
    assert.equal(summary.frontend.unresolvedHighOrCriticalCount, 0)
    assert.equal(summary.totals.highOrCriticalFindings, 2)
    assert.equal(summary.totals.acceptedHighOrCriticalFindings, 2)
    assert.equal(summary.totals.unresolvedHighOrCriticalFindings, 0)
    assert.equal(summary.totals.scanFailures, 0)
    assert.equal(summary.totals.allowlistFailures, 0)
    assert.equal(summary.totals.hasActionableFindings, false)
    assert.equal(summary.totals.hasEnforcementFailures, false)
    assert.match(markdown, /Accepted high\/critical exceptions: 2/)
    assert.match(markdown, new RegExp(backendAdvisory, 'i'))
    assert.match(markdown, new RegExp(frontendAdvisory, 'i'))
  } finally {
    await rm(tempDir, { recursive: true, force: true })
  }
})

test('expired exceptions remain visible but no longer suppress their finding', async () => {
  const tempDir = await mkdtemp(join(tmpdir(), 'taskdeck-dependency-summary-'))

  try {
    const advisoryId = 'GHSA-test-expired-0001'
    const paths = await writeScanFixture(tempDir, {
      backendReport: backendReportWithFinding({ advisoryId }),
      allowlist: {
        schemaVersion: 1,
        advisories: [
          exceptionEntry({
            id: advisoryId,
            ecosystem: 'nuget',
            packageId: 'System.Text.Json',
            expiresOn: '2026-09-14',
          }),
        ],
      },
    })

    const { summary, markdown } = await buildSummary(summaryOptions(paths))

    assert.equal(summary.allowlist.activeEntryCount, 0)
    assert.equal(summary.allowlist.expiredEntryCount, 1)
    assert.equal(summary.backend.acceptedHighOrCriticalCount, 0)
    assert.equal(summary.backend.unresolvedHighOrCriticalCount, 1)
    assert.equal(summary.totals.unresolvedHighOrCriticalFindings, 1)
    assert.equal(summary.totals.hasEnforcementFailures, true)
    assert.match(markdown, /Expired allowlist entries: 1/)
  } finally {
    await rm(tempDir, { recursive: true, force: true })
  }
})

test('invalid or duplicate allowlist records fail closed even when scans are clean', async () => {
  const tempDir = await mkdtemp(join(tmpdir(), 'taskdeck-dependency-summary-'))

  try {
    const duplicate = exceptionEntry({
      id: 'GHSA-test-duplicate-0001',
      ecosystem: 'npm',
      packageId: 'vite',
    })
    const paths = await writeScanFixture(tempDir, {
      allowlist: {
        schemaVersion: 1,
        advisories: [duplicate, { ...duplicate }],
      },
    })

    const { summary, markdown } = await buildSummary(summaryOptions(paths))

    assert.equal(summary.allowlist.parseFailed, true)
    assert.equal(summary.totals.allowlistFailures, 1)
    assert.equal(summary.totals.hasActionableFindings, false)
    assert.equal(summary.totals.hasEnforcementFailures, true)
    assert.match(summary.allowlist.errors.join('\n'), /duplicate/i)
    assert.match(markdown, /Allowlist status: failed closed/)
  } finally {
    await rm(tempDir, { recursive: true, force: true })
  }
})

test('an unidentified high npm finding cannot be allowlisted by package alone', async () => {
  const tempDir = await mkdtemp(join(tmpdir(), 'taskdeck-dependency-summary-'))

  try {
    const report = frontendReportWithFinding()
    report.vulnerabilities.vite.via = []
    const paths = await writeScanFixture(tempDir, {
      frontendReport: report,
      frontendExitCode: 1,
      allowlist: {
        schemaVersion: 1,
        advisories: [
          exceptionEntry({
            id: 'GHSA-unrelated-0001',
            ecosystem: 'npm',
            packageId: 'vite',
          }),
        ],
      },
    })

    const { summary } = await buildSummary(summaryOptions(paths))

    assert.equal(summary.frontend.acceptedHighOrCriticalCount, 0)
    assert.equal(summary.frontend.unresolvedHighOrCriticalCount, 1)
    assert.equal(summary.frontend.unresolvedFindings[0].advisoryId, null)
    assert.equal(summary.totals.hasEnforcementFailures, true)
  } finally {
    await rm(tempDir, { recursive: true, force: true })
  }
})

test('required and release dependency lanes pass the canonical allowlist to the summarizer', async () => {
  const reusable = await readFile(
    join(process.cwd(), '.github', 'workflows', 'reusable-dependency-security-signals.yml'),
    'utf8',
  )
  const release = await readFile(
    join(process.cwd(), '.github', 'workflows', 'release-security.yml'),
    'utf8',
  )

  for (const [name, workflow] of [['reusable', reusable], ['release', release]]) {
    assert.match(
      workflow,
      /--allowlist \.github\/security\/dependency-allowlist\.json/,
      `${name} dependency workflow must consume the canonical per-advisory allowlist`,
    )
  }
})
