import { mkdtemp, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import test from 'node:test'
import assert from 'node:assert/strict'

import { buildSummary } from './summarize-dependency-security-signals.mjs'

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

async function buildFixture({
  backendReport = emptyBackendReport(),
  frontendReport = emptyFrontendReport(),
  backendExitCode = 0,
  frontendExitCode = 0,
  allowlist,
  today = '2026-09-17',
  workflowContext = 'ci-required',
} = {}) {
  const tempDir = await mkdtemp(join(tmpdir(), 'taskdeck-dependency-summary-'))

  try {
    const backendReportPath = join(tempDir, 'backend.json')
    const frontendReportPath = join(tempDir, 'frontend.json')
    const backendExitCodePath = join(tempDir, 'backend.exitcode')
    const frontendExitCodePath = join(tempDir, 'frontend.exitcode')
    const allowlistPath = allowlist === undefined ? null : join(tempDir, 'allowlist.json')
    const policyDocPath = join(process.cwd(), 'docs', 'security', 'SECURITY_DEPENDENCY_VULNERABILITY_POLICY.md')

    await writeFile(
      backendReportPath,
      typeof backendReport === 'string' ? backendReport : JSON.stringify(backendReport),
      'utf8',
    )
    await writeFile(
      frontendReportPath,
      typeof frontendReport === 'string' ? frontendReport : JSON.stringify(frontendReport),
      'utf8',
    )
    await writeFile(backendExitCodePath, String(backendExitCode), 'utf8')
    await writeFile(frontendExitCodePath, String(frontendExitCode), 'utf8')
    if (allowlistPath) {
      await writeFile(
        allowlistPath,
        typeof allowlist === 'string' ? allowlist : JSON.stringify(allowlist),
        'utf8',
      )
    }

    return await buildSummary({
      backendReport: backendReportPath,
      backendExitCodeFile: backendExitCodePath,
      frontendReport: frontendReportPath,
      frontendExitCodeFile: frontendExitCodePath,
      allowlist: allowlistPath,
      policyDoc: policyDocPath,
      summaryTitle: 'Dependency Security Signal Summary',
      workflowContext,
      today,
    })
  } finally {
    await rm(tempDir, { recursive: true, force: true })
  }
}

test('buildSummary aggregates backend and frontend vulnerability counts', async () => {
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
                  {
                    severity: 'High',
                    advisoryurl: 'https://github.com/advisories/GHSA-aaaa-bbbb-cccc',
                  },
                  {
                    severity: 'Moderate',
                    advisoryurl: 'https://github.com/advisories/GHSA-dddd-eeee-ffff',
                  },
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
        fixAvailable: true,
        via: [
          {
            source: 1234,
            name: 'happy-dom',
            severity: 'critical',
            url: 'https://github.com/advisories/GHSA-gggg-hhhh-jjjj',
          },
        ],
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

  const { summary, markdown } = await buildFixture({
    backendReport,
    frontendReport,
    backendExitCode: 1,
    frontendExitCode: 1,
    workflowContext: 'nightly-quality',
  })

  assert.equal(summary.backend.packageCount, 1)
  assert.equal(summary.backend.severityCounts.high, 1)
  assert.equal(summary.backend.severityCounts.moderate, 1)
  assert.equal(summary.frontend.severityCounts.critical, 1)
  assert.equal(summary.totals.highOrCriticalFindings, 2)
  assert.equal(summary.totals.acceptedHighOrCriticalFindings, 0)
  assert.equal(summary.totals.unresolvedHighOrCriticalFindings, 2)
  assert.equal(summary.totals.scanFailures, 1)
  assert.equal(summary.totals.hasActionableFindings, true)
  assert.equal(summary.totals.hasEnforcementFailures, true)
  assert.match(markdown, /System\.Text\.Json@8\.0\.0/)
  assert.match(markdown, /happy-dom/)
  assert.match(markdown, /SECURITY_DEPENDENCY_VULNERABILITY_POLICY\.md/)
})

test('buildSummary flags parse failures without crashing', async () => {
  const { summary } = await buildFixture({
    backendReport: '{not-json}',
    frontendReport: '{also-not-json}',
    backendExitCode: 2,
    frontendExitCode: 3,
    workflowContext: 'ci-extended',
  })

  assert.equal(summary.backend.parseFailed, true)
  assert.equal(summary.frontend.parseFailed, true)
  assert.equal(summary.totals.parseFailures, 2)
  assert.equal(summary.totals.scanFailures, 2)
  assert.equal(summary.totals.hasActionableFindings, false)
  assert.equal(summary.totals.hasEnforcementFailures, true)
})

test('buildSummary treats non-zero parseable scanner errors as enforcement failures', async () => {
  const frontendReport = {
    ...emptyFrontendReport(),
    error: {
      code: 'EAUDITNETWORK',
      summary: 'mock registry/network failure',
    },
  }

  const { summary } = await buildFixture({
    frontendReport,
    frontendExitCode: 1,
    workflowContext: 'release-security',
  })

  assert.equal(summary.frontend.parseFailed, false)
  assert.equal(summary.frontend.scanFailed, true)
  assert.equal(summary.totals.parseFailures, 0)
  assert.equal(summary.totals.scanFailures, 1)
  assert.equal(summary.totals.hasActionableFindings, false)
  assert.equal(summary.totals.hasEnforcementFailures, true)
})

test('active advisory exceptions suppress only matching high and critical findings', async () => {
  const backendReport = {
    projects: [
      {
        path: 'backend/src/Taskdeck.Api/Taskdeck.Api.csproj',
        frameworks: [
          {
            framework: 'net8.0',
            transitivePackages: [
              {
                id: 'Backend.Package',
                resolvedVersion: '1.2.3',
                vulnerabilities: [
                  {
                    severity: 'High',
                    advisoryurl: 'https://github.com/advisories/GHSA-1111-2222-3333',
                  },
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
      'frontend-package': {
        name: 'frontend-package',
        severity: 'critical',
        isDirect: true,
        fixAvailable: false,
        via: [
          {
            source: 5678,
            name: 'frontend-package',
            severity: 'critical',
            url: 'https://github.com/advisories/GHSA-4444-5555-6666',
          },
        ],
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
  const allowlist = {
    schemaVersion: 1,
    entries: [
      {
        advisoryId: 'GHSA-1111-2222-3333',
        reason: 'Backend package is build-only while the upstream fix is validated.',
        owner: '@Chris0Jeky',
        expiresOn: '2026-09-30',
      },
      {
        advisoryId: 'ghsa-4444-5555-6666',
        reason: 'Frontend package is unreachable behind a disabled feature.',
        owner: '@Chris0Jeky',
        expiresOn: '2026-09-30',
      },
    ],
  }

  const { summary, markdown } = await buildFixture({
    backendReport,
    frontendReport,
    frontendExitCode: 1,
    allowlist,
  })

  assert.equal(summary.allowlist.valid, true)
  assert.equal(summary.allowlist.activeEntryCount, 2)
  assert.equal(summary.allowlist.expiredEntryCount, 0)
  assert.equal(summary.totals.highOrCriticalFindings, 2)
  assert.equal(summary.totals.acceptedHighOrCriticalFindings, 2)
  assert.equal(summary.totals.unresolvedHighOrCriticalFindings, 0)
  assert.equal(summary.totals.scanFailures, 0)
  assert.equal(summary.totals.allowlistFailures, 0)
  assert.equal(summary.totals.hasActionableFindings, false)
  assert.equal(summary.totals.hasEnforcementFailures, false)
  assert.match(markdown, /Accepted high\/critical findings: 2/)
  assert.match(markdown, /GHSA-1111-2222-3333/)
  assert.match(markdown, /GHSA-4444-5555-6666/)
})

test('frontend package indirection resolves to the underlying advisory identity', async () => {
  const frontendReport = {
    vulnerabilities: {
      'root-package': {
        name: 'root-package',
        severity: 'high',
        isDirect: true,
        fixAvailable: true,
        via: ['transitive-package'],
      },
      'transitive-package': {
        name: 'transitive-package',
        severity: 'high',
        isDirect: false,
        fixAvailable: true,
        via: [
          {
            source: 9012,
            name: 'transitive-package',
            severity: 'high',
            url: 'https://github.com/advisories/GHSA-7777-8888-9999',
          },
        ],
      },
    },
    metadata: {
      vulnerabilities: {
        info: 0,
        low: 0,
        moderate: 0,
        high: 2,
        critical: 0,
        total: 2,
      },
    },
  }
  const allowlist = {
    schemaVersion: 1,
    entries: [
      {
        advisoryId: 'GHSA-7777-8888-9999',
        reason: 'The affected parser is not invoked by Taskdeck.',
        owner: '@Chris0Jeky',
        expiresOn: '2026-09-30',
      },
    ],
  }

  const { summary } = await buildFixture({
    frontendReport,
    frontendExitCode: 1,
    allowlist,
  })

  assert.equal(summary.frontend.highOrCriticalCount, 2)
  assert.equal(summary.frontend.acceptedHighOrCriticalCount, 2)
  assert.equal(summary.frontend.unresolvedHighOrCriticalCount, 0)
  assert.deepEqual(summary.frontend.packages[0].advisoryIds, ['GHSA-7777-8888-9999'])
  assert.deepEqual(summary.frontend.packages[1].advisoryIds, ['GHSA-7777-8888-9999'])
  assert.equal(summary.totals.hasEnforcementFailures, false)
})

test('expired exceptions do not suppress findings and fail enforcement visibly', async () => {
  const backendReport = {
    projects: [
      {
        path: 'backend/src/Taskdeck.Api/Taskdeck.Api.csproj',
        frameworks: [
          {
            framework: 'net8.0',
            topLevelPackages: [
              {
                id: 'Expired.Package',
                resolvedVersion: '4.5.6',
                vulnerabilities: [
                  {
                    severity: 'Critical',
                    advisoryurl: 'https://github.com/advisories/GHSA-abcd-efgh-jkmn',
                  },
                ],
              },
            ],
          },
        ],
      },
    ],
  }
  const allowlist = {
    schemaVersion: 1,
    entries: [
      {
        advisoryId: 'GHSA-abcd-efgh-jkmn',
        reason: 'Temporary exception that has reached its review deadline.',
        owner: '@Chris0Jeky',
        expiresOn: '2026-09-16',
      },
    ],
  }

  const { summary, markdown } = await buildFixture({ backendReport, allowlist })

  assert.equal(summary.allowlist.valid, true)
  assert.equal(summary.allowlist.activeEntryCount, 0)
  assert.equal(summary.allowlist.expiredEntryCount, 1)
  assert.equal(summary.backend.acceptedHighOrCriticalCount, 0)
  assert.equal(summary.backend.unresolvedHighOrCriticalCount, 1)
  assert.equal(summary.totals.allowlistFailures, 1)
  assert.equal(summary.totals.hasActionableFindings, true)
  assert.equal(summary.totals.hasEnforcementFailures, true)
  assert.match(markdown, /Expired exceptions: 1/)
})

test('invalid allowlist configuration fails closed even when scans are clean', async () => {
  const allowlist = {
    schemaVersion: 2,
    entries: [],
  }

  const { summary, markdown } = await buildFixture({ allowlist })

  assert.equal(summary.allowlist.valid, false)
  assert.equal(summary.allowlist.errorCount, 1)
  assert.equal(summary.totals.highOrCriticalFindings, 0)
  assert.equal(summary.totals.allowlistFailures, 1)
  assert.equal(summary.totals.hasEnforcementFailures, true)
  assert.match(markdown, /Allowlist status: invalid/)
})
